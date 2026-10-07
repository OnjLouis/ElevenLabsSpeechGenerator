import Foundation
import XCTest
@testable import ElevenLabsSpeechGeneratorMac

final class VoicePreviewTests: XCTestCase {
    func testPreviewAvailabilityAndSecurity() {
        XCTAssertNotNil(VoicePreview.url(CatalogItem(id: "voice", name: "Voice", data: ["preview_url": "https://example.invalid/voice.mp3"])))
        for value in ["", "http://example.invalid/voice.mp3", "https://user:password@example.invalid/voice.mp3"] {
            XCTAssertNil(VoicePreview.url(CatalogItem(id: "voice", name: "Voice", data: ["preview_url": value])))
        }
        XCTAssertNil(VoicePreview.url(nil))
    }
    func testOutputGroupingAndDefaultFormat() throws {
        let root = URL(fileURLWithPath: "/disposable/output")
        XCTAssertEqual(SpeechFiles.generationFolder(root: root, mode: .textToSpeech, voiceName: "Adam - Warm").lastPathComponent, "Adam - Warm")
        XCTAssertEqual(SpeechFiles.generationFolder(root: root, mode: .voiceChanger, voiceName: "../../escape").deletingLastPathComponent().path, root.path)
        XCTAssertEqual(SpeechFiles.generationFolder(root: root, mode: .dialogue, voiceName: "Adam").lastPathComponent, "Dialogue")
        XCTAssertEqual(SpeechFiles.generationFolder(root: root, mode: .voiceDesign, voiceName: "").lastPathComponent, "Voice design")
        XCTAssertEqual(SpeechFiles.generationFolder(root: root, mode: .transcription, voiceName: ""), root)
        var preferences = AppPreferences(); preferences.defaultOutputFormat = "pcm_24000"
        XCTAssertEqual(try JSONDecoder().decode(AppPreferences.self, from: JSONEncoder().encode(preferences)).resolvedOutputFormat, "pcm_24000")
        preferences.defaultOutputFormat = nil; XCTAssertEqual(preferences.resolvedOutputFormat, "mp3_44100_128")
        preferences.defaultOutputFormat = "unsupported"; XCTAssertEqual(preferences.resolvedOutputFormat, "mp3_44100_128")
    }
}
