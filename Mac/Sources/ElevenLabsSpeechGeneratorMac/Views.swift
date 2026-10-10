import AppKit
import SwiftUI

struct MainView: View {
    @ObservedObject var model: AppModel
    @FocusState private var filenameFocus: Bool
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Picker("Mode", selection: Binding(get: { model.project.mode }, set: model.switchMode)) { ForEach(SpeechMode.allCases) { Text($0.title).tag($0) } }.accessibilityHint("Choose the speech, transcription, or voice-processing task.")
                if model.project.mode.needsVoice {
                    Picker("Voice Group", selection: Binding(get: { model.preferences.voiceGroup ?? "All voices" }, set: { model.preferences.voiceGroup = $0; model.preferences.save() })) { ForEach(VoiceGroups.names, id: \.self) { Text($0).tag($0) } }.accessibilityHint("Filter voices by ownership or category without changing the current voice.")
                    Picker("Voice", selection: $model.project.voiceId) { Text("Choose a voice").tag(""); ForEach(model.availableVoices) { Text($0.name).tag($0.id) } }.accessibilityHint("Choose the voice to use.")
                    Button("Preview Voice", action: model.previewSelectedVoice).disabled(!model.canPreviewSelectedVoice)
                        .keyboardShortcut("p", modifiers: [.command, .option]).accessibilityHint("Command+Option+P. Play this voice's existing preview without generating speech or spending credits.")
                }
                Button("Refresh Voices", action: model.loadCatalog).accessibilityHint("Reload voices and models from your account.")
            }.disabled(model.busy)
            HStack {
                Picker("Model", selection: $model.project.modelId) { ForEach(model.availableModels) { Text($0.name).tag($0.id) } }.accessibilityHint("Choose the speech model.")
                if model.project.mode.choosesFormat {
                    Picker("Output Format", selection: $model.project.outputFormat) { ForEach(SpeechProject.formats, id: \.self) { Text($0).tag($0) } }.accessibilityHint("Choose MP3 or a PCM WAV sample rate. Some formats require a higher subscription tier.")
                    VariationsField(count: $model.project.variations)
                } else if model.project.mode == .voiceIsolation { Text("Output: service-supplied audio") }
            }.disabled(model.busy)
            nativeText($model.balance, name: "Credit balance", hint: "Command+B. Remaining balance and credit usage.", editable: false, height: 70, ready: { model.balanceView = $0 })
            nativeText($model.status, name: "Status log", hint: "Command+T. Generation and error log.", editable: false, height: 90, ready: { model.statusView = $0 })
            if model.project.mode == .dialogue {
                Button("Edit Dialogue...") { model.openTool(.dialogue) }.keyboardShortcut("p").accessibilityHint("Command+P. Add, edit and reorder dialogue lines and their voices.")
                Text("\(model.project.dialogue.count) dialogue lines; \(model.project.dialogue.reduce(0) { $0 + $1.text.utf16.count }) / 2,000 characters.")
            } else if model.project.mode != .voiceChanger && model.project.mode != .voiceIsolation {
                nativeText($model.project.text, name: model.project.mode == .transcription ? "Transcript" : model.project.mode == .forcedAlignment ? "Matching transcript" : model.project.mode == .voiceDesign ? "Voice description" : model.project.mode == .voiceRemix ? "Voice changes" : "Speech text", hint: "Command+Shift+M. Type or paste the text.", editable: true, height: 160, maximum: model.project.mode == .transcription ? nil : model.textLimit, ready: { model.promptView = $0 })
            }
            HStack { Text("Base filename:"); TextField("Base filename", text: $model.project.filename).focused($filenameFocus).accessibilityHint("Name for the generated files; leave blank to use the start of your text."); Text(model.project.mode == .transcription ? "\(model.project.text.utf16.count) characters" : "\(model.project.text.utf16.count) / \(model.textLimit) characters") }
            if model.project.mode.needsAudio { HStack { TextField("Input audio file", text: $model.project.inputFile).disabled(true).accessibilityHint("The audio file selected for processing."); Button("Choose Audio...", action: model.chooseAudio).accessibilityHint("Choose an existing audio or video file.") } }
            options
            HStack {
                Button("Generate", action: model.generate).keyboardShortcut(.return, modifiers: .command).disabled(model.busy).accessibilityHint("Command+Enter. Confirm and start generation.")
                Button("Cancel", action: model.cancel).disabled(!model.busy).accessibilityHint("Cancel the active request; completed files are kept.")
                Button("Play", action: model.play).accessibilityHint("Play the selected generated file.")
                Button("Stop", action: model.stop).accessibilityHint("Stop audio playback.")
                Button("Save Selected...", action: model.saveSelected).accessibilityHint("Save another copy of the selected file.")
                if model.project.mode.previewsVoice { Button("Add Preview Voice...", action: model.addDesign).accessibilityHint("Save the selected preview as a new voice in your account. The original voice is unchanged.") }
                Text(model.busy ? "Working" : "Ready").accessibilityLabel(model.busy ? "Working" : "Ready")
            }
            List(model.outputs, selection: $model.selectedOutput) { Text($0.url.lastPathComponent).tag($0.id) }.frame(minHeight: 90, maxHeight: 140).accessibilityLabel("Generated files and voice previews").accessibilityHint("Choose a file to play or save. Return plays it; Escape stops playback.")
                .onKeyPress(.return) { model.play(); return .handled }
                .onKeyPress(.escape) { model.stop(); return .handled }
        }.padding(16).onAppear { model.launch() }
            .onExitCommand { model.stop(); if model.busy { model.cancel() } }
            .sheet(item: $model.tool) { kind in ToolView(model: model, kind: kind).frame(minWidth: 680, minHeight: 450) }
    }
    private func nativeText(_ text: Binding<String>, name: String, hint: String, editable: Bool, height: CGFloat, maximum: Int? = nil, ready: @escaping (TabAwareTextView) -> Void) -> some View {
        KeyboardTextView(text: text, editable: editable, accessibilityLabel: name, accessibilityHelp: hint, maximumLength: maximum,
                         onTab: { NSApp.keyWindow?.selectNextKeyView(nil) }, onBackTab: { NSApp.keyWindow?.selectPreviousKeyView(nil) }, onReady: ready).frame(minHeight: height, maxHeight: height + 80)
    }
    @ViewBuilder private var options: some View {
        if model.project.mode == .transcription {
            HStack { Toggle("Identify Speakers", isOn: $model.project.diarize).accessibilityHint("Label different speakers in the transcript details."); Toggle("Include Audio Events", isOn: $model.project.audioEvents).accessibilityHint("Include sounds such as laughter in the transcript."); TextField("Language code", text: $model.project.language).frame(width: 100).accessibilityHint("Leave blank to detect the language automatically.") }
        } else if model.project.mode == .textToSpeech || model.project.mode == .voiceChanger || model.project.mode == .dialogue {
            HStack { Button("Voice Settings...") { model.openTool(.voiceSettings) }.accessibilityHint("Adjust the settings supported by the selected model."); if model.project.mode != .voiceChanger { TextField("Language code", text: $model.project.language).frame(width: 100).accessibilityHint("Optional language code; leave blank for automatic selection.") }; if model.project.mode == .voiceChanger { Toggle("Remove Background Noise", isOn: $model.project.removeNoise).accessibilityHint("Clean background noise before converting the voice.") } }
        }
    }
}

