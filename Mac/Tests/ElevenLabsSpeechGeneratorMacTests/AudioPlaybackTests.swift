import AppKit
import XCTest
@testable import ElevenLabsSpeechGeneratorMac
private final class TestSound: PlaybackSound {
    var delegate: NSSoundDelegate?
    var playbackDeviceIdentifier: String?
    var plays = 0, stops = 0
    var succeeds = true
    func play() -> Bool { plays += 1; return succeeds }
    func stop() -> Bool { stops += 1; return true }
}
final class AudioPlaybackTests: XCTestCase {
    @MainActor func testNativeSoundCompletion() async throws {
        _ = NSApplication.shared
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        var wave = Data()
        func text(_ value: String) { wave.append(contentsOf: value.utf8) }
        func integer<T: FixedWidthInteger>(_ value: T) { var little = value.littleEndian; withUnsafeBytes(of: &little) { wave.append(contentsOf: $0) } }
        text("RIFF"); integer(UInt32(8856)); text("WAVEfmt "); integer(UInt32(16)); integer(UInt16(1)); integer(UInt16(1))
        integer(UInt32(44100)); integer(UInt32(88200)); integer(UInt16(2)); integer(UInt16(16)); text("data"); integer(UInt32(8820)); wave.append(Data(repeating: 0, count: 8820))
        let paths = ["one", "two", "three"].map { folder.appendingPathComponent($0 + ".wav") }
        for url in paths { try wave.write(to: url) }
        var starts: [URL] = [], errors: [String] = []
        let playback = AudioPlayback { url in let sound = NSSound(contentsOf: url, byReference: true); sound?.volume = 0; starts.append(url); return sound }
        playback.onError = { errors.append($0) }
        playback.playSequence(paths, device: "")
        let deadline = Date().addingTimeInterval(6)
        while playback.isPlaying && Date() < deadline { try await Task.sleep(for: .milliseconds(20)) }
        XCTAssertTrue(errors.isEmpty, errors.joined(separator: "; "))
        XCTAssertFalse(playback.isPlaying)
        XCTAssertEqual(starts, paths)
        playback.stop()
    }
    @MainActor func testSequenceStopAndStaleCallbacks() {
        var created: [TestSound] = []
        let playback = AudioPlayback { _ in let sound = TestSound(); created.append(sound); return sound }
        let urls = ["first", "second", "third"].map { URL(fileURLWithPath: "/test/\($0).wav") }
        playback.playSequence(urls, device: "test-output")
        XCTAssertEqual(created.count, 1)
        XCTAssertEqual(created[0].playbackDeviceIdentifier, "test-output")
        playback.finished(created[0], success: true)
        XCTAssertEqual(created.count, 2)
        playback.finished(created[0], success: true)
        XCTAssertEqual(created.count, 2)
        playback.stop()
        playback.finished(created[1], success: true)
        XCTAssertEqual(created.count, 2)
        playback.play(urls[0], device: "")
        playback.finished(created[1], success: true)
        XCTAssertEqual(created.count, 3)
        playback.finished(created[2], success: true)
        XCTAssertFalse(playback.isPlaying)
    }
    @MainActor func testFailureEndsQueue() {
        let sound = TestSound(); sound.succeeds = false
        var errors = 0
        let playback = AudioPlayback { _ in sound }
        playback.onError = { _ in errors += 1 }
        playback.playSequence([URL(fileURLWithPath: "/bad.wav"), URL(fileURLWithPath: "/never.wav")], device: "")
        XCTAssertEqual(sound.plays, 1)
        XCTAssertEqual(errors, 1)
        XCTAssertFalse(playback.isPlaying)
    }
    func testLegacyPreferencesKeepSettings() throws {
        var preferences = AppPreferences()
        preferences.outputFolder = "/custom/output"
        let original = try JSONEncoder().encode(preferences)
        var object = try XCTUnwrap(JSONSerialization.jsonObject(with: original) as? [String: Any])
        object.removeValue(forKey: "autoPlayGenerations")
        let restored = try JSONDecoder().decode(AppPreferences.self, from: JSONSerialization.data(withJSONObject: object))
        XCTAssertEqual(restored.outputFolder, "/custom/output")
        XCTAssertNotEqual(restored.autoPlayGenerations, true)
        preferences.autoPlayGenerations = true
        XCTAssertEqual(try JSONDecoder().decode(AppPreferences.self, from: JSONEncoder().encode(preferences)).autoPlayGenerations, true)
    }
}
