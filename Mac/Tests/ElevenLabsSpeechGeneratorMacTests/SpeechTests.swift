import AppKit
import XCTest
@testable import ElevenLabsSpeechGeneratorMac

final class SpeechTests: XCTestCase {
    func testUnavailableSelectionsArePreserved() {
        let available = [CatalogItem(id: "known", name: "Known")]
        XCTAssertEqual(CatalogItem.preservingSelection(available, id: "custom").map(\.id), ["known", "custom"])
        XCTAssertEqual(CatalogItem.preservingSelection(available, id: "custom").last?.name, "Unavailable: custom")
        XCTAssertEqual(CatalogItem.preservingSelection(available, id: "known").count, 1)
        XCTAssertEqual(CatalogItem.preservingSelection(available, id: "").count, 1)
    }
    func testPreviewFormats() throws {
        XCTAssertEqual(try SpeechFiles.previewExtension("audio/mpeg"), ".mp3")
        XCTAssertEqual(try SpeechFiles.previewExtension("audio/x-wav; charset=binary"), ".wav")
        XCTAssertThrowsError(try SpeechFiles.previewExtension("application/json"))
    }
    func testProjectRoundTrip() throws {
        var p = SpeechProject(); p.text = "one\ntwo"; p.dialogue = [DialogueLine(voiceId: "v", voiceName: "Voice", text: "Hi")]
        let data = try SpeechFiles.json(p); let q = try JSONDecoder().decode(SpeechProject.self, from: data)
        XCTAssertEqual(q.text, p.text); XCTAssertEqual(q.dialogue[0].text, "Hi"); XCTAssertTrue(String(decoding: data, as: UTF8.self).contains("\n  \""))
        XCTAssertTrue(String(decoding: data, as: UTF8.self).contains("\"SchemaVersion\""))
    }
    func testFiniteSettings() { var p = SpeechProject(); p.speed = .nan; XCTAssertThrowsError(try p.validateStructure()) }
    func testV4Settings() { var p = SpeechProject(); p.modelId = "eleven_v4"; let m = CatalogItem(id: p.modelId, name: "v4", data: ["can_use_style": true, "can_use_speaker_boost": true]); XCTAssertEqual(p.voiceSettings(model: m).count, 2) }
    func testOlderVoiceSettings() { var p = SpeechProject(); p.modelId = "eleven_multilingual_v2"; let m = CatalogItem(id: p.modelId, name: "v2", data: ["can_use_style": true, "can_use_speaker_boost": true]); XCTAssertEqual(p.voiceSettings(model: m).count, 5) }
    func testSpeechModesExcludeEffects() { XCTAssertEqual(SpeechMode.allCases.count, 8); XCTAssertNil(SpeechMode(rawValue: 4)); XCTAssertEqual(SpeechMode.voiceDesign.rawValue, 5) }
    func testAlignmentNeedsTranscript() throws {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("Alignment-\(UUID()).wav")
        try Data([0]).write(to: file); defer { try? FileManager.default.removeItem(at: file) }
        var p = SpeechProject(); p.mode = .forcedAlignment; p.modelId = "forced_alignment"; p.inputFile = file.path
        XCTAssertThrowsError(try p.validate(limit: 675000))
        p.text = "Hello world"; XCTAssertNoThrow(try p.validate(limit: 675000))
        XCTAssertTrue(SpeechFiles.subtitles([["start": 0.0, "end": 1.0, "text": "Hello"], ["start": 1.0, "end": 2.0, "text": "world"]], separateWords: true).contains("Hello world"))
    }
    func testRemixRequiresVoiceAndPreservesInstructions() {
        var p = SpeechProject(); p.mode = .voiceRemix; p.modelId = "eleven_ttv_v3"; p.text = "Make the voice warmer."
        XCTAssertThrowsError(try p.validate(limit: 1000)); p.voiceId = "voice-a"; XCTAssertNoThrow(try p.validate(limit: 1000))
        XCTAssertEqual(p.body(model: nil)["voice_description"] as? String, p.text); XCTAssertNil(p.body(model: nil)["model_id"])
    }
    func testOutputFolderValidation() { XCTAssertThrowsError(try SpeechFiles.validateOutputFolder("")); XCTAssertThrowsError(try SpeechFiles.validateOutputFolder("relative")); XCTAssertNoThrow(try SpeechFiles.validateOutputFolder("/Users/example/Music/Speech")) }
    func testDialogueSchema() { var p = SpeechProject(); p.mode = .dialogue; p.dialogue = [DialogueLine(voiceId: "v", voiceName: "Voice", text: "Hi")]; XCTAssertNotNil(p.body(model: nil)["settings"]); XCTAssertNil(p.body(model: nil)["voice_settings"]); XCTAssertNoThrow(try p.validate(limit: 10000)); p.dialogue[0].text = String(repeating: "x", count: 2001); XCTAssertThrowsError(try p.validate(limit: 10000)) }
    func testDialogueVoiceLimit() { var p = SpeechProject(); p.mode = .dialogue; p.dialogue = (0...10).map { DialogueLine(voiceId: "\($0)", voiceName: "Voice", text: "Hi") }; XCTAssertThrowsError(try p.validate(limit: 10000)) }
    func testVoiceDesignMinimum() { var p = SpeechProject(); p.mode = .voiceDesign; p.modelId = "eleven_ttv_v3"; p.text = "Short"; XCTAssertThrowsError(try p.validate(limit: 10000)); p.text = "A warm British adult narrator."; XCTAssertNoThrow(try p.validate(limit: 10000)); XCTAssertEqual(p.body(model: nil)["auto_generate_text"] as? Bool, true) }
    func testPCMMonoHeader() throws { let d = try SpeechService.audio(SpeechResult(data: Data(repeating: 0, count: 32000), requestId: "", contentType: "audio/pcm"), format: "pcm_16000", channels: 1); XCTAssertEqual(d.count, 32044); XCTAssertEqual(d[22], 1); XCTAssertEqual(String(decoding: d.prefix(4), as: UTF8.self), "RIFF") }
    func testPCMStereoHeader() throws { let d = try SpeechService.audio(SpeechResult(data: Data(repeating: 0, count: 44100 * 4), requestId: "", contentType: "audio/pcm"), format: "pcm_44100", channels: 2); XCTAssertEqual(d[22], 2); XCTAssertEqual(d.count, 176444) }
    func testPCMRejectPartialFrame() { XCTAssertThrowsError(try SpeechService.audio(SpeechResult(data: Data([0]), requestId: "", contentType: "audio/pcm"), format: "pcm_16000", channels: 1)) }
    func testRejectJSONAudio() { XCTAssertThrowsError(try SpeechService.audio(SpeechResult(data: Data("{}".utf8), requestId: "", contentType: "application/json"), format: "mp3_44100_128", channels: 1)) }
    func testFilenames() { XCTAssertEqual(SpeechFiles.stem("CON"), "_CON"); XCTAssertEqual(SpeechFiles.stem("My speech"), "My speech"); XCTAssertEqual(SpeechFiles.stem("a/b"), "a_b") }
    func testInputBoundary() { XCTAssertEqual(PromptInputLimiter.replacement("123", range: NSRange(location: 3, length: 0), with: "456", limit: 5), "45"); XCTAssertEqual(PromptInputLimiter.replacement("", range: NSRange(location: 0, length: 0), with: "😀", limit: 1), "") }
    func testSubtitles() { let s = SpeechFiles.subtitles([["start": 0.0, "end": 1.0, "text": "Hello "], ["start": 1.0, "end": 2.0, "text": "world"]]); XCTAssertTrue(s.contains("00:00:00,000 --> 00:00:02,000")); XCTAssertTrue(s.contains("Hello world")) }
    func testMalformedSubtitleTimes() { let s = SpeechFiles.subtitles([["start": Double.nan, "end": Double.infinity, "text": "Invalid"], ["start": 1e30, "end": 1e30, "text": "Overflow"], ["start": 1.0, "end": 2.0, "text": "Valid"]]); XCTAssertEqual(s, "1\n00:00:01,000 --> 00:00:02,000\nValid\n\n") }
    func testArchitectureAssets() { XCTAssertEqual(MacPackageArchitecture.appleSilicon.assetName(version: "1.0.0"), "ElevenLabs-Speech-Generator-Mac-1.0.0.zip"); XCTAssertEqual(MacPackageArchitecture.intel.assetName(version: "1.0.0"), "ElevenLabs-Speech-Generator-Mac-Intel-1.0.0.zip") }
    @MainActor func testTabsLeaveTextWithoutInsertion() {
        let editor = TabAwareTextView(); var forward = false, backward = false; editor.tabAction = { forward = true }; editor.backTabAction = { backward = true }; editor.string = "one\ntwo"
        let event = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: [], timestamp: 0, windowNumber: 0, context: nil, characters: "\t", charactersIgnoringModifiers: "\t", isARepeat: false, keyCode: 48)!
        editor.keyDown(with: event); XCTAssertTrue(forward); XCTAssertEqual(editor.string, "one\ntwo")
        let shift = NSEvent.keyEvent(with: .keyDown, location: .zero, modifierFlags: .shift, timestamp: 0, windowNumber: 0, context: nil, characters: "\t", charactersIgnoringModifiers: "\t", isARepeat: false, keyCode: 48)!
        editor.keyDown(with: shift); XCTAssertTrue(backward); XCTAssertEqual(editor.string, "one\ntwo")
    }
}
