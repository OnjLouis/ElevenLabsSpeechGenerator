import AppKit
import SwiftUI
import XCTest
@testable import ElevenLabsSpeechGeneratorMac

final class PreferenceTabsTests: XCTestCase {
    @MainActor func testAudioSelectionAndNativeTabChangesStayInSync() {
        _ = NSApplication.shared
        let tabs = PreferenceTabs.makeTabView(["General", "Audio"].map { name in PreferenceTabs.Pane(name) { Text(name) } })
        var selected = "Audio"
        let coordinator = PreferenceTabs.Coordinator(Binding(get: { selected }, set: { selected = $0 }))
        tabs.delegate = coordinator
        PreferenceTabs.select(selected, in: tabs)
        XCTAssertEqual(tabs.selectedTabViewItem?.label, "Audio")
        tabs.selectTabViewItem(at: 0)
        XCTAssertEqual(selected, "General")
        PreferenceTabs.select(selected, in: tabs)
        XCTAssertEqual(tabs.selectedTabViewItem?.label, "General")
    }
    @MainActor func testNativeTabAccessibilityLabels() {
        _ = NSApplication.shared
        let names = ["General", "API key", "Updates"]
        let tabs = PreferenceTabs.makeTabView(names.map { name in
            PreferenceTabs.Pane(name) { Text(name) }
        })
        let window = NSWindow(contentRect: NSRect(x: 0, y: 0, width: 660, height: 420), styleMask: [.titled], backing: .buffered, defer: false)
        window.contentView = tabs
        defer { window.orderOut(nil) }
        XCTAssertEqual(tabs.tabViewType, .topTabsBezelBorder)
        let elements = tabs.accessibilityTabs() ?? []
        XCTAssertEqual(elements.count, names.count)
        let labels = elements.compactMap { element -> String? in
            guard let object = element as? NSObject, object.responds(to: NSSelectorFromString("accessibilityAttributeValue:")) else { return nil }
            return object.perform(NSSelectorFromString("accessibilityAttributeValue:"), with: NSAccessibility.Attribute.title.rawValue)?.takeUnretainedValue() as? String
        }
        XCTAssertEqual(labels, names)
        tabs.selectTabViewItem(at: 1)
        XCTAssertEqual(tabs.selectedTabViewItem?.label, "API key")
        XCTAssertEqual(tabs.tabViewItems.map(\.label), names)
    }
}
