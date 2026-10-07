import Foundation
import Security

struct AppPreferences: Codable, Equatable {
    var outputFolder = FileManager.default.homeDirectoryForCurrentUser.appendingPathComponent("Music/ElevenLabs Speech Generator").path
    var includeDetails = true
    var completionSound = true
    var autoUpdateOnLaunch = true
    var installUpdatesSilently = false
    var playbackDevice = ""
    var autoPlayGenerations: Bool? = false
    var voiceGroup: String? = "All voices"
    var defaultOutputFormat: String? = "mp3_44100_128"
    var resolvedOutputFormat: String { SpeechProject.formats.contains(defaultOutputFormat ?? "") ? defaultOutputFormat! : "mp3_44100_128" }
    static func load() -> AppPreferences { guard let data = UserDefaults.standard.data(forKey: "preferences"), let value = try? JSONDecoder().decode(Self.self, from: data) else { return Self() }; return value }
    func save() { if let data = try? SpeechFiles.json(self) { UserDefaults.standard.set(data, forKey: "preferences") } }
}

enum KeychainStore {
    private static let service = "me.onj.ElevenLabsSpeechGeneratorMac"
    private static let account = "ElevenLabsAPIKey"
    private static var query: [String: Any] { [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account] }
    static func read() throws -> String? {
        var q = query; q[kSecReturnData as String] = true; q[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?; let status = SecItemCopyMatching(q as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess, let data = result as? Data else { throw SpeechError.response("Could not read the API key from Keychain (\(status)).") }
        return String(data: data, encoding: .utf8)
    }
    static func write(_ key: String) throws {
        let value = key.trimmingCharacters(in: .whitespacesAndNewlines)
        if value.isEmpty { let status = SecItemDelete(query as CFDictionary); guard status == errSecSuccess || status == errSecItemNotFound else { throw SpeechError.response("Could not remove the API key (\(status)).") }; return }
        let update = [kSecValueData as String: Data(value.utf8)]; var status = SecItemUpdate(query as CFDictionary, update as CFDictionary)
        if status == errSecItemNotFound { var q = query; q[kSecValueData as String] = Data(value.utf8); status = SecItemAdd(q as CFDictionary, nil) }
        guard status == errSecSuccess else { throw SpeechError.response("Could not save the API key to Keychain (\(status)).") }
    }
}

enum DraftStore {
    static var folder: URL { FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("ElevenLabs Speech Generator") }
    static func load() throws -> [String: SpeechProject] {
        let path = folder.appendingPathComponent("Drafts.json"); if !FileManager.default.fileExists(atPath: path.path) { return [:] }
        guard (try path.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0) <= 8 * 1024 * 1024 else { throw SpeechError.validation("The drafts file is too large.") }
        let drafts = try JSONDecoder().decode([String: SpeechProject].self, from: Data(contentsOf: path)); for p in drafts.values { try p.validateStructure() }; return drafts
    }
    static func save(_ drafts: [String: SpeechProject]) throws { try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true); try SpeechFiles.json(drafts).write(to: folder.appendingPathComponent("Drafts.json"), options: .atomic) }
}
