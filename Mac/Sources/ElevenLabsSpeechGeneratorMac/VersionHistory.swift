import AppKit

@MainActor
enum VersionHistory {
    static let text = """
    1.1.1

    Version History is now available from the Help menu, including when offline.

    1.1.0

    - Optional long-text speech generation uses sentence-aware splitting, assembles one WAV per variation and can recover completed parts.
    - Voice cloning shows readable upload progress and the final result.
    - Windows context help provides specific explanations for cloning controls, Preferences and other dialogs.
    - The Mac variations field accepts a typed number.
    """

    static func make() -> (NSAlert, NSTextView) {
        let alert = NSAlert()
        alert.messageText = "Version history"
        alert.addButton(withTitle: "Close").keyEquivalent = "\u{1b}"
        let scroll = NSScrollView(frame: NSRect(x: 0, y: 0, width: 600, height: 300))
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        let editor = NSTextView(frame: scroll.bounds)
        editor.isEditable = false
        editor.isSelectable = true
        editor.isRichText = false
        editor.isHorizontallyResizable = false
        editor.isVerticallyResizable = true
        editor.textContainer?.widthTracksTextView = true
        editor.autoresizingMask = [.width]
        editor.font = .systemFont(ofSize: NSFont.systemFontSize)
        editor.string = text
        editor.setAccessibilityLabel("Version history")
        scroll.documentView = editor
        alert.accessoryView = scroll
        alert.window.initialFirstResponder = editor
        return (alert, editor)
    }

    static func show() {
        let (alert, editor) = make()
        let window = alert.window
        DispatchQueue.main.async { window.makeFirstResponder(editor) }
        alert.runModal()
    }
}
