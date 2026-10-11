import Foundation

@MainActor final class VoiceAudition {
    static let selectionDelayNanoseconds: UInt64 = 300_000_000
    private let folder: URL
    private let download: (URL, URL) async throws -> Void
    private let play: (URL) -> Void
    private let stopPlayback: () -> Void
    private let isPlaying: () -> Bool
    private let failed: (Error) -> Void
    private(set) var lastRequest: Task<Void, Never>?
    private var pending = false
    private var voiceID: String?
    private var currentFile: URL?
    private var generation = 0

    init(folder: URL = FileManager.default.temporaryDirectory,
         download: @escaping (URL, URL) async throws -> Void = VoicePreview.download,
         play: @escaping (URL) -> Void, stop: @escaping () -> Void,
         isPlaying: @escaping () -> Bool, failed: @escaping (Error) -> Void) {
        self.folder = folder; self.download = download; self.play = play
        self.stopPlayback = stop; self.isPlaying = isPlaying; self.failed = failed
    }
    func select(_ voice: CatalogItem?, automatic: Bool) {
        stop()
        if automatic, let voice, VoicePreview.url(voice) != nil { start(voice, automatic: true) }
    }
    func toggle(_ voice: CatalogItem?) {
        if let voice, voice.id == voiceID, pending || isPlaying() { stop(); return }
        stop()
        guard let voice, VoicePreview.url(voice) != nil else { failed(SpeechError.response("No preview is available for this voice.")); return }
        start(voice, automatic: false)
    }
    private func start(_ voice: CatalogItem, automatic: Bool) {
        guard let url = VoicePreview.url(voice) else { return }
        generation += 1; let ticket = generation
        voiceID = voice.id; pending = true
        let path = folder.appendingPathComponent("SpeechPreview-\(UUID().uuidString).mp3")
        lastRequest = Task {
            defer {
                if currentFile != path { try? FileManager.default.removeItem(at: path) }
                if generation == ticket { pending = false }
            }
            do {
                if automatic { try await Task.sleep(nanoseconds: Self.selectionDelayNanoseconds) }
                try Task.checkCancellation()
                try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                try await download(url, path)
                try Task.checkCancellation()
                guard generation == ticket else { return }
                currentFile = path; play(path)
            } catch is CancellationError { }
            catch { if generation == ticket, !Task.isCancelled { failed(error) } }
        }
    }
    func stop() {
        generation += 1; lastRequest?.cancel(); pending = false
        if voiceID != nil { stopPlayback() }
        voiceID = nil
        if let file = currentFile { try? FileManager.default.removeItem(at: file) }; currentFile = nil
    }
}

enum VoiceNames {
    static func canRename(_ voice: CatalogItem?) -> Bool {
        guard let voice else { return false }
        return voice.data["is_owner"] as? Bool == true && voice.string("category").lowercased() != "premade"
    }
    static func validate(_ value: String) throws -> String {
        let name = value.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, !name.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }) else {
            throw SpeechError.validation("Enter a voice name without line breaks.")
        }
        return name
    }
    static func rename(_ voice: CatalogItem, to value: String, service: SpeechService) async throws {
        guard canRename(voice) else { throw SpeechError.validation("Only voices owned by your account can be renamed.") }
        let name = try validate(value)
        let result = try await service.upload("/v1/voices/\(voice.id.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed.subtracting(CharacterSet(charactersIn: "/?#"))) ?? "")/edit", fields: ["name": name], files: [])
        guard (try JSONSerialization.jsonObject(with: result.data) as? [String: Any])?["status"] as? String == "ok" else {
            throw SpeechError.response("ElevenLabs did not confirm the rename. Refresh the voice library before trying again.")
        }
    }
}
