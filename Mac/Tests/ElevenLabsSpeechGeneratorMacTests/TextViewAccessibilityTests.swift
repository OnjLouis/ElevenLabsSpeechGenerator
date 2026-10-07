import AppKit
import SwiftUI
import XCTest
@testable import ElevenLabsSpeechGeneratorMac

@MainActor private final class LabelState: ObservableObject {
    @Published var label = "Speech text"
    @Published var text = "Example text"
    var ready: (TabAwareTextView) -> Void = { _ in }
}

@MainActor private struct LabelFixture: View {
    @ObservedObject var state: LabelState
    var body: some View {
        KeyboardTextView(text: $state.text, editable: true,
            accessibilityLabel: state.label, accessibilityHelp: "Type text.",
            onTab: {}, onBackTab: {}, onReady: state.ready)
    }
}

final class TextViewAccessibilityTests: XCTestCase {
    @MainActor func testLabelUpdatesWhenModeChanges() async throws {
        let ready = expectation(description: "Native editor created")
        let state = LabelState()
        var editor: TabAwareTextView?
        state.ready = { editor = $0; ready.fulfill() }
        let host = NSHostingView(rootView: LabelFixture(state: state))
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 500, height: 200),
            styleMask: [.titled], backing: .buffered, defer: false)
        window.contentView = host
        host.layoutSubtreeIfNeeded()
        await fulfillment(of: [ready], timeout: 2)
        XCTAssertEqual(editor?.accessibilityLabel(), "Speech text")
        state.label = "Transcript"
        try await Task.sleep(nanoseconds: 100_000_000)
        host.layoutSubtreeIfNeeded()
        XCTAssertEqual(editor?.accessibilityLabel(), "Transcript")
        state.label = "Voice description"
        try await Task.sleep(nanoseconds: 100_000_000)
        host.layoutSubtreeIfNeeded()
        XCTAssertEqual(editor?.accessibilityLabel(), "Voice description")
        XCTAssertEqual(editor?.string, "Example text")
    }
}
