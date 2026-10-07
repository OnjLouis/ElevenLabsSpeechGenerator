import Foundation

enum SpeechError: LocalizedError {
    case validation(String), response(String)
    var errorDescription: String? { switch self { case .validation(let text), .response(let text): return text } }
}

enum SpeechMode: Int, Codable, CaseIterable, Identifiable {
    case textToSpeech = 0, dialogue = 1, transcription = 2, voiceChanger = 3, voiceDesign = 5, voiceIsolation = 6, forcedAlignment = 7, voiceRemix = 8
    var id: Int { rawValue }
    var title: String { switch self { case .textToSpeech: return "Text to speech"; case .dialogue: return "Dialogue"; case .transcription: return "Transcription"; case .voiceChanger: return "Voice changer"; case .voiceDesign: return "Voice design"; case .voiceIsolation: return "Voice isolation"; case .forcedAlignment: return "Transcript alignment"; case .voiceRemix: return "Voice remix" } }
    var needsAudio: Bool { self == .transcription || self == .voiceChanger || self == .voiceIsolation || self == .forcedAlignment }
    var needsVoice: Bool { self == .textToSpeech || self == .voiceChanger || self == .voiceRemix }
    var choosesFormat: Bool { self == .textToSpeech || self == .dialogue || self == .voiceChanger }
    var previewsVoice: Bool { self == .voiceDesign || self == .voiceRemix }
}

struct DialogueLine: Codable, Identifiable, Equatable {
    var id = UUID()
    var voiceId = ""
    var voiceName = ""
    var text = ""
    enum CodingKeys: String, CodingKey { case voiceId = "VoiceId", voiceName = "VoiceName", text = "Text" }
}

struct DictionaryLocator: Codable, Equatable {
    var pronunciation_dictionary_id: String
    var version_id: String
}

struct SpeechProject: Codable, Equatable {
    var schemaVersion = 1
    var mode = SpeechMode.textToSpeech
    var text = "", filename = "", inputFile = "", voiceId = "", language = ""
    var modelId = "eleven_v4"
    var outputFormat = "mp3_44100_128"
    var variations = 1
    var stability = 0.5, similarity = 0.75, style = 0.0, speed = 1.0
    var speakerBoost = true
    var diarize = false, audioEvents = true, removeNoise = false
    var dialogue: [DialogueLine] = []
    var dictionaries: [DictionaryLocator] = []
    enum CodingKeys: String, CodingKey {
        case schemaVersion = "SchemaVersion", mode = "Mode", text = "Text", filename = "Filename", inputFile = "InputFile", voiceId = "VoiceId", language = "Language", modelId = "ModelId", outputFormat = "OutputFormat", variations = "Variations", stability = "Stability", similarity = "Similarity", style = "Style", speed = "Speed", speakerBoost = "SpeakerBoost", diarize = "Diarize", audioEvents = "AudioEvents", removeNoise = "RemoveNoise", dialogue = "Dialogue", dictionaries = "Dictionaries"
    }
    static let formats = ["mp3_44100_128", "mp3_44100_192", "pcm_16000", "pcm_22050", "pcm_24000", "pcm_44100", "pcm_48000"]
    func validateStructure() throws {
        guard schemaVersion == 1, (1...10).contains(variations), Self.formats.contains(outputFormat), [stability, similarity, style, speed].allSatisfy(\.isFinite), (0...1).contains(stability), (0...1).contains(similarity), (0...1).contains(style), (0.25...4).contains(speed), dictionaries.count <= 3 else { throw SpeechError.validation("The project contains unsupported settings.") }
        guard dictionaries.allSatisfy({ !$0.pronunciation_dictionary_id.isEmpty && !$0.version_id.isEmpty }) else { throw SpeechError.validation("A dictionary is missing its ID or version.") }
        if modelId == "eleven_v3" || mode == .dialogue { guard [0, 0.5, 1].contains(stability) else { throw SpeechError.validation("This model needs stability of 0, 50, or 100 percent.") }; }
    }
    func validate(limit: Int) throws {
        try validateStructure()
        guard !modelId.isEmpty else { throw SpeechError.validation("Choose a model.") }
        if mode.needsVoice && voiceId.isEmpty { throw SpeechError.validation("Choose a voice.") }
        if mode.needsAudio {
            let attrs = try FileManager.default.attributesOfItem(atPath: inputFile)
            guard let size = attrs[.size] as? NSNumber, size.int64Value > 0, size.int64Value <= 500 * 1024 * 1024 else { throw SpeechError.validation("Select an audio file smaller than 500 MB.") }
            if mode == .forcedAlignment {
                guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, text.utf16.count <= 675000 else { throw SpeechError.validation("Enter the matching transcript, up to 675,000 characters.") }
            }
        } else if mode == .dialogue {
            guard !dialogue.isEmpty, dialogue.allSatisfy({ !$0.voiceId.isEmpty && !$0.text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }), dialogue.reduce(0, { $0 + $1.text.utf16.count }) <= 2000, Set(dialogue.map(\.voiceId)).count <= 10 else { throw SpeechError.validation("Add dialogue with up to 2,000 characters and 10 voices.") }
        } else {
            let maximum = mode.previewsVoice ? 1000 : limit
            guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, text.utf16.count <= maximum else { throw SpeechError.validation("Enter text of up to \(maximum) characters.") }
            if mode == .voiceDesign && text.utf16.count < 20 { throw SpeechError.validation("Describe the voice in at least 20 characters.") }
            if mode == .voiceRemix && text.utf16.count < 5 { throw SpeechError.validation("Describe the voice changes in at least 5 characters.") }
        }
    }
    func voiceSettings(model: CatalogItem?) -> [String: Any] {
        var settings: [String: Any] = ["stability": stability, "similarity_boost": similarity]
        if !modelId.hasPrefix("eleven_v4") {
            settings["speed"] = speed
            if model?.data["can_use_style"] as? Bool == true { settings["style"] = style }
            if model?.data["can_use_speaker_boost"] as? Bool == true { settings["use_speaker_boost"] = speakerBoost }
        }
        return settings
    }
    func body(model: CatalogItem?) -> [String: Any] {
        if mode == .voiceDesign { return ["voice_description": text, "auto_generate_text": true, "model_id": modelId] }
        if mode == .voiceRemix { return ["voice_description": text, "auto_generate_text": true] }
        var b: [String: Any] = ["model_id": modelId]
        if mode == .dialogue { b["inputs"] = dialogue.map { ["text": $0.text, "voice_id": $0.voiceId] }; b["settings"] = ["stability": stability] }
        else { b["text"] = text; b["voice_settings"] = voiceSettings(model: model) }
        if !language.isEmpty { b["language_code"] = language }
        if !dictionaries.isEmpty { b["pronunciation_dictionary_locators"] = dictionaries.map { ["pronunciation_dictionary_id": $0.pronunciation_dictionary_id, "version_id": $0.version_id] } }
        return b
    }
}

