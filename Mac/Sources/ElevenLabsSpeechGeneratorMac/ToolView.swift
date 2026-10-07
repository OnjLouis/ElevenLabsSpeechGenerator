import AppKit
import SwiftUI

struct ToolView: View {
    @ObservedObject var model: AppModel
    let kind: ToolKind
    @State private var name = ""
    @State private var description = ""
    @State private var word = ""
    @State private var pronunciation = ""
    @State private var ruleType = "Alias"
    @State private var samples: [URL] = []
    @State private var consent = false
    @State private var lines: [DialogueLine] = []
    @State private var selectedLine: UUID?
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(title).font(.headline)
            content
            HStack { Spacer(); if model.busy { Button("Cancel Request", action: model.cancel).accessibilityHint("Cancel the active request.") }; Button("Close") { model.tool = nil }.disabled(model.busy).keyboardShortcut(.cancelAction).accessibilityHint("Close this window.") }
        }.padding(20).onAppear { lines = model.project.dialogue; selectedLine = lines.first?.id }
            .onChange(of: lines.map(\.text)) { _, _ in updateTagLimit() }
            .onChange(of: selectedLine) { _, _ in updateTagLimit() }
            .onDisappear { if kind == .dialogue { model.dialogueTextView = nil } }
            .interactiveDismissDisabled(model.busy).onExitCommand { if model.busy { model.cancel() } else { model.tool = nil } }
    }
    private var title: String { switch kind { case .voices: return "Voice library"; case .history: return "Generation history"; case .dictionaries: return "Pronunciation dictionaries"; case .clone: return "Clone voice"; case .dialogue: return "Dialogue"; case .voiceSettings: return "Voice settings"; case .settings: return "Settings"; case .dubbing: return "Automatic dubbing" } }
    @ViewBuilder private var content: some View {
        switch kind {
        case .dialogue: dialogue
        case .voiceSettings: voiceSettings
        case .clone: clone
        case .voices, .history, .dictionaries: catalog
        case .settings: SettingsView(model: model)
        case .dubbing: DubbingView(model: model)
        }
    }
    private var catalog: some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack { TextField("Search", text: $model.search).accessibilityHint("Search voices, history or dictionary names."); if kind == .voices { Toggle("Shared Voice Library", isOn: $model.sharedVoices).accessibilityHint("Search public voices instead of your account voices.") }; Button("Refresh") { model.refreshTool() }.disabled(model.busy).accessibilityHint("Reload results using the current search and catalogue choice."); Button("Next Page") { model.refreshTool(next: true) }.disabled(model.busy || model.cursor.isEmpty).accessibilityHint("Load more catalogue results where available.") }
            List(model.catalog, selection: $model.selectedCatalog) { Text($0.name).tag($0.id) }.accessibilityLabel(title).accessibilityHint(kind == .dictionaries ? "Select up to three dictionaries to use for speech." : "Choose an item.").frame(minHeight: 180)
            if let x = model.catalogSelection {
                KeyboardTextView(text: .constant((x.data["description"] as? String ?? x.string("text"))), editable: false, accessibilityLabel: "Details", accessibilityHelp: "Details for the selected item.", onTab: { NSApp.keyWindow?.selectNextKeyView(nil) }, onBackTab: { NSApp.keyWindow?.selectPreviousKeyView(nil) }, onReady: { _ in }).frame(height: 80)
            }
            HStack {
                if kind == .voices { Button("Use Voice", action: model.useVoice).accessibilityHint("Select this voice. Adding a shared voice to your account requires confirmation."); Button("Preview", action: model.previewVoice).accessibilityHint("Play the voice's existing preview without generating new audio."); Button("Delete Voice", action: model.deleteVoice).disabled(model.sharedVoices).accessibilityHint("Permanently remove this account voice after confirmation.") }
                if kind == .history { Button("Download") { model.historyDownload() }.accessibilityHint("Save the selected existing recording locally."); Button("Play") { model.historyDownload(play: true) }.accessibilityHint("Download and play this existing recording without generating it again.") }
                if kind == .dictionaries { Button("Use Selected", action: model.applyDictionaries).accessibilityHint("Use up to three selected dictionaries for speech."); Button("Import PLS...", action: model.importDictionary).accessibilityHint("Upload a lexicon file to create an account dictionary."); Button("Export PLS...", action: model.exportDictionary).accessibilityHint("Save the selected dictionary as a local lexicon file.") }
            }.disabled(model.busy)
            if kind == .dictionaries {
                HStack { TextField("Original text", text: $word).accessibilityHint("The word or phrase to replace."); TextField("Pronunciation", text: $pronunciation).accessibilityHint("The replacement alias or phoneme sequence."); Picker("Rule Type", selection: $ruleType) { ForEach(["Alias", "IPA phoneme", "CMU phoneme"], id: \.self) { Text($0).tag($0) } }.accessibilityHint("Choose an alias, IPA, or CMU phoneme rule.") }
                HStack { Button("Create Dictionary...") { model.dictionaryRule(create: true, word: word, pronunciation: pronunciation, type: ruleType) }; Button("Add Rule") { model.dictionaryRule(create: false, word: word, pronunciation: pronunciation, type: ruleType) }.disabled(model.catalogSelection == nil) }.disabled(model.busy)
            }
        }
    }
    private var clone: some View {
        VStack(alignment: .leading, spacing: 12) {
            TextField("Voice name", text: $name).accessibilityHint("Name for the cloned voice in your account.")
            TextField("Voice description", text: $description).accessibilityHint("Optional description of the voice.")
            List(samples, id: \.self) { Text($0.lastPathComponent) }.accessibilityLabel("Voice samples").frame(minHeight: 120)
            HStack { Button("Add Audio Files...") { let p = NSOpenPanel(); p.allowsMultipleSelection = true; if p.runModal() == .OK { for url in p.urls where !samples.contains(url) { samples.append(url) } } }.accessibilityHint("Add recordings of the voice you have permission to clone."); Button("Clear Samples") { samples = [] }.accessibilityHint("Remove the selected sample list without deleting any files.") }
            Toggle("I have the rights and consent to clone this voice", isOn: $consent).accessibilityHint("Confirm that the speaker has given permission and you have the necessary rights.")
            Button("Create Voice") { model.clone(name: name, description: description, samples: samples, consent: consent) }.accessibilityHint("Upload the samples and create a voice in your account.")
        }.disabled(model.busy)
    }
    private var voiceSettings: some View {
        Form {
            if model.project.modelId == "eleven_v3" || model.project.mode == .dialogue {
                Picker("Stability", selection: $model.project.stability) { Text("Creative, 0 percent").tag(0.0); Text("Natural, 50 percent").tag(0.5); Text("Robust, 100 percent").tag(1.0) }.accessibilityHint("Choose variation or consistency in delivery.")
            } else { field("Stability", value: $model.project.stability, hint: "Stability from zero to one.") }
            field("Similarity", value: $model.project.similarity, hint: "Voice similarity from zero to one.")
            if !model.project.modelId.hasPrefix("eleven_v4") {
                field("Speed", value: $model.project.speed, hint: "Speech speed from 0.25 to four, with one being normal speed.")
                if model.selectedModel?.data["can_use_style"] as? Bool == true { field("Style", value: $model.project.style, hint: "Style exaggeration from zero to one.") }
                if model.selectedModel?.data["can_use_speaker_boost"] as? Bool == true { Toggle("Speaker Boost", isOn: $model.project.speakerBoost).accessibilityHint("Enhance the similarity to the original speaker.") }
            }
        }
    }
    private func field(_ title: String, value: Binding<Double>, hint: String) -> some View { TextField(title, value: value, format: .number).accessibilityHint(hint) }
    private var dialogue: some View {
        VStack(alignment: .leading, spacing: 10) {
            List(lines, selection: $selectedLine) { Text($0.voiceName + ": " + $0.text.replacingOccurrences(of: "\n", with: " ")).tag($0.id) }.frame(minHeight: 140).accessibilityLabel("Dialogue lines").accessibilityHint("Choose a line to edit.")
            if let index = lines.firstIndex(where: { $0.id == selectedLine }) {
                Picker("Voice", selection: Binding(get: { lines[index].voiceId }, set: { id in lines[index].voiceId = id; lines[index].voiceName = model.voices.first { $0.id == id }?.name ?? id })) { ForEach(CatalogItem.preservingSelection(model.voices, id: lines[index].voiceId)) { Text($0.name).tag($0.id) } }.accessibilityHint("Voice for this dialogue line.")
                KeyboardTextView(text: $lines[index].text, editable: true, accessibilityLabel: "Dialogue text", accessibilityHelp: "Text spoken by this voice. Command+Shift+T inserts a speech tag.", maximumLength: 2000, onTab: { NSApp.keyWindow?.selectNextKeyView(nil) }, onBackTab: { NSApp.keyWindow?.selectPreviousKeyView(nil) }, onReady: { model.dialogueTextView = $0; updateTagLimit() }).frame(height: 160)
                Button("Insert Speech Tag...", action: model.insertSpeechTag).disabled(model.busy).accessibilityHint("Command+Shift+T. Insert before the cursor without replacing the selected passage.")
            }
            HStack {
                Button("Add Line") { let v = model.voices.first; let line = DialogueLine(voiceId: v?.id ?? "", voiceName: v?.name ?? ""); lines.append(line); selectedLine = line.id }.accessibilityHint("Add a new line of dialogue.")
                Button("Remove") { if let i = lines.firstIndex(where: { $0.id == selectedLine }) { lines.remove(at: i); selectedLine = lines.first?.id } }.accessibilityHint("Remove the selected dialogue line.")
                Button("Move Up") { move(-1) }.accessibilityHint("Move the selected line earlier.")
                Button("Move Down") { move(1) }.accessibilityHint("Move the selected line later.")
                Spacer(); Button("OK") { if lines.reduce(0, { $0 + $1.text.utf16.count }) > 2000 || Set(lines.map(\.voiceId)).count > 10 { model.showResult("Dialogue too long", "Use up to 2,000 characters and 10 voices.") } else { model.project.dialogue = lines; model.tool = nil } }.accessibilityHint("Save changes to the dialogue.")
            }
            Text("\(lines.reduce(0) { $0 + $1.text.utf16.count }) / 2,000 characters; up to 10 voices.")
        }
    }
    private func move(_ delta: Int) { guard let i = lines.firstIndex(where: { $0.id == selectedLine }), lines.indices.contains(i + delta) else { return }; lines.swapAt(i, i + delta) }
    private func updateTagLimit() { model.dialogueTagLimit = 2000 - lines.filter { $0.id != selectedLine }.reduce(0) { $0 + $1.text.utf16.count } }
}
