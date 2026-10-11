import XCTest
import AppKit
@testable import ElevenLabsSpeechGeneratorMac

final class SpeechTagTests: XCTestCase {
    func testNormalizationAndRejection() throws {
        XCTAssertEqual(try SpeechTags.normalize(" [whispers] "), "whispers")
        for value in ["[]", "one\ntwo", "[one][two]", String(repeating: "x", count: 65)] {
            XCTAssertThrowsError(try SpeechTags.normalize(value))
        }
        XCTAssertEqual(try SpeechTags.validated(["Direction", "DIRECTION"]), ["Direction"])
    }
    func testInsertionPreservesPassageAndLimits() throws {
        let (text, cursor) = try SpeechTags.insertion("whispers", into: "Hello world", at: 6, maximum: 100)
        XCTAssertEqual(text, "Hello [whispers] world")
        XCTAssertEqual(cursor, 17)
        XCTAssertThrowsError(try SpeechTags.insertion("laughs", into: text, at: cursor, maximum: text.utf16.count))
        XCTAssertThrowsError(try SpeechTags.insertion("laughs", into: "😀", at: 1, maximum: 100))
    }
    @MainActor func testDialogHelpExplainsSettingsWithoutValues() {
        XCTAssertTrue(ContextHelp.description(title: "Stability", hint: nil).contains("emotional variation"))
        XCTAssertTrue(ContextHelp.description(title: "API key", hint: nil).contains("private credential"))
    }
    @MainActor func testNativePickerSavesCustomTagAndReturnsIt() {
        let suite = "SpeechTagTests-" + UUID().uuidString
        let defaults = UserDefaults(suiteName: suite)!
        defer { defaults.removePersistentDomain(forName: suite) }
        func children(_ root: NSView) -> [NSView] { [root] + root.subviews.flatMap(children) }
        func activate(_ title: String, in views: [NSView]) {
            guard let button = views.compactMap({ $0 as? NSButton }).first(where: { $0.title == title }),
                  button.isEnabled, let action = button.action else { XCTFail("Missing enabled action: \(title)"); NSApp.stopModal(); return }
            XCTAssertTrue(NSApp.sendAction(action, to: button.target, from: button))
        }
        let choose = DispatchWorkItem {
            guard let window = NSApp.windows.first(where: { $0.title == "Insert Speech Tag" }), let root = window.contentView else { XCTFail("Picker did not open"); NSApp.stopModal(); return }
            let views = children(root)
            XCTAssertTrue(window.isExcludedFromWindowsMenu)
            let custom = views.compactMap { $0 as? NSTextField }.first { $0.accessibilityLabel() == "Custom tag" }!
            XCTAssertTrue(ContextHelp.description(title: "Custom tag", hint: nil).contains("without replacing"))
            custom.stringValue = "my test direction"
            activate("Save Custom Tag", in: views)
            XCTAssertEqual(defaults.stringArray(forKey: "customSpeechTags"), ["my test direction"])
            activate("Insert", in: views)
        }
        let guardWork = DispatchWorkItem { XCTFail("Picker timed out"); NSApp.stopModal() }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.2, execute: choose)
        DispatchQueue.main.asyncAfter(deadline: .now() + 3, execute: guardWork)
        XCTAssertEqual(SpeechTagPicker(defaults: defaults).choose(), "my test direction")
        choose.cancel(); guardWork.cancel()
    }
}
