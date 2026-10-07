import AppKit

struct FocusedHelpContent {
    let title: String
    let instructions: String
    let restoreView: NSView?

    @MainActor static func accessibilityFocus() -> NSObject? {
        NSApp.accessibilityFocusedUIElement as? NSObject
    }

    @MainActor static func capture(in window: NSWindow?) -> FocusedHelpContent {
        let responder = window?.firstResponder
        var view = responder as? NSView
        if let editor = view as? NSTextView, editor.isFieldEditor, let root = window?.contentView {
            func editingField(_ candidate: NSView) -> NSTextField? {
                if let field = candidate as? NSTextField, field.currentEditor() === responder { return field }
                for child in candidate.subviews { if let found = editingField(child) { return found } }
                return nil
            }
            view = editingField(root)
        }
        let focused = window === NSApp.keyWindow ? accessibilityFocus() : nil
        func metadata(_ object: NSObject?, _ selector: String) -> String? {
            guard let object, object.responds(to: NSSelectorFromString(selector)),
                  let value = object.perform(NSSelectorFromString(selector))?.takeUnretainedValue() as? String,
                  !value.isEmpty else { return nil }
            return value
        }
        // Descriptions only: field values can contain credentials or private text.
        let hint = metadata(focused, "accessibilityHelp") ?? view?.accessibilityHelp()
        let title = metadata(focused, "accessibilityLabel") ?? view?.accessibilityLabel()
            ?? (view as? NSButton)?.title ?? "Current window"
        return FocusedHelpContent(title: title,
            instructions: ContextHelp.description(title: title, hint: hint),
            restoreView: view)
    }
}

final class HelpPanel: NSPanel {
    var finish: (() -> Void)?
    var manualButton: NSButton?
    var closeButton: NSButton?
    override func sendEvent(_ event: NSEvent) {
        let modifiers = event.modifierFlags.intersection([.command, .control, .option, .shift])
        if event.type == .keyDown {
            if event.keyCode == 53 && modifiers.isEmpty || event.keyCode == 13 && modifiers == .command {
                finish?(); return
            }
            if event.keyCode == 48 && (modifiers.isEmpty || modifiers == .shift) {
                if modifiers == .shift { selectPreviousKeyView(nil) } else { selectNextKeyView(nil) }
                return
            }
        }
        super.sendEvent(event)
    }
    override func cancelOperation(_ sender: Any?) { finish?() }
    override func performClose(_ sender: Any?) { finish?() }
}

@MainActor final class ContextHelp: NSObject {
    static func description(title: String, hint: String?) -> String {
        switch title.lowercased() {
        case "api key": return "An ElevenLabs key is a private credential that lets this app use your account. Create one on the ElevenLabs API keys page, allow the features you need, then paste it in Settings and choose Save API Key. Test API Key checks Models, Voices and balance read access without generating audio. Never share your key; requests may spend your credits."
        case "stability": return "Controls consistency of delivery. Lower values allow more emotional variation but can produce uneven results; higher values are steadier and may sound less expressive. For a numeric field, zero to one is the full range. Eleven v3 and Dialogue offer Creative, Natural and Robust choices."
        case "similarity": return "Controls how closely the result preserves the selected voice's character, from zero to one. Higher is not always better; compare short generations if the voice sounds unnatural."
        case "style": return "Exaggerates the speaker's original style on supported models. Zero disables exaggeration; higher values can reduce stability. This setting is not offered when unsupported."
        case "speed": return "One is normal speech speed. Values below one slow it down and values above one speed it up. This setting is not offered when unsupported."
        case "speaker boost": return "Enhances similarity to the original speaker on supported models. It may increase generation latency. This is not a volume control."
        case "speech tags", "custom tag": return "Choose an example or enter your own delivery instruction. Insert places it before the saved cursor without replacing selected text. Save Custom Tag keeps it for reuse. Instructions use square brackets, not paired opening and closing tags. Results depend on the voice; Official Guide opens current guidance."
        default: return hint ?? "Open the manual for instructions about this window."
        }
    }
    static let shared = ContextHelp()
    private var monitor: Any?
    private var manual: (() -> Void)?
    private var panel: HelpPanel?
    private var owner: NSWindow?
    private weak var restoreView: NSView?

