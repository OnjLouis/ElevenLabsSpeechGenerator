import AppKit
import XCTest
@testable import ElevenLabsSpeechGeneratorMac

final class VersionHistoryTests: XCTestCase {
    @MainActor
    func testCurrentAndPreviousReleaseAreReadableOffline() {
        XCTAssertTrue(VersionHistory.text.hasPrefix("1.2.0\n\n"))
        XCTAssertTrue(VersionHistory.text.contains("\n\n1.1.1\n\n"))
        XCTAssertFalse(VersionHistory.text.contains("\\n"))
        let (alert, editor) = VersionHistory.make()
        XCTAssertEqual(alert.messageText, "Version history")
        XCTAssertEqual(editor.string, VersionHistory.text)
        XCTAssertEqual(editor.accessibilityLabel(), "Version history")
        XCTAssertTrue((editor.accessibilityHelp() ?? "").isEmpty)
        XCTAssertFalse(editor.isEditable)
        XCTAssertTrue(editor.isSelectable)
        XCTAssertTrue(alert.window.initialFirstResponder === editor)
        XCTAssertEqual(alert.buttons.first?.title, "Close")
        XCTAssertEqual(alert.buttons.first?.keyEquivalent, "\u{1b}")
        XCTAssertTrue(alert.accessoryView is NSScrollView)
    }
}
