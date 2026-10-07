import AppKit

enum SpeechTags {
    static let guideURL = URL(string: "https://elevenlabs.io/docs/best-practices/prompting")!
    static let examples = ["curious", "crying", "mischievously", "whispers", "shouts", "laughs", "clears throat", "sighs", "excited", "sarcastic", "exhales"]
    static func normalize(_ value: String) throws -> String {
        var tag = value.trimmingCharacters(in: .whitespacesAndNewlines)
        if tag.hasPrefix("["), tag.hasSuffix("]"), tag.count > 1 { tag = String(tag.dropFirst().dropLast()).trimmingCharacters(in: .whitespacesAndNewlines) }
        guard !tag.isEmpty, tag.utf16.count <= 64, !tag.contains("["), !tag.contains("]"),
              !tag.unicodeScalars.contains(where: { CharacterSet.controlCharacters.contains($0) }) else {
            throw SpeechError.validation("Enter one tag of up to 64 characters, without line breaks or nested brackets.")
        }
        return tag
    }
    static func validated(_ tags: [String]) throws -> [String] {
        guard tags.count <= 200 else { throw SpeechError.validation("Save no more than 200 custom tags.") }
        var result: [String] = []
        for value in tags {
            let tag = try normalize(value)
            if !result.contains(where: { $0.caseInsensitiveCompare(tag) == .orderedSame }) { result.append(tag) }
        }
        return result
    }
    static func insertion(_ tag: String, into text: String, at position: Int, maximum: Int) throws -> (String, Int) {
        let value = "[\(try normalize(tag))] "
        guard text.utf16.count + value.utf16.count <= maximum, position >= 0,
              let utf16Index = text.utf16.index(text.utf16.startIndex, offsetBy: position, limitedBy: text.utf16.endIndex),
              let index = String.Index(utf16Index, within: text) else {
            throw SpeechError.validation("There is not enough room for this tag. Shorten the text first.")
        }
        var result = text; result.insert(contentsOf: value, at: index)
        return (result, position + value.utf16.count)
    }
}

