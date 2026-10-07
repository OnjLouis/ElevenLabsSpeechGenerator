import AppKit
import SwiftUI

final class TabAwareTextView: NSTextView {
    var tabAction: (() -> Void)?
    var backTabAction: (() -> Void)?

    override func keyDown(with event: NSEvent) {
        if event.keyCode == 48 && event.modifierFlags.intersection([.command, .option, .control]).isEmpty {
            if event.modifierFlags.contains(.shift) { backTabAction?() }
            else { tabAction?() }
            return
        }
        super.keyDown(with: event)
    }
}

enum PromptInputLimiter {
    static func replacement(_ text: String, range: NSRange, with proposed: String, limit: Int) -> String? {
        guard let selection = Range(range, in: text) else { return nil }
        let available = max(0, limit - text.utf16.count + text[selection].utf16.count)
        var clipped = ""
        var used = 0
        for character in proposed {
            let size = String(character).utf16.count
            if used + size > available { break }
            clipped.append(character)
            used += size
        }
        return clipped == proposed ? nil : clipped
    }
}

struct KeyboardTextView: NSViewRepresentable {
    @Binding var text: String
    var editable: Bool
    var accessibilityLabel: String
    var accessibilityHelp: String
    var maximumLength: Int? = nil
    var onTab: () -> Void
    var onBackTab: () -> Void
    var onReady: (TabAwareTextView) -> Void

    func makeCoordinator() -> Coordinator { Coordinator(parent: self) }

    func makeNSView(context: Context) -> NSScrollView {
        let scroll = NSScrollView()
        scroll.hasVerticalScroller = true
        scroll.borderType = .bezelBorder
        scroll.autohidesScrollers = true
        let editor = TabAwareTextView(frame: .zero)
        editor.isRichText = false
        editor.isEditable = editable
        editor.isSelectable = true
        editor.allowsUndo = editable
        editor.font = .systemFont(ofSize: NSFont.systemFontSize)
        editor.textContainerInset = NSSize(width: 7, height: 7)
        editor.isHorizontallyResizable = false
        editor.isVerticallyResizable = true
        editor.textContainer?.widthTracksTextView = true
        editor.autoresizingMask = [.width]
        editor.string = text
        editor.setAccessibilityLabel(accessibilityLabel)
        editor.setAccessibilityHelp(accessibilityHelp)
        editor.delegate = context.coordinator
        editor.tabAction = onTab
        editor.backTabAction = onBackTab
        scroll.documentView = editor
        DispatchQueue.main.async { onReady(editor) }
        return scroll
    }

    func updateNSView(_ scroll: NSScrollView, context: Context) {
        guard let editor = scroll.documentView as? TabAwareTextView else { return }
        context.coordinator.parent = self
        editor.tabAction = onTab
        editor.backTabAction = onBackTab
        editor.isEditable = editable
        editor.setAccessibilityLabel(accessibilityLabel)
        editor.setAccessibilityHelp(accessibilityHelp)
        if editor.string != text {
            let focused = editor.window?.firstResponder === editor
            let selection = editor.selectedRange()
            let scrollOrigin = scroll.contentView.bounds.origin
            editor.string = text
            let location = focused ? min(selection.location, (text as NSString).length) : (text as NSString).length
            editor.setSelectedRange(NSRange(location: location, length: focused ? min(selection.length, (text as NSString).length - location) : 0))
            if focused { scroll.contentView.scroll(to: scrollOrigin); scroll.reflectScrolledClipView(scroll.contentView) }
            if !focused { editor.scrollToEndOfDocument(nil) }
        }
    }

    final class Coordinator: NSObject, NSTextViewDelegate {
        var parent: KeyboardTextView
        init(parent: KeyboardTextView) { self.parent = parent }

        func textView(_ textView: NSTextView, shouldChangeTextIn affectedCharRange: NSRange, replacementString: String?) -> Bool {
            guard parent.editable, let maximumLength = parent.maximumLength, let replacementString,
                  let clipped = PromptInputLimiter.replacement(textView.string, range: affectedCharRange,
                      with: replacementString, limit: maximumLength) else { return true }
            if !clipped.isEmpty { textView.insertText(clipped, replacementRange: affectedCharRange) }
            return false
        }

        func textDidChange(_ notification: Notification) {
            guard let editor = notification.object as? NSTextView, parent.editable else { return }
            if let maximumLength = parent.maximumLength, editor.string.utf16.count > maximumLength {
                let clipped = PromptInputLimiter.replacement("", range: NSRange(location: 0, length: 0), with: editor.string, limit: maximumLength) ?? editor.string
                let position = min(editor.selectedRange().location, (clipped as NSString).length)
                editor.string = clipped
                editor.setSelectedRange(NSRange(location: position, length: 0))
            }
            parent.text = editor.string
        }
    }
}
