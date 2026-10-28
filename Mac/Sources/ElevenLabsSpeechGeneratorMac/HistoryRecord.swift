import Foundation

enum HistoryRecord {
    private static func string(_ data: [String: Any], _ key: String) -> String { data[key] as? String ?? "" }
    private static func dialogue(_ data: [String: Any]) -> [[String: Any]] {
        (data["dialogue"] as? [Any] ?? []).compactMap { $0 as? [String: Any] }
            .filter { !string($0, "text").trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
    }
    private static func text(_ data: [String: Any]) -> String {
        let value = string(data, "text")
        return value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? dialogue(data).map { string($0, "text") }.joined(separator: "\n") : value
    }
    private static func singleLine(_ value: String) -> String { value.split(whereSeparator: { $0.isWhitespace }).joined(separator: " ") }
    private static func kind(_ data: [String: Any]) -> String {
        switch string(data, "source") {
        case "TTS": return "Speech"
        case "STS": return "Voice conversion"
        case "Dubbing": return "Dubbing"
        case "VoicePreview": return "Voice preview"
        case "ConvAI": return "Conversation"
        default: return "Recording"
        }
    }
    private static func date(_ data: [String: Any]) -> String {
        guard let seconds = (data["date_unix"] as? NSNumber)?.doubleValue, seconds.isFinite,
              seconds >= -62135596800, seconds < 253402300800 else { return "" }
        let formatter = DateFormatter()
        formatter.locale = .current; formatter.dateFormat = "d MMM yyyy, HH:mm:ss"
        return formatter.string(from: Date(timeIntervalSince1970: seconds))
    }
    static func label(_ data: [String: Any]) -> String {
        var voice = singleLine(string(data, "voice_name"))
        if voice.isEmpty {
            var voices: [String] = []
            for line in dialogue(data) {
                let name = singleLine(string(line, "voice_name"))
                if !name.isEmpty && !voices.contains(name) { voices.append(name) }
            }
            voice = voices.joined(separator: ", ")
        }
        let fullText = singleLine(text(data))
        let preview = String(fullText.prefix(160)) + (fullText.count > 160 ? "..." : "")
        if !preview.isEmpty { return voice.isEmpty ? preview : voice + " - " + preview }
        let created = date(data), id = string(data, "history_item_id")
        let suffix = created.isEmpty ? id : created
        return (voice.isEmpty ? kind(data) : voice) + (suffix.isEmpty ? "" : " - " + suffix)
    }
    static func details(_ data: [String: Any]) -> String {
        // Single-speaker speech can also use the dialogue response structure.
        var value = string(data, "text")
        if value.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            value = dialogue(data).map { line in
                let voice = string(line, "voice_name").trimmingCharacters(in: .whitespacesAndNewlines)
                return (voice.isEmpty ? "" : voice + ": ") + string(line, "text")
            }.joined(separator: "\n\n")
        }
        var lines = [value.isEmpty ? "No transcript is available for this recording." : value]
        let created = date(data), model = string(data, "model_id")
        if !created.isEmpty { lines.append("Created: " + created) }
        if !model.isEmpty { lines.append("Model: " + model) }
        return lines.joined(separator: "\n").replacingOccurrences(of: "\r\n", with: "\n").replacingOccurrences(of: "\r", with: "\n")
    }
    static func matches(_ data: [String: Any], query: String) -> Bool {
        query.isEmpty || (label(data) + "\n" + details(data)).localizedCaseInsensitiveContains(query)
    }
}
