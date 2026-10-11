import AppKit
import XCTest
@testable import ElevenLabsSpeechGeneratorMac

final class InitialFocusTests: XCTestCase {
    @MainActor func testPromptIsInitialResponderWithoutStealingLaterFocus() async {
        _ = NSApplication.shared
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 400, height: 240), styleMask: [.titled], backing: .buffered, defer: false)
        defer { window.orderOut(nil) }
        let button = NSButton(title: "Another control", target: nil, action: nil)
        let editor = TabAwareTextView(frame: NSRect(x: 0, y: 50, width: 300, height: 150))
        editor.wantsInitialFocus = true
        window.contentView?.addSubview(button)
        window.contentView?.addSubview(editor)
        XCTAssertTrue(window.initialFirstResponder === editor, "Prompt must be the native window's initial responder.")
        window.makeKeyAndOrderFront(nil)
        await Task.yield()
        XCTAssertTrue(window.firstResponder === editor, "Prompt must receive focus when the window opens.")
        window.makeFirstResponder(button)
        editor.viewDidMoveToWindow()
        window.orderOut(nil)
        window.makeKeyAndOrderFront(nil)
        await Task.yield()
        XCTAssertTrue(window.firstResponder === button, "Reactivation and view updates must preserve the user's focus.")
    }

    @MainActor func testOtherTextFieldsDoNotClaimStartupFocus() {
        _ = NSApplication.shared
        let window = NSWindow(contentRect: .zero, styleMask: [.titled], backing: .buffered, defer: false)
        defer { window.orderOut(nil) }
        let existing = NSTextField()
        window.contentView?.addSubview(existing)
        window.initialFirstResponder = existing
        window.contentView?.addSubview(TabAwareTextView())
        XCTAssertTrue(window.initialFirstResponder === existing)
    }
}
