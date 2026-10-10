import AppKit
import CoreAudio
import Foundation

extension AppModel {
    func openTool(_ kind: ToolKind) { guard !busy, kind != .dialogue || project.mode == .dialogue else { return }; if kind == .clone { cloneCreated = false; cloneStatus = "Ready. Add samples and confirm consent before creating a voice." }; tool = kind; catalog = []; selectedCatalog = []; cursor = ""; search = ""; if [.voices, .history, .dictionaries].contains(kind) { refreshTool() } }
    func refreshTool(next: Bool = false) {
        guard let kind = tool, !next || !cursor.isEmpty else { return }; let page = next ? cursor : "", query = Self.escape(search), shared = sharedVoices
        run("Loading \(kind.rawValue)") {
            let path: String
            switch kind {
            case .voices: path = shared ? "/v1/shared-voices?page_size=100&search=\(query)" + (page.isEmpty ? "" : "&page=\(page)") : "/v2/voices?page_size=100&search=\(query)" + (page.isEmpty ? "" : "&next_page_token=\(Self.escape(page))")
            case .history: path = "/v1/history?page_size=100" + (page.isEmpty ? "" : "&start_after_history_item_id=\(Self.escape(page))")
            default: path = "/v1/pronunciation-dictionaries?page_size=100" + (page.isEmpty ? "" : "&cursor=\(Self.escape(page))")
            }
            let d = try Self.object(await self.service().get(path))
            let key = kind == .history ? "history" : kind == .dictionaries ? "pronunciation_dictionaries" : "voices"
            let id = kind == .history ? "history_item_id" : kind == .dictionaries ? "id" : "voice_id"
            var items = Self.items(d, key, id: id)
            if kind == .history { items = items.map { x in var i = x; i.name += " - " + String(x.string("text").prefix(100)).replacingOccurrences(of: "\n", with: " "); return i } }
            self.catalog = items; self.selectedCatalog = []
            let more = d["has_more"] as? Bool == true
            self.cursor = !more ? "" : kind == .history ? items.last?.id ?? "" : kind == .dictionaries ? d["next_cursor"] as? String ?? "" : shared ? String((Int(page) ?? 0) + 1) : d["next_page_token"] as? String ?? ""
        }
    }
    var catalogSelection: CatalogItem? { catalog.first { selectedCatalog.contains($0.id) } }
    func useVoice() {
        guard let item = catalogSelection else { return }
        if !sharedVoices { project.voiceId = item.id; if !voices.contains(where: { $0.id == item.id }) { voices.append(item) }; tool = nil; return }
        guard confirm("Add voice", "Add this shared voice to your ElevenLabs account?") else { return }
        run("Adding shared voice") {
            let d = try Self.object(await self.service().post("/v1/voices/add/\(Self.escape(item.string("public_owner_id")))/\(Self.escape(item.id))", body: ["new_name": item.name]))
            guard let id = d["voice_id"] as? String else { throw SpeechError.response("The service returned no voice ID.") }
            self.voices.append(CatalogItem(id: id, name: item.name, data: item.data)); self.project.voiceId = id; self.tool = nil
        }
    }
    func previewVoice() {
        playVoicePreview(catalogSelection)
    }
    var canPreviewSelectedVoice: Bool { !busy && project.mode.needsVoice && VoicePreview.url(voices.first { $0.id == project.voiceId }) != nil }
    func previewSelectedVoice() { if canPreviewSelectedVoice { playVoicePreview(voices.first { $0.id == project.voiceId }) } }
    private func playVoicePreview(_ voice: CatalogItem?) {
        guard let url = VoicePreview.url(voice) else { showResult("Voice preview", "No preview is available."); return }
        guard !busy else { return }
        stop(); for file in previewFiles { try? FileManager.default.removeItem(at: file) }; previewFiles = []
        run("Downloading preview") {
            let (file, response) = try await URLSession.shared.download(for: URLRequest(url: url, timeoutInterval: 30))
            defer { try? FileManager.default.removeItem(at: file) }
            let size = try file.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0
            guard (response as? HTTPURLResponse)?.statusCode == 200, response.url?.scheme == "https", size > 0, size <= 20 * 1024 * 1024 else { throw SpeechError.response("The preview could not be downloaded.") }
            try Task.checkCancellation()
            let target = FileManager.default.temporaryDirectory.appendingPathComponent("SpeechPreview-\(UUID().uuidString).mp3")
            try FileManager.default.moveItem(at: file, to: target); self.previewFiles.append(target); self.play(target)
        }
    }
    func deleteVoice() {
        guard let x = catalogSelection, !sharedVoices, confirm("Delete voice", "Permanently delete '\(x.name)' from your ElevenLabs account?") else { return }
        run("Deleting voice") { try await self.service().delete("/v1/voices/\(Self.escape(x.id))"); self.catalog.removeAll { $0.id == x.id }; self.voices.removeAll { $0.id == x.id } }
    }
    func historyDownload(play: Bool = false) {
        guard let x = catalogSelection else { return }; let folder = URL(fileURLWithPath: preferences.outputFolder)
        var target = SpeechFiles.next(folder: folder, stem: x.name, ext: ".mp3")
        if !play { let panel = NSSavePanel(); panel.nameFieldStringValue = target.lastPathComponent; guard panel.runModal() == .OK, let url = panel.url else { return }; target = url }
        run("Downloading history audio") { let data = try await self.service().get("/v1/history/\(Self.escape(x.id))/audio"); try FileManager.default.createDirectory(at: target.deletingLastPathComponent(), withIntermediateDirectories: true); try data.write(to: target, options: .atomic); if play { self.play(target) } }
    }
    func applyDictionaries() {
        guard selectedCatalog.count <= 3 else { showResult("Pronunciation dictionaries", "Select up to three dictionaries."); return }
        project.dictionaries = catalog.filter { selectedCatalog.contains($0.id) }.map { DictionaryLocator(pronunciation_dictionary_id: $0.id, version_id: $0.string("latest_version_id")) }; tool = nil
    }
    func importDictionary() {
        let panel = NSOpenPanel(); guard panel.runModal() == .OK, let url = panel.url, let name = ask("Import pronunciation dictionary", message: "Dictionary name", initial: url.deletingPathExtension().lastPathComponent), !name.isEmpty else { return }
        run("Importing dictionary") { _ = try await self.service().upload("/v1/pronunciation-dictionaries/add-from-file", fields: ["name": name], files: [("file", url)]); self.log("Dictionary imported. Refresh the list to use it.") }
    }
    func exportDictionary() {
        guard let x = catalogSelection else { return }; let panel = NSSavePanel(); panel.nameFieldStringValue = SpeechFiles.stem(x.name) + ".pls"; guard panel.runModal() == .OK, let url = panel.url else { return }
        run("Exporting dictionary") { let data = try await self.service().get("/v1/pronunciation-dictionaries/\(Self.escape(x.id))/\(Self.escape(x.string("latest_version_id")))/download"); try data.write(to: url, options: .atomic) }
    }
    func dictionaryRule(create: Bool, word: String, pronunciation: String, type: String) {
        guard !word.isEmpty, !pronunciation.isEmpty else { showResult("Pronunciation rule", "Enter the original text and its pronunciation."); return }
        var rule: [String: Any] = ["string_to_replace": word, "type": type == "Alias" ? "alias" : "phoneme"]
        if type == "Alias" { rule["alias"] = pronunciation } else { rule["phoneme"] = pronunciation; rule["alphabet"] = type == "IPA phoneme" ? "ipa" : "cmu" }
        if create {
            guard let name = ask("Create dictionary", message: "Dictionary name"), !name.isEmpty else { return }
            run("Creating dictionary") { _ = try await self.service().post("/v1/pronunciation-dictionaries/add-from-rules", body: ["name": name, "rules": [rule]]); self.log("Dictionary created. Refresh the list to use it.") }
        } else if let x = catalogSelection {
            run("Adding pronunciation rule") { _ = try await self.service().post("/v1/pronunciation-dictionaries/\(Self.escape(x.id))/add-rules", body: ["rules": [rule]]); self.log("Rule added. Refresh the dictionary version before applying it.") }
        }
    }
    func clone(name: String, description: String, samples: [URL], consent: Bool) {
        guard consent, !name.isEmpty, !samples.isEmpty else { showResult("Cannot clone", "Enter a name, add samples, and confirm that you have the rights and consent."); return }
        guard confirm("Create voice", "Upload these samples and create a voice in your account?") else { return }
        run("Cloning voice") {
            self.cloneUploading = true; defer { self.cloneUploading = false }
            do {
                _ = try await self.service().upload("/v1/voices/add", fields: ["name": name, "description": description], files: samples.map { ("files", $0) }, progress: { message in
                    Task { @MainActor in if self.cloneUploading { self.cloneStatus = message } }
                })
                self.cloneCreated = true; self.cloneStatus = "Voice created successfully. It has been added to your account."; self.log(self.cloneStatus)
            } catch {
                self.cloneStatus = "The request did not complete locally. If the upload reached ElevenLabs, the voice may still have been created. Check your voice library before trying again.\n" + error.localizedDescription
                throw error
            }
        }
    }
}

