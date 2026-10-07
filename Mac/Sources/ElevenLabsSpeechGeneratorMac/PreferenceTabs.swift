import AppKit
import SwiftUI

struct PreferenceTabs: NSViewRepresentable {
    struct Pane {
        let title: String
        let content: AnyView
        init<Content: View>(_ title: String, @ViewBuilder content: () -> Content) {
            self.title = title
            self.content = AnyView(content())
        }
    }

    let panes: [Pane]
    @Binding var selection: String

    init(panes: [Pane], selection: Binding<String> = .constant("General")) {
        self.panes = panes; _selection = selection
    }
    final class Coordinator: NSObject, NSTabViewDelegate {
        var selection: Binding<String>
        var updating = false
        init(_ selection: Binding<String>) { self.selection = selection }
        func tabView(_ tabView: NSTabView, didSelect tabViewItem: NSTabViewItem?) {
            if !updating, let title = tabViewItem?.label, selection.wrappedValue != title { selection.wrappedValue = title }
        }
    }
    func makeCoordinator() -> Coordinator { Coordinator($selection) }

    func makeNSView(context: Context) -> NSTabView {
        let tabs = Self.makeTabView(panes)
        Self.select(selection, in: tabs)
        tabs.delegate = context.coordinator
        return tabs
    }

    static func makeTabView(_ panes: [Pane]) -> NSTabView {
        let tabs = NSTabView()
        tabs.tabViewType = .topTabsBezelBorder
        tabs.setAccessibilityLabel("Preference categories")
        for pane in panes {
            let item = NSTabViewItem(identifier: pane.title)
            item.label = pane.title
            item.view = NSHostingView(rootView: pane.content)
            tabs.addTabViewItem(item)
        }
        return tabs
    }

    func updateNSView(_ tabs: NSTabView, context: Context) {
        context.coordinator.selection = $selection
        context.coordinator.updating = true
        defer { context.coordinator.updating = false }
        // Retain the native tab items so updates do not move keyboard focus.
        for (item, pane) in zip(tabs.tabViewItems, panes) {
            item.label = pane.title
            (item.view as? NSHostingView<AnyView>)?.rootView = pane.content
        }
        Self.select(selection, in: tabs)
    }
    static func select(_ title: String, in tabs: NSTabView) {
        if tabs.selectedTabViewItem?.label != title, let item = tabs.tabViewItems.first(where: { $0.label == title }) {
            tabs.selectTabViewItem(item)
        }
    }
}
