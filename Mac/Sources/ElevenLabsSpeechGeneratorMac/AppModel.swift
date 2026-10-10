import AppKit
import SwiftUI

struct OutputItem: Identifiable { var id = UUID(); var url: URL; var preview: [String: Any]? }
enum ToolKind: String, Identifiable { case voices, clone, history, dictionaries, settings, dialogue, voiceSettings, dubbing; var id: String { rawValue } }

@MainActor final class AppModel: ObservableObject {
    @Published var project = SpeechProject() { didSet { scheduleDrafts() } }
    @Published var preferences = AppPreferences.load()
    @Published var settingsTab = "General"
    @Published var voices: [CatalogItem] = []
    @Published var models: [CatalogItem] = []
    @Published var outputs: [OutputItem] = []
    @Published var selectedOutput: UUID?
    @Published var balance = "Enter an API key in Settings to check your balance."
    @Published var status = "Ready."
    @Published var busy = false
    @Published var tool: ToolKind?
    @Published var catalog: [CatalogItem] = []
    @Published var selectedCatalog: Set<String> = []
    @Published var search = ""
    @Published var sharedVoices = false
    @Published var dubbingJob = DubbingJob()
    @Published var dubbingStatus = "Ready."
    @Published var cloneStatus = "Ready. Add samples and confirm consent before creating a voice."
    @Published var cloneCreated = false
    var cloneUploading = false
    @Published var notice: String?
    private var task: Task<Void, Never>?
    private var balanceTask: Task<Void, Never>?
    private var updateTask: Task<Void, Never>?
    private var draftTask: Task<Void, Never>?
    private var draftsReady = false
    private var drafts: [String: SpeechProject] = [:]
    private var projectURL: URL?
    private let playback = AudioPlayback()
    var cursor = ""
    var previewFiles: [URL] = []
    weak var promptView: TabAwareTextView?
    weak var dialogueTextView: TabAwareTextView?
    var dialogueTagLimit = 2000
    var canInsertSpeechTag: Bool { !busy && (project.modelId.hasPrefix("eleven_v3") || project.modelId.hasPrefix("eleven_v4")) && (project.mode == .textToSpeech || project.mode == .dialogue && tool == .dialogue) }
    func insertSpeechTag() {
        guard canInsertSpeechTag, let editor = project.mode == .dialogue ? dialogueTextView : promptView, editor.isEditable else { return }
        let position = editor.selectedRange().location
        guard let tag = SpeechTagPicker().choose() else { return }
        do {
            _ = try SpeechTags.insertion(tag, into: editor.string, at: position, maximum: project.mode == .dialogue ? dialogueTagLimit : textLimit)
            editor.window?.makeFirstResponder(editor)
            editor.insertText("[\(tag)] ", replacementRange: NSRange(location: position, length: 0))
        } catch { showResult("Could not insert tag", error.localizedDescription) }
    }
    weak var balanceView: TabAwareTextView?
    weak var statusView: TabAwareTextView?
    static let repo = "https://github.com/OnjLouis/ElevenLabsSpeechGenerator"
    init() {
        playback.onError = { [weak self] message in self?.showResult("Could not play", message) }
        project.outputFormat = preferences.resolvedOutputFormat
        do { drafts = try DraftStore.load(); if let last = drafts["Last"] { project = last } } catch { status = "Could not restore drafts: \(error.localizedDescription)" }
        draftsReady = true
    }
    var availableModels: [CatalogItem] { CatalogItem.preservingSelection(supportedModels, id: project.modelId) }
    var availableVoices: [CatalogItem] { VoiceGroups.choices(voices, group: preferences.voiceGroup ?? "All voices", selected: project.voiceId) }
    private var supportedModels: [CatalogItem] {
        switch project.mode {
        case .transcription: return [CatalogItem(id: "scribe_v2", name: "Scribe v2"), CatalogItem(id: "scribe_v1", name: "Scribe v1")]
        case .voiceDesign: return [CatalogItem(id: "eleven_ttv_v3", name: "Voice Design v3"), CatalogItem(id: "eleven_multilingual_ttv_v2", name: "Voice Design v2")]
        case .voiceRemix: return [CatalogItem(id: "eleven_ttv_v3", name: "Voice Remix")]
        case .voiceIsolation: return [CatalogItem(id: "audio_isolation", name: "Voice Isolation")]
        case .forcedAlignment: return [CatalogItem(id: "forced_alignment", name: "Transcript Alignment")]
        default:
            let filtered = models.filter { project.mode == .voiceChanger ? $0.data["can_do_voice_conversion"] as? Bool == true : $0.data["can_do_text_to_speech"] as? Bool == true && (project.mode != .dialogue || $0.id.hasPrefix("eleven_v3") || $0.id.hasPrefix("eleven_v4")) }
            return filtered.isEmpty ? [CatalogItem(id: project.mode == .voiceChanger ? "eleven_multilingual_sts_v2" : "eleven_v4", name: project.mode == .voiceChanger ? "Multilingual Voice Changer v2" : "Eleven v4")] : filtered
        }
    }
    var selectedModel: CatalogItem? { availableModels.first { $0.id == project.modelId } }
    var requestLimit: Int { project.mode.previewsVoice ? 1000 : project.mode == .forcedAlignment ? 675000 : LongSpeech.modelLimit(selectedModel, id: project.modelId) }
    var textLimit: Int { project.mode == .textToSpeech && preferences.allowLongSpeech == true ? LongSpeech.maximumCharacters : requestLimit }
    var windowTitle: String {
        let count = project.mode == .dialogue ? project.dialogue.reduce(0) { $0 + $1.text.utf16.count } : project.text.utf16.count
        return "ElevenLabs Speech Generator - " + (project.mode == .transcription ? "\(count) characters" : "\(count) / \(project.mode == .dialogue ? 2000 : textLimit) characters")
    }
    func switchMode(_ mode: SpeechMode) {
        guard !busy else { return }
        drafts[String(project.mode.rawValue)] = project
        if let saved = drafts[String(mode.rawValue)] { project = saved }
        else { project = SpeechProject(); project.outputFormat = preferences.resolvedOutputFormat; project.mode = mode; project.modelId = supportedModels[0].id }
        flushDrafts()
    }
    func launch() { ContextHelp.shared.install(openManual: manual); refreshBalance(); loadCatalog(); if preferences.autoUpdateOnLaunch { checkUpdates(automatic: true) } }
    func service() throws -> SpeechService { SpeechService(key: try KeychainStore.read() ?? "") }
    func run(_ title: String, _ action: @escaping () async throws -> Void) {
        guard !busy else { return }; busy = true; log(title + ".")
        task = Task {
            defer { busy = false; task = nil }
            do { try await action() }
            catch is CancellationError { log("Cancelled. Completed files were kept.") }
            catch { if (error as NSError).code == NSURLErrorCancelled { log("Cancelled. Completed files were kept.") } else { log(error.localizedDescription); showResult(title + " failed", error.localizedDescription) } }
        }
    }
    func cancel() { task?.cancel() }
    func startDubbing() {
        guard !busy else { return }
        let creating = dubbingJob.projectId.isEmpty
        do { try dubbingJob.validate(creating: creating); try SpeechFiles.validateOutputFolder(preferences.outputFolder) }
        catch { showResult("Cannot start dubbing", error.localizedDescription); return }
        if creating && !confirm("Confirm automatic dubbing", "Send this recording to ElevenLabs and translate it into \(dubbingJob.targetLanguage)? You must have the necessary rights and speaker consent. Creating the project spends credits, even before output is ready. Stopping local monitoring does not cancel that charge.") { return }
        run("Automatic dubbing") {
            defer { self.refreshBalance() }
            let service = DubbingService(api: try self.service())
            do {
                if creating {
                    self.dubbingStatus = "Uploading source and creating dubbing project."
                    self.dubbingJob = try await service.submit(self.dubbingJob)
                    try self.dubbingJob.save()
                }
                let folder = URL(fileURLWithPath: self.preferences.outputFolder).appendingPathComponent("Dubbing").appendingPathComponent(SpeechFiles.stem(self.dubbingJob.targetLanguage))
                self.dubbingJob = try await service.wait(self.dubbingJob, folder: folder, progress: { self.dubbingStatus += "\n" + $0 }, persist: { self.dubbingJob = $0; try $0.save() })
                let item = OutputItem(url: URL(fileURLWithPath: self.dubbingJob.outputFile)); self.addOutput(item.url)
                self.dubbingStatus += "\nDubbing complete. Saved \(item.url.path)."; self.log("Dubbing complete. Saved \(item.url.lastPathComponent).")
                if let app = NSApp { NSAccessibility.post(element: app.keyWindow ?? app, notification: .announcementRequested, userInfo: [.announcement: "Dubbing complete", .priority: NSAccessibilityPriorityLevel.high.rawValue]) }
                if self.preferences.autoPlayGenerations == true { self.playback.playSequence([item.url], device: self.preferences.playbackDevice) }
                else if self.preferences.completionSound { NSSound(named: "Glass")?.play() }
            } catch {
                self.dubbingStatus += "\n" + (self.dubbingJob.projectId.isEmpty ? "The submission may have reached ElevenLabs. Check your dubbing projects before starting again." : "Stopped waiting. Resume this saved job later; it may still be processing on ElevenLabs.")
                throw error
            }
        }
    }
    func openDubbingJob() {
        guard !busy else { return }; let panel = NSOpenPanel(); panel.directoryURL = DubbingJob.folder; panel.allowedContentTypes = [.json]
        if panel.runModal() == .OK, let url = panel.url { do { dubbingJob = try DubbingJob.read(url); dubbingStatus = "Saved project: \(dubbingJob.projectId). Resume checks the existing job without creating another." } catch { showResult("Could not open job", error.localizedDescription) } }
    }
    func loadCatalog() {
        guard (try? KeychainStore.read()) != nil else { return }
        run("Refreshing voices and models") {
            let api = try self.service()
            let m = try JSONSerialization.jsonObject(with: await api.get("/v1/models")) as? [[String: Any]] ?? []
            self.models = m.compactMap { d in guard let id = d["model_id"] as? String, let name = d["name"] as? String else { return nil }; return CatalogItem(id: id, name: name, data: d) }
            var voices: [CatalogItem] = [], cursor = "", seen: Set<String> = []
            repeat {
                let d = try Self.object(await api.get("/v2/voices?page_size=100" + (cursor.isEmpty ? "" : "&next_page_token=\(Self.escape(cursor))")))
                voices += Self.items(d, "voices", id: "voice_id")
                cursor = d["has_more"] as? Bool == true ? d["next_page_token"] as? String ?? "" : ""
                if !cursor.isEmpty && !seen.insert(cursor).inserted { throw SpeechError.response("The service repeated a voice page.") }
            } while !cursor.isEmpty && voices.count < 10000
            self.voices = voices.sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
            if self.project.voiceId.isEmpty { self.project.voiceId = self.voices.first?.id ?? "" }; self.log("Loaded \(voices.count) voices and \(m.count) models.")
        }
    }
    func generate() {
        guard !busy else { return }
        do { try project.validate(limit: textLimit); try SpeechFiles.validateOutputFolder(preferences.outputFolder) } catch { showResult("Cannot generate", error.localizedDescription); return }
        let p = project, model = selectedModel
        let limit = requestLimit, longSpeech = p.mode == .textToSpeech && preferences.allowLongSpeech == true && p.text.utf16.count > requestLimit
        var partCount = 1
        do { if longSpeech { partCount = try LongSpeech.split(p.text, limit: limit).count } } catch { showResult("Cannot generate", error.localizedDescription); return }
        let folder = SpeechFiles.generationFolder(root: URL(fileURLWithPath: preferences.outputFolder), mode: p.mode, voiceName: voices.first { $0.id == p.voiceId }?.name ?? p.voiceId)
        let total = p.mode.choosesFormat ? p.variations : 1
        var confirmation = "Generate \(total * partCount) \(p.mode.title.lowercased()) request(s)? This sends your text or audio to ElevenLabs and may spend credits."
        if longSpeech { confirmation += "\n\nEach variation will use \(partCount) parts in your chosen format and be saved as one WAV at \(LongSpeech.sampleRate(p.outputFormat)) Hz. Verified parts from an identical unfinished job will be reused. An interrupted request already accepted by ElevenLabs may still have incurred a charge; the app does not retry it automatically." }
        guard confirm("Confirm generation", confirmation) else { return }
        stop()
        run("Generating \(p.mode.title.lowercased())") {
            var newAudio: [URL] = []
            defer { self.refreshBalance(); self.flushDrafts() }
            let api = try self.service(), start = Date()
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            if longSpeech {
                let stem = SpeechFiles.stem(p.filename.isEmpty ? p.text.split(whereSeparator: \.isWhitespace).prefix(8).joined(separator: " ") : p.filename)
                newAudio = try await LongSpeech.generate(p, model: model, folder: folder, stem: stem, limit: limit, progress: { self.log($0) }, generate: { part, context in try await api.generate(part, model: model, context: context) })
                for url in newAudio {
                    self.addOutput(url)
                    if self.preferences.includeDetails { try self.saveDetails(folder, stem: url.deletingPathExtension().lastPathComponent, data: ["project": try Self.object(SpeechFiles.json(p)), "assembled_wav": true, "generated_utc": ISO8601DateFormatter().string(from: Date())]) }
                }
            } else {
            for i in 1...total {
                try Task.checkCancellation(); let one = Date()
                let r = try await api.generate(p, model: model)
                var stem = p.filename.isEmpty ? p.text.split(whereSeparator: \.isWhitespace).prefix(8).joined(separator: " ") : p.filename
                if p.mode.needsAudio && p.filename.isEmpty { stem = URL(fileURLWithPath: p.inputFile).deletingPathExtension().lastPathComponent }
                if total > 1 { stem += "_v\(i)" }
                if p.mode == .transcription || p.mode == .forcedAlignment {
                    let d = try Self.object(r.data); self.project.text = p.mode == .forcedAlignment ? p.text : d["text"] as? String ?? ""
                    let txt = SpeechFiles.next(folder: folder, stem: stem, ext: ".txt"); try self.project.text.write(to: txt, atomically: true, encoding: .utf8); self.addOutput(txt)
                    let srt = SpeechFiles.subtitles(d["words"] as? [[String: Any]] ?? [], separateWords: p.mode == .forcedAlignment); if !srt.isEmpty { try srt.write(to: txt.deletingPathExtension().appendingPathExtension("srt"), atomically: true, encoding: .utf8) }
                    if self.preferences.includeDetails { try self.saveDetails(folder, stem: txt.deletingPathExtension().lastPathComponent, data: d) }
                } else if p.mode.previewsVoice {
                    let d = try Self.object(r.data), previews = d["previews"] as? [[String: Any]] ?? []
                    guard !previews.isEmpty else { throw SpeechError.response("No voice previews were returned.") }
                    for (n, value) in previews.enumerated() {
                        guard let encoded = value["audio_base_64"] as? String, let audio = Data(base64Encoded: encoded), !audio.isEmpty else { throw SpeechError.response("Invalid voice preview.") }
                        let ext = try SpeechFiles.previewExtension(value["media_type"] as? String ?? "")
                        let url = SpeechFiles.next(folder: folder, stem: stem + " - Preview \(n + 1)", ext: ext)
                        try audio.write(to: url, options: .atomic); var stored = value; stored["voice_description"] = p.text; self.addOutput(url, preview: stored)
                        newAudio.append(url)
                    }
                } else {
                    let isolation = p.mode == .voiceIsolation
                    let ext = isolation ? try SpeechService.isolationExtension(r.data) : p.outputFormat.hasPrefix("pcm_") ? ".wav" : ".mp3"
                    let url = SpeechFiles.next(folder: folder, stem: stem, ext: ext)
                    let audio = isolation ? try SpeechService.isolatedAudio(r) : try SpeechService.audio(r, format: p.outputFormat, channels: 1)
                    try audio.write(to: url, options: .atomic); self.addOutput(url)
                    newAudio.append(url)
                    if self.preferences.includeDetails { try self.saveDetails(folder, stem: url.deletingPathExtension().lastPathComponent, data: ["project": try Self.object(SpeechFiles.json(p)), "request_id": r.requestId, "generated_utc": ISO8601DateFormatter().string(from: Date())]) }
                }
                self.log("Request \(i) completed in \(String(format: "%.1f", Date().timeIntervalSince(one))) seconds.")
            }
            }
            self.log("Generation complete. Elapsed: \(String(format: "%.1f", Date().timeIntervalSince(start))) seconds.")
            if let app = NSApp { NSAccessibility.post(element: app.mainWindow ?? app, notification: .announcementRequested, userInfo: [.announcement: "Generation complete", .priority: NSAccessibilityPriorityLevel.high.rawValue]) }
            if self.preferences.completionSound && !(self.preferences.autoPlayGenerations == true && !newAudio.isEmpty) { NSSound(named: "Glass")?.play() }
            if self.preferences.autoPlayGenerations == true { self.playback.playSequence(newAudio, device: self.preferences.playbackDevice) }
        }
    }
    private func saveDetails(_ folder: URL, stem: String, data: [String: Any]) throws {
        let details = folder.appendingPathComponent("Details"); try FileManager.default.createDirectory(at: details, withIntermediateDirectories: true)
        try JSONSerialization.data(withJSONObject: data, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]).write(to: details.appendingPathComponent(SpeechFiles.stem(stem) + ".json"), options: .atomic)
    }
    private func addOutput(_ url: URL, preview: [String: Any]? = nil) { let item = OutputItem(url: url, preview: preview); outputs.append(item); selectedOutput = item.id; if outputs.count > 200 { outputs.removeFirst() } }
    func play() {
        guard let item = outputs.first(where: { $0.id == selectedOutput }) else { return }
        if item.url.pathExtension == "txt" { NSWorkspace.shared.open(item.url); return }
        play(item.url)
    }
    func play(_ url: URL) { playback.play(url, device: preferences.playbackDevice) }
    func stop() { playback.stop() }
    func saveSelected() { guard let item = outputs.first(where: { $0.id == selectedOutput }) else { return }; let p = NSSavePanel(); p.nameFieldStringValue = item.url.lastPathComponent; if p.runModal() == .OK, let target = p.url { do { if target != item.url { try Data(contentsOf: item.url).write(to: target, options: .atomic) } } catch { showResult("Could not save", error.localizedDescription) } } }
    func addDesign() {
        guard let item = outputs.first(where: { $0.id == selectedOutput }), let data = item.preview, let id = data["generated_voice_id"] as? String else { showResult("Designed voice", "Select a generated voice preview first."); return }
        guard let name = ask("Save designed voice", message: "Voice name", initial: project.filename), !name.isEmpty, confirm("Save voice", "Save this designed voice to your ElevenLabs account?") else { return }
        run("Saving designed voice") { _ = try await self.service().post("/v1/text-to-voice", body: ["voice_name": name, "voice_description": data["voice_description"] as? String ?? "", "generated_voice_id": id]); self.log("Designed voice saved. Refresh the voice list to use it.") }
    }
    func refreshBalance() {
        guard balanceTask == nil else { return }
        balanceTask = Task { defer { balanceTask = nil }; do { let data = try await service().get("/v1/user/subscription"); balance = try SubscriptionBalance.decode(data).display() } catch { balance = "Could not check the credit balance: \(error.localizedDescription)" } }
    }
    func testKey(_ key: String) {
        run("Testing API key") { let api = SpeechService(key: key); var lines: [String] = []
            for (name, path) in [("Models", "/v1/models"), ("Voices", "/v2/voices?page_size=1"), ("Credit balance", "/v1/user/subscription")] { do { _ = try await api.get(path); lines.append("\(name): available.") } catch { lines.append("\(name): \(error.localizedDescription)") } }
            lines.append("No audio was generated. Grant the appropriate generation permissions for the modes you use."); self.showResult("API key test result", lines.joined(separator: "\n"))
        }
    }
    func focus(_ which: String) { let view = which == "balance" ? balanceView : which == "status" ? statusView : promptView; if let view { view.window?.makeFirstResponder(view) } }
    func log(_ text: String) { status += "\n" + text; if status.utf16.count > 50000 { status = String(status.suffix(35000)) } }
    func showResult(_ title: String, _ text: String) {
        let alert = NSAlert(); alert.messageText = title; let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 600, height: 180)); scroll.hasVerticalScroller = true
        let editor = NSTextView(frame: scroll.bounds); editor.isEditable = false; editor.isSelectable = true; editor.isRichText = false; editor.string = text; editor.font = .systemFont(ofSize: 13); editor.setAccessibilityLabel(title); scroll.documentView = editor; alert.accessoryView = scroll; alert.addButton(withTitle: "Close"); alert.window.initialFirstResponder = editor; alert.runModal()
    }
    func confirm(_ title: String, _ message: String) -> Bool { let a = NSAlert(); a.messageText = title; a.informativeText = message; a.addButton(withTitle: "Cancel"); a.addButton(withTitle: "Continue"); return a.runModal() == .alertSecondButtonReturn }
    func ask(_ title: String, message: String, initial: String = "") -> String? { let a = NSAlert(); a.messageText = title; a.informativeText = message; let edit = NSTextField(string: initial); edit.frame = NSRect(x: 0, y: 0, width: 500, height: 25); edit.setAccessibilityLabel(message); a.accessoryView = edit; a.addButton(withTitle: "OK"); a.addButton(withTitle: "Cancel"); return a.runModal() == .alertFirstButtonReturn ? edit.stringValue.trimmingCharacters(in: .whitespacesAndNewlines) : nil }
    func open(_ link: String) { if let url = URL(string: link) { NSWorkspace.shared.open(url) } }
    func openFolder() { do { try SpeechFiles.validateOutputFolder(preferences.outputFolder); let url = URL(fileURLWithPath: preferences.outputFolder); try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true); NSWorkspace.shared.open(url) } catch { showResult("Could not open output folder", error.localizedDescription) } }
    func manual() { if let url = Bundle.main.url(forResource: "Manual", withExtension: "html") { NSWorkspace.shared.open(url) } }
    func newProject() { guard !busy else { return }; let mode = project.mode; project = SpeechProject(); project.outputFormat = preferences.resolvedOutputFormat; project.mode = mode; project.modelId = supportedModels[0].id; projectURL = nil }
    func openProject() {
        guard !busy else { return }; let panel = NSOpenPanel(); panel.allowsMultipleSelection = false
        if panel.runModal() == .OK, let url = panel.url { do { let size = try url.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0; guard size <= 8 * 1024 * 1024 else { throw SpeechError.validation("The project is too large.") }; let data = try Data(contentsOf: url); if url.pathExtension == "txt" { project.text = String(decoding: data, as: UTF8.self); projectURL = nil } else { let p = try SpeechFiles.project(data); project = p; projectURL = url } } catch { showResult("Could not open", error.localizedDescription) } }
    }
    func saveProject(as saveAs: Bool = false) {
        if projectURL == nil || saveAs { let p = NSSavePanel(); p.nameFieldStringValue = SpeechFiles.stem(project.filename) + ".speech.json"; if p.runModal() != .OK { return }; projectURL = p.url }
        do { if let url = projectURL { try SpeechFiles.json(project).write(to: url, options: .atomic); log("Project saved.") } } catch { showResult("Could not save", error.localizedDescription) }
    }
    func chooseAudio() { let p = NSOpenPanel(); if p.runModal() == .OK, let url = p.url { project.inputFile = url.path } }
    func flushDrafts() { drafts[String(project.mode.rawValue)] = project; drafts["Last"] = project; do { try DraftStore.save(drafts); preferences.save() } catch { log("Could not save drafts: \(error.localizedDescription)") } }
    private func scheduleDrafts() { guard draftsReady else { return }; draftTask?.cancel(); draftTask = Task { do { try await Task.sleep(for: .milliseconds(1500)); self.flushDrafts(); self.draftTask = nil } catch { } } }
    func shutdown() { ContextHelp.shared.stop(); draftsReady = false; draftTask?.cancel(); task?.cancel(); balanceTask?.cancel(); updateTask?.cancel(); stop(); flushDrafts(); for url in previewFiles { try? FileManager.default.removeItem(at: url) } }
    func checkUpdates(automatic: Bool = false) {
        guard updateTask == nil else { return }
        updateTask = Task { defer { updateTask = nil }; do {
            let (data, response) = try await URLSession.shared.data(from: URL(string: "https://api.github.com/repos/OnjLouis/ElevenLabsSpeechGenerator/releases?per_page=100")!)
            if (response as? HTTPURLResponse)?.statusCode == 404 { if !automatic { showResult("Check for updates", "No update is currently available.") }; return }
            guard (response as? HTTPURLResponse)?.statusCode == 200 else { throw SpeechError.response("The update service is unavailable. Please try again later.") }
            let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "1.1.0"
            guard let r = try ReleaseCatalog.newerMacRelease(in: data, currentVersion: version) else { if !automatic { showResult("Check for updates", "The app is up to date.") }; return }
            guard !busy && tool != .dubbing else { if !automatic { showResult("Update postponed", "Finish the active request and close Automatic Dubbing before installing an update.") }; return }
            if automatic && preferences.installUpdatesSilently || confirm("Update available", "Version \(r.version) is available. Download, verify and install it, then reopen the app?") { busy = true; defer { busy = false }; flushDrafts(); try await MacUpdateInstaller.start(r) }
        } catch { if !automatic { showResult("Could not check for updates", error.localizedDescription) } } }
    }
    static func object(_ data: Data) throws -> [String: Any] { guard let d = try JSONSerialization.jsonObject(with: data) as? [String: Any] else { throw SpeechError.response("The service returned an unexpected response.") }; return d }
    static func items(_ d: [String: Any], _ key: String, id: String) -> [CatalogItem] { (d[key] as? [[String: Any]] ?? []).compactMap { value in guard let identifier = value[id] as? String else { return nil }; return CatalogItem(id: identifier, name: value["name"] as? String ?? value["voice_name"] as? String ?? identifier, data: value) } }
    static func escape(_ s: String) -> String { s.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? "" }
}
