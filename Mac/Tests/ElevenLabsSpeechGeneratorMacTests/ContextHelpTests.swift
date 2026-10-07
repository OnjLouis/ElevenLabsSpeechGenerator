import AppKit
import XCTest
@testable import ElevenLabsSpeechGeneratorMac

final class ContextHelpTests: XCTestCase {
    @MainActor func testApplicationAccessibilityFocusGetter() {
        let app = NSApplication.shared
        XCTAssertTrue(app.responds(to: NSSelectorFromString("accessibilityFocusedUIElement")))
        _ = FocusedHelpContent.accessibilityFocus()
    }
    @MainActor func testFocusedFieldNeverExposesValue() {
        _ = NSApplication.shared
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 400, height: 240), styleMask: [.titled], backing: .buffered, defer: false)
        let field = NSSecureTextField(string: "private-value-not-help")
        field.setAccessibilityLabel("API key")
        field.setAccessibilityHelp("Enter your own ElevenLabs API key.")
        window.contentView?.addSubview(field)
        window.makeFirstResponder(field)
        defer { window.orderOut(nil) }
        let content = FocusedHelpContent.capture(in: window)
        XCTAssertEqual(content.title, "API key")
        XCTAssertTrue(content.instructions.contains("private credential"))
        XCTAssertFalse(content.instructions.contains(field.stringValue))
    }
    @MainActor func testReadableHelpKeyboardAndClose() {
        _ = NSApplication.shared
        let panel = ContextHelp.makePanel(FocusedHelpContent(title: "Model", instructions: "Choose a model.", restoreView: nil))
        defer { panel.orderOut(nil) }
        let editor = panel.initialFirstResponder as! TabAwareTextView
        XCTAssertFalse(editor.isEditable)
        XCTAssertTrue(editor.isSelectable)
        XCTAssertEqual(editor.accessibilityLabel(), "Model help")
        XCTAssertTrue((editor.accessibilityHelp() ?? "").isEmpty)
        XCTAssertEqual(editor.string, "Choose a model.")
        XCTAssertEqual((editor.nextKeyView as? NSButton)?.title, "Open Manual")
        XCTAssertEqual((editor.nextKeyView?.nextKeyView as? NSButton)?.title, "Close")
        XCTAssertTrue(editor.nextKeyView?.nextKeyView?.nextKeyView === editor)
        var closed = 0
        panel.finish = { closed += 1 }
        panel.cancelOperation(nil); panel.performClose(nil)
        XCTAssertEqual(closed, 2)
    }
}
