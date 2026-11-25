import AppKit

@MainActor
enum VersionHistory {
    static let text = """
    1.3.0

    Added Scribe v3 with number formatting and secondary languages. New transcription drafts use v3; existing projects keep their saved model. Voice Changer uses the supported ElevenLabs endpoint. Credit Balance shows the percentage remaining and lists active overage billing and any reported charge after the reset. Status log shows the reported credits used for a text-to-speech run, including newly generated parts. The Mac updater validates its working folders and preserves the previous app if rollback cannot safely finish.

    1.2.3

    Copy Voice ID in Voice Library copies the selected voice ID with Command+Shift+C. The ID is also listed in Details.

    1.2.2

    Generation History now displays voice names and full transcripts for current speech and dialogue recordings. Search includes the full transcript on the current page. History playback and downloads retrieve playable WAV files, including recordings originally generated as raw PCM.

    1.2.1

    Generation History, Voice Library and pronunciation dictionaries now offer Previous Page as well as Next Page. Navigation remains usable after failed requests; Refresh returns to the first page.

    1.2.0

    - Voice Library can automatically preview selected voices; Return or Preview / Stop toggles playback.
    - Rename voices owned by your account without changing their IDs or recordings.
    - Expanded the speech-tag picker with current official examples.
    - Fixed Tab and Shift-Tab navigation through the speech-tag picker.

    1.1.1

    Version History is now available from the Help menu, including when offline.
    The app opens with keyboard focus in the main text editor when the selected mode has one.

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