    func install(openManual: @escaping () -> Void) {
        manual = openManual
        guard monitor == nil else { return }
        monitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            let modifiers = event.modifierFlags.intersection([.command, .control, .option, .shift])
            guard event.keyCode == 122, modifiers.isEmpty else { return event }
            self?.show(); return nil
        }
    }
    func stop() {
        if let monitor { NSEvent.removeMonitor(monitor) }
        monitor = nil; manual = nil
    }
    func show() {
        guard panel == nil, let owner = NSApp.keyWindow else { return }
        let content = FocusedHelpContent.capture(in: owner)
        self.owner = owner; restoreView = content.restoreView
        let dialog = Self.makePanel(content)
        panel = dialog
        dialog.finish = { [weak self] in self?.finish() }
        dialog.manualButton?.target = self; dialog.manualButton?.action = #selector(openManual)
        dialog.closeButton?.target = self; dialog.closeButton?.action = #selector(closeHelp)
        dialog.center(); dialog.makeKeyAndOrderFront(nil)
        dialog.makeFirstResponder(dialog.initialFirstResponder)
        NSApp.runModal(for: dialog)
        dialog.orderOut(nil); panel = nil
        if owner.isVisible {
            owner.makeKeyAndOrderFront(nil)
            if let restoreView, restoreView.window === owner { owner.makeFirstResponder(restoreView) }
        }
        self.owner = nil; restoreView = nil
    }
    @objc private func openManual() { manual?() }
    @objc private func closeHelp() { finish() }
    private func finish() { NSApp.stopModal() }

    static func makePanel(_ content: FocusedHelpContent) -> HelpPanel {
        let panel = HelpPanel(contentRect: NSRect(x: 0, y: 0, width: 580, height: 300),
            styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        panel.title = "Help: " + content.title
        panel.isReleasedWhenClosed = false
        panel.isExcludedFromWindowsMenu = true
        let editor = TabAwareTextView(frame: NSRect(x: 0, y: 0, width: 540, height: 220))
        editor.isEditable = false; editor.isSelectable = true; editor.isRichText = false
        editor.font = .systemFont(ofSize: NSFont.systemFontSize)
        editor.string = content.instructions
        editor.setAccessibilityLabel(content.title + " help")
        editor.setSelectedRange(NSRange(location: 0, length: 0))
        editor.isVerticallyResizable = true; editor.isHorizontallyResizable = false
        editor.autoresizingMask = [.width]; editor.textContainer?.widthTracksTextView = true
        let scroll = NSScrollView(); scroll.hasVerticalScroller = true; scroll.documentView = editor
        let manual = NSButton(title: "Open Manual", target: nil, action: nil)
        manual.setAccessibilityHelp("Open the complete manual.")
        let close = NSButton(title: "Close", target: nil, action: nil)
        panel.manualButton = manual; panel.closeButton = close
        close.setAccessibilityHelp("Close help and return to the previous control.")
        let buttons = NSStackView(views: [manual, close]); buttons.spacing = 12
        let stack = NSStackView(views: [scroll, buttons]); stack.orientation = .vertical
        stack.alignment = .leading; stack.spacing = 12; stack.translatesAutoresizingMaskIntoConstraints = false
        panel.contentView!.addSubview(stack)
        let root = panel.contentView!
        NSLayoutConstraint.activate([
            stack.leadingAnchor.constraint(equalTo: root.leadingAnchor, constant: 16),
            stack.trailingAnchor.constraint(equalTo: root.trailingAnchor, constant: -16),
            stack.topAnchor.constraint(equalTo: root.topAnchor, constant: 16),
            stack.bottomAnchor.constraint(equalTo: root.bottomAnchor, constant: -16),
            scroll.widthAnchor.constraint(equalTo: stack.widthAnchor),
            scroll.heightAnchor.constraint(greaterThanOrEqualToConstant: 120)
        ])
        editor.nextKeyView = manual; manual.nextKeyView = close; close.nextKeyView = editor
        editor.tabAction = { [weak panel] in panel?.makeFirstResponder(manual) }
        editor.backTabAction = { [weak panel] in panel?.makeFirstResponder(close) }
        panel.initialFirstResponder = editor
        return panel
    }
}
