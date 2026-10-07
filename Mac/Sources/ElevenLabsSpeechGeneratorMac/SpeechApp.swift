import AppKit
import SwiftUI

@main struct ElevenLabsSpeechGeneratorApp: App {
    @StateObject private var model = AppModel()
    @Environment(\.openSettings) private var openSettings
    var body: some Scene {
        Window("ElevenLabs Speech Generator", id: "main") {
            MainView(model: model).frame(minWidth: 780, minHeight: 720)
                .onReceive(NotificationCenter.default.publisher(for: NSApplication.willTerminateNotification)) { _ in model.shutdown() }
        }.commands {
            CommandGroup(replacing: .newItem) {
                Button("New Project", action: model.newProject).keyboardShortcut("n")
                Button("Open Prompt or Project...", action: model.openProject).keyboardShortcut("o")
                Button("Save Project") { model.saveProject() }.keyboardShortcut("s")
                Button("Save Project As...") { model.saveProject(as: true) }.keyboardShortcut("s", modifiers: [.command, .shift])
                Divider(); Button("Open Output Folder", action: model.openFolder).keyboardShortcut("o", modifiers: [.command, .shift])
            }
            CommandGroup(after: .appInfo) {
                Button("Check for Updates...") { model.checkUpdates() }.keyboardShortcut(KeyEquivalent("\u{F704}"), modifiers: .shift)
                Button("Donate") { model.open("https://onj.me/donate") }
            }
            CommandMenu("Controls") {
                Button("Generate", action: model.generate).keyboardShortcut(.return, modifiers: .command).disabled(model.busy)
                Button("Edit Dialogue...") { model.openTool(.dialogue) }.keyboardShortcut("p").disabled(model.busy || model.project.mode != .dialogue)
                Button("Insert Speech Tag...", action: model.insertSpeechTag).keyboardShortcut("t", modifiers: [.command, .shift]).disabled(!model.canInsertSpeechTag)
                Button("Focus Balance") { model.focus("balance") }.keyboardShortcut("b")
                Button("Focus Status Log") { model.focus("status") }.keyboardShortcut("t")
                Button("Focus Speech Text") { model.focus("prompt") }.keyboardShortcut("m", modifiers: [.command, .shift])
                Button("Refresh Balance", action: model.refreshBalance).keyboardShortcut("r")
            }
            CommandMenu("Tools") {
                Button("Automatic Dubbing...") { model.openTool(.dubbing) }.keyboardShortcut("b", modifiers: [.command, .shift]).disabled(model.busy)
                Button("Voice Library...") { model.openTool(.voices) }.keyboardShortcut("l"); Button("Clone Voice...") { model.openTool(.clone) }.keyboardShortcut("n", modifiers: [.command, .shift])
                Button("Generation History...") { model.openTool(.history) }.keyboardShortcut("h", modifiers: [.command, .shift])
                Button("Pronunciation Dictionaries...") { model.openTool(.dictionaries) }.keyboardShortcut("d", modifiers: [.command, .shift])
                Button("Audio Settings...") { model.settingsTab = "Audio"; openSettings() }.keyboardShortcut("u").disabled(model.busy)
            }
            CommandGroup(replacing: .help) {
                Button("Help for Focused Control") { ContextHelp.shared.show() }.keyboardShortcut(KeyEquivalent("\u{F704}"), modifiers: [])
                Button("ElevenLabs Speech Generator Help", action: model.manual)
                Button("Project Page") { model.open(AppModel.repo) }.keyboardShortcut(KeyEquivalent("\u{F704}"), modifiers: .command)
                Button("Usage Analytics") { model.open("https://elevenlabs.io/app/developers/analytics/usage") }.keyboardShortcut(KeyEquivalent("\u{F704}"), modifiers: .option)
                Button("Software Catalogue") { model.open("https://onj.me/software") }; Button("Donate") { model.open("https://onj.me/donate") }
            }
        }
        Settings { SettingsView(model: model).frame(width: 660, height: 420) }
    }
}
