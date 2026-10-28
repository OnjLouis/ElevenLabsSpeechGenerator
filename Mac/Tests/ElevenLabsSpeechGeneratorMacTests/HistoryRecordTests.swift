import Foundation
import XCTest
@testable import ElevenLabsSpeechGeneratorMac

final class HistoryRecordTests: XCTestCase {
    func testNestedSpeechAndAllDialogueTurns() throws {
        let data: [String: Any] = ["history_item_id": "nested", "voice_name": NSNull(), "text": NSNull(), "source": "TTS", "model_id": "eleven_v4", "dialogue": [
            ["voice_name": "Alice", "text": "First line.\nSecond line." + String(repeating: "x", count: 200)],
            ["voice_name": "Bob", "text": "Later searchable words."]]]
        let label = HistoryRecord.label(data), details = HistoryRecord.details(data)
        XCTAssertTrue(label.contains("Alice") && label.contains("Bob") && label.contains("First line."))
        XCTAssertFalse(label.contains("\n")); XCTAssertFalse(label.contains("Later searchable"))
        XCTAssertTrue(details.contains("Alice: First line.\nSecond line.") && details.contains("Bob: Later searchable words.") && details.contains("eleven_v4"))
        XCTAssertTrue(HistoryRecord.matches(data, query: "LATER SEARCHABLE"))
        XCTAssertFalse(HistoryRecord.matches(data, query: "not in this recording"))
    }
    func testLegacyAndMissingOptionalMetadata() {
        XCTAssertEqual(HistoryRecord.label(["voice_name": "Rachel", "text": "Older speech."]), "Rachel - Older speech.")
        XCTAssertEqual(HistoryRecord.label(["dialogue": [["text": "Words without a voice name."]]]), "Words without a voice name.")
        let data: [String: Any] = ["history_item_id": "empty", "voice_name": NSNull(), "text": NSNull(), "dialogue": NSNull(), "source": "STS", "date_unix": 1793142000]
        XCTAssertTrue(HistoryRecord.label(data).contains("Voice conversion") && HistoryRecord.label(data).contains("2026"))
        XCTAssertTrue(HistoryRecord.details(data).contains("No transcript is available"))
        XCTAssertEqual(HistoryRecord.label([:]), "Recording")
    }
    func testMalformedDialogueAndDuplicateVoices() {
        let data: [String: Any] = ["dialogue": [NSNull(), "invalid", ["text": "\t"], ["voice_name": "Alice", "text": "One."], ["voice_name": "Alice", "text": "Two."]]]
        XCTAssertEqual(HistoryRecord.label(data), "Alice - One. Two.")
        XCTAssertEqual(HistoryRecord.details(data), "Alice: One.\n\nAlice: Two.")
        XCTAssertEqual(HistoryRecord.label(["history_item_id": "unknown", "date_unix": Double.infinity]), "Recording - unknown")
    }
}