private struct VariationsField: View {
    @Binding var count: Int

    var body: some View {
        HStack {
            Text("Variations")
            TextField("Variations", value: $count, format: .number)
                .frame(width: 90)
                .accessibilityLabel("Number of variations")
                .accessibilityHint("Number of separate generations, from 1 to 10.")
        }
    }
}

struct SettingsView: View {
    @State private var devices: [CatalogItem] = []
    @ObservedObject var model: AppModel
    @State private var key = ""
    var body: some View {
        PreferenceTabs(panes: [
            .init("General") {
            Form {
                HStack { TextField("Default Output Folder", text: $model.preferences.outputFolder).accessibilityHint("Choose where generated audio and transcripts are saved."); Button("Browse...") { let p = NSOpenPanel(); p.canChooseDirectories = true; p.canChooseFiles = false; if p.runModal() == .OK, let url = p.url { model.preferences.outputFolder = url.path; model.preferences.save() } }.accessibilityHint("Select the default output folder.") }
                Toggle("Save Generation Details", isOn: $model.preferences.includeDetails).accessibilityHint("Save reusable JSON details in the Details subfolder.")
                Toggle("Play a Completion Sound", isOn: $model.preferences.completionSound).accessibilityHint("Play a sound when a batch finishes.")
                Toggle("Allow long speech text (split into parts)", isOn: Binding(get: { model.preferences.allowLongSpeech == true }, set: { model.preferences.allowLongSpeech = $0 }))
                    .accessibilityHint("Generate Text to speech of up to 500,000 characters in sentence-aware parts, assembled into one WAV per variation. Each new part spends credits.")
            }.padding()
            },
            .init("API key") {
            Form {
                SecureField("API Key", text: $key).accessibilityHint("Your ElevenLabs API key, stored securely in Keychain.")
                Button("Save API Key") { do { try KeychainStore.write(key); model.refreshBalance() } catch { model.showResult("Could not save key", error.localizedDescription) } }.accessibilityHint("Save the key to this Mac user's Keychain.")
                Button("Test API Key") { model.testKey(key) }.disabled(model.busy).accessibilityHint("Check models, voices and credit-balance access without generating audio.")
                Link("Get an ElevenLabs API Key", destination: URL(string: "https://elevenlabs.io/app/settings/api-keys")!)
            }.padding()
            },
            .init("Updates") {
            Form {
                Toggle("Check for Updates at Startup", isOn: $model.preferences.autoUpdateOnLaunch).accessibilityHint("Check for a new version when the app opens.")
                Toggle("Install Verified Updates Silently", isOn: $model.preferences.installUpdatesSilently).accessibilityHint("Download and install verified updates without asking first.")
            }.padding()
            }
            , .init("Audio") {
                Form {
                    Picker("Playback Device", selection: $model.preferences.playbackDevice) {
                        Text("System default").tag("")
                        ForEach(devices) { Text($0.name).tag($0.id) }
                    }.accessibilityHint("Choose the output for this app's audio playback.")
                        .onChange(of: model.preferences.playbackDevice) { _, _ in model.stop(); model.preferences.save() }
                    Picker("Default Output Format", selection: Binding(get: { model.preferences.resolvedOutputFormat }, set: { model.preferences.defaultOutputFormat = $0; model.project.outputFormat = $0 })) {
                        ForEach(SpeechProject.formats, id: \.self) { Text($0).tag($0) }
                    }.accessibilityHint("Choose the audio format for new projects. The main window can override it for a project.")
                    Toggle("Play new generations automatically in sequence", isOn: Binding(get: { model.preferences.autoPlayGenerations == true }, set: { model.preferences.autoPlayGenerations = $0; model.stop(); model.preferences.save() }))
                        .accessibilityHint("Off by default. Play only the new audio from a successfully completed batch, one file at a time. Stop ends the queue.")
                }.padding(18)
            }
        ], selection: $model.settingsTab).padding().onAppear { key = (try? KeychainStore.read()) ?? ""; devices = AudioDevices.list() }
            .onChange(of: model.preferences) { _, value in value.save() }
            .onExitCommand { NSApp.keyWindow?.close() }
    }
}