struct CatalogItem: Identifiable {
    var id: String
    var name: String
    var data: [String: Any] = [:]
    func string(_ key: String) -> String { data[key] as? String ?? "" }
    static func preservingSelection(_ items: [CatalogItem], id: String) -> [CatalogItem] {
        guard !id.isEmpty, !items.contains(where: { $0.id == id }) else { return items }
        return items + [CatalogItem(id: id, name: "Unavailable: " + id)]
    }
}

enum SpeechFiles {
    static func generationFolder(root: URL, mode: SpeechMode, voiceName: String) -> URL {
        if mode == .transcription || mode == .forcedAlignment { return root }
        return root.appendingPathComponent(stem(mode == .dialogue ? "Dialogue" : mode == .voiceDesign ? "Voice design" : mode == .voiceRemix ? "Voice remix" : mode == .voiceIsolation ? "Voice isolation" : voiceName), isDirectory: true)
    }
    static func previewExtension(_ type: String) throws -> String {
        switch type.components(separatedBy: ";")[0].trimmingCharacters(in: .whitespacesAndNewlines).lowercased() {
        case "audio/mpeg", "audio/mp3": return ".mp3"
        case "audio/wav", "audio/x-wav", "audio/wave": return ".wav"
        default: throw SpeechError.response("The voice preview uses an unsupported audio format.")
        }
    }
    static func validateOutputFolder(_ path: String) throws {
        guard path.hasPrefix("/"), !path.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, !path.contains("\0") else { throw SpeechError.validation("Choose an absolute default output folder in Settings.") }
        var directory: ObjCBool = false
        if FileManager.default.fileExists(atPath: path, isDirectory: &directory), !directory.boolValue { throw SpeechError.validation("The default output folder is a file. Choose a folder in Settings.") }
    }
    static func stem(_ text: String) -> String {
        var s = String(text.map { "/\\:*?\"<>|".contains($0) || $0.unicodeScalars.contains(where: { $0.value < 32 }) ? "_" : $0 }).trimmingCharacters(in: .whitespacesAndNewlines)
        while s.hasSuffix(".") { s.removeLast() }
        s = String(s.prefix(100)); if s.isEmpty { s = "Speech" }
        let reserved = ["CON", "PRN", "AUX", "NUL"] + (1...9).flatMap { ["COM\($0)", "LPT\($0)"] }
        if reserved.contains(s.components(separatedBy: ".")[0].uppercased()) { s = "_" + s }; return s
    }
    static func next(folder: URL, stem: String, ext: String) -> URL {
        var n = 1
        while true { let path = folder.appendingPathComponent(self.stem(stem) + (n == 1 ? "" : " (\(n))") + ext); if !FileManager.default.fileExists(atPath: path.path) { return path }; n += 1 }
    }
    static func json<T: Encodable>(_ value: T) throws -> Data { let e = JSONEncoder(); e.outputFormatting = [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes]; return try e.encode(value) }
    static func project(_ data: Data) throws -> SpeechProject {
        let value = try JSONSerialization.jsonObject(with: data)
        let nested = (value as? [String: Any])?["project"] ?? value
        let p = try JSONDecoder().decode(SpeechProject.self, from: JSONSerialization.data(withJSONObject: nested)); try p.validateStructure(); return p
    }
    static func subtitles(_ words: [[String: Any]], separateWords: Bool = false) -> String {
        var out = "", text = "", start = 0.0, end = 0.0, index = 0
        func cue() { guard !text.isEmpty else { return }; index += 1; out += "\(index)\n\(time(start)) --> \(time(max(start + 0.1, end)))\n\(text.trimmingCharacters(in: .whitespacesAndNewlines))\n\n"; text = "" }
        for w in words { guard let a = w["start"] as? Double, let b = w["end"] as? Double, a.isFinite, b.isFinite, a >= 0, b >= a, b < Double(Int.max / 1000 - 1) else { continue }; if text.isEmpty { start = a }; if separateWords && !text.isEmpty { text += " " }; text += w["text"] as? String ?? ""; end = b; if text.count >= 70 || end - start >= 5 { cue() } }; cue(); return out
    }
    private static func time(_ s: Double) -> String { let ms = Int((s * 1000).rounded()); return String(format: "%02d:%02d:%02d,%03d", ms / 3600000, ms / 60000 % 60, ms / 1000 % 60, ms % 1000) }
}