enum AudioDevices {
    static func list() -> [CatalogItem] {
        var address = AudioObjectPropertyAddress(mSelector: kAudioHardwarePropertyDevices, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain), size: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size) == noErr else { return [] }
        var ids = [AudioObjectID](repeating: 0, count: Int(size) / MemoryLayout<AudioObjectID>.size)
        guard AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &size, &ids) == noErr else { return [] }
        return ids.compactMap { id in
            var stream = AudioObjectPropertyAddress(mSelector: kAudioDevicePropertyStreams, mScope: kAudioDevicePropertyScopeOutput, mElement: kAudioObjectPropertyElementMain), bytes: UInt32 = 0
            guard AudioObjectGetPropertyDataSize(id, &stream, 0, nil, &bytes) == noErr, bytes > 0 else { return nil }
            func string(_ selector: AudioObjectPropertySelector) -> String? {
                var a = AudioObjectPropertyAddress(mSelector: selector, mScope: kAudioObjectPropertyScopeGlobal, mElement: kAudioObjectPropertyElementMain), s = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
                let value = UnsafeMutablePointer<Unmanaged<CFString>?>.allocate(capacity: 1); value.initialize(to: nil)
                defer { value.deinitialize(count: 1); value.deallocate() }
                guard AudioObjectGetPropertyData(id, &a, 0, nil, &s, value) == noErr else { return nil }; return value.pointee?.takeRetainedValue() as String?
            }
            guard let uid = string(kAudioDevicePropertyDeviceUID), let name = string(kAudioObjectPropertyName) else { return nil }; return CatalogItem(id: uid, name: name)
        }
    }
}