@MainActor final class SpeechTagPicker: NSObject, NSTableViewDataSource, NSTableViewDelegate, NSSearchFieldDelegate {
    private let panel = HelpPanel(contentRect: NSRect(x: 0, y: 0, width: 610, height: 440), styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
    private let search = NSSearchField()
    private let custom = NSTextField()
    private let table = NSTableView()
    private var customs: [String] = []
    private var rows: [String] = []
    private var result: String?
    private let remove = NSButton(title: "Remove Custom Tag", target: nil, action: nil)
    private let defaults: UserDefaults

    init(defaults: UserDefaults = .standard) { self.defaults = defaults; super.init() }

    func choose() -> String? {
        do { customs = try SpeechTags.validated(defaults.stringArray(forKey: "customSpeechTags") ?? []) }
        catch { let alert = NSAlert(); alert.messageText = "Could not open speech tags"; alert.informativeText = error.localizedDescription; alert.runModal(); return nil }
        let owner = NSApp.keyWindow; let responder = owner?.firstResponder
        panel.title = "Insert Speech Tag"; panel.isReleasedWhenClosed = false; panel.isExcludedFromWindowsMenu = true
        panel.finish = { NSApp.stopModal() }
        search.placeholderString = "Search tags"; search.setAccessibilityLabel("Search tags"); search.setAccessibilityHelp("Filter the example and custom tags."); search.delegate = self
        custom.placeholderString = "Custom tag"; custom.setAccessibilityLabel("Custom tag"); custom.setAccessibilityHelp("Enter a delivery instruction, with or without square brackets.")
        let column = NSTableColumn(identifier: NSUserInterfaceItemIdentifier("tag")); column.title = "Speech Tags"; table.addTableColumn(column)
        table.headerView = nil; table.dataSource = self; table.delegate = self
        table.setAccessibilityLabel("Speech tags"); table.setAccessibilityHelp("Choose an example or saved custom tag with the arrow keys.")
        let scroll = NSScrollView(); scroll.hasVerticalScroller = true; scroll.documentView = table
        let insert = button("Insert", #selector(insertTag), "Insert before the saved cursor position without replacing text.")
        let save = button("Save Custom Tag", #selector(saveTag), "Save this instruction for reuse.")
        remove.target = self; remove.action = #selector(removeTag); remove.setAccessibilityHelp("Remove a saved custom tag; built-in examples remain available.")
        let guide = button("Official Guide", #selector(openGuide), "Open the current ElevenLabs prompting guide.")
        let close = button("Close", #selector(closePicker), "Close without inserting a tag.")
        insert.keyEquivalent = "\r"
        let buttons = NSStackView(views: [insert, save, remove, guide, close]); buttons.spacing = 8
        let stack = NSStackView(views: [search, scroll, custom, buttons]); stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 12; stack.translatesAutoresizingMaskIntoConstraints = false
        let root = panel.contentView!; root.addSubview(stack)
        NSLayoutConstraint.activate([stack.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16), stack.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16), stack.topAnchor.constraint(equalTo: root.topAnchor, constant: 16), stack.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -16), search.widthAnchor.constraint(equalTo: stack.widthAnchor), scroll.widthAnchor.constraint(equalTo: stack.widthAnchor), scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 180), custom.widthAnchor.constraint(equalTo: stack.widthAnchor)])
        let chain: [NSView] = [search, table, custom, insert, save, remove, guide, close]
        for index in chain.indices { chain[index].nextKeyView = chain[(index + 1) % chain.count] }
        refresh(); panel.center(); panel.makeKeyAndOrderFront(nil); panel.makeFirstResponder(search)
        NSApp.runModal(for: panel); panel.orderOut(nil)
        if let owner, owner.isVisible { owner.makeKeyAndOrderFront(nil); if let responder { owner.makeFirstResponder(responder) } }
        return result
    }
    private func button(_ title: String, _ action: Selector, _ hint: String) -> NSButton { let value = NSButton(title: title, target: self, action: action); value.setAccessibilityHelp(hint); return value }
    func numberOfRows(in tableView: NSTableView) -> Int { rows.count }
    func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? { NSTextField(labelWithString: rows[row]) }
    func tableViewSelectionDidChange(_ notification: Notification) {
        guard rows.indices.contains(table.selectedRow) else { remove.isEnabled = false; return }
        custom.stringValue = rows[table.selectedRow]
        remove.isEnabled = customs.contains(where: { $0.caseInsensitiveCompare(custom.stringValue) == .orderedSame })
    }
    func controlTextDidChange(_ notification: Notification) { if notification.object as? NSSearchField === search { refresh() } }
    private func refresh() {
        rows = SpeechTags.examples
        for value in customs where !rows.contains(where: { $0.caseInsensitiveCompare(value) == .orderedSame }) { rows.append(value) }
        rows = rows.filter { search.stringValue.isEmpty || $0.localizedCaseInsensitiveContains(search.stringValue) }.sorted()
        table.reloadData(); if !rows.isEmpty { table.selectRowIndexes(IndexSet(integer: 0), byExtendingSelection: false) }; remove.isEnabled = !rows.isEmpty && customs.contains(custom.stringValue)
    }
    private func report(_ error: Error) { let alert = NSAlert(); alert.messageText = "Could not use tag"; alert.informativeText = error.localizedDescription; alert.runModal() }
    @objc private func insertTag() { do { result = try SpeechTags.normalize(custom.stringValue); NSApp.stopModal() } catch { report(error) } }
    @objc private func saveTag() {
        do { let value = try SpeechTags.normalize(custom.stringValue); customs = try SpeechTags.validated(customs + [value]); defaults.set(customs, forKey: "customSpeechTags"); search.stringValue = ""; refresh(); custom.stringValue = value }
        catch { report(error) }
    }
    @objc private func removeTag() { guard rows.indices.contains(table.selectedRow) else { return }; let tag = rows[table.selectedRow]; customs.removeAll { $0.caseInsensitiveCompare(tag) == .orderedSame }; defaults.set(customs, forKey: "customSpeechTags"); refresh() }
    @objc private func openGuide() { NSWorkspace.shared.open(SpeechTags.guideURL) }
    @objc private func closePicker() { NSApp.stopModal() }
}
