import AppKit
import SwiftUI

struct DubbingView: View {
    @ObservedObject var model: AppModel
    private var locked: Bool { model.busy || !model.dubbingJob.projectId.isEmpty }
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            VStack(alignment: .leading, spacing: 12) {
                Toggle("Use a Public HTTPS URL", isOn: $model.dubbingJob.useUrl).accessibilityHint("Choose an online recording instead of a local file. Nothing is uploaded until you confirm Start Dubbing.")
                if model.dubbingJob.useUrl {
                    TextField("Source URL", text: $model.dubbingJob.sourceUrl).accessibilityHint("A public HTTPS link to the source media, without a username or password.")
                } else {
                    HStack {
                        TextField("Source Audio or Video", text: $model.dubbingJob.inputFile).accessibilityHint("The recording to translate, up to 500 MB. The original file is not changed.")
                        Button("Browse...") {
                            let panel = NSOpenPanel(); if panel.runModal() == .OK, let url = panel.url { model.dubbingJob.inputFile = url.path; if model.dubbingJob.name.isEmpty { model.dubbingJob.name = url.deletingPathExtension().lastPathComponent } }
                        }.accessibilityHint("Select a recording for dubbing.")
                    }
                }
                TextField("Dubbing Job Name", text: $model.dubbingJob.name).accessibilityHint("Optional job and output filename.")
                HStack {
                    TextField("Source Language Code", text: $model.dubbingJob.sourceLanguage).accessibilityHint("Leave blank to detect the source language automatically, or enter a supported language code.")
                    Menu("Choose Source Language") { Button("Automatic Detection") { model.dubbingJob.sourceLanguage = "" }; ForEach(DubbingJob.languages, id: \.0) { code, name in Button(name) { model.dubbingJob.sourceLanguage = code } } }.accessibilityHint("Choose a common source language or automatic detection.")
                }
                HStack {
                    TextField("Target Language Code", text: $model.dubbingJob.targetLanguage).accessibilityHint("Enter a supported translation language code, such as es or fr-CA.")
                    Menu("Choose Target Language") { ForEach(DubbingJob.languages, id: \.0) { code, name in Button(name) { model.dubbingJob.targetLanguage = code } } }.accessibilityHint("Choose a common translation language.")
                }
            }.disabled(locked)
            KeyboardTextView(text: $model.dubbingStatus, editable: false, accessibilityLabel: "Dubbing status", accessibilityHelp: "Progress, saved project information and errors for this job.", onTab: { NSApp.keyWindow?.selectNextKeyView(nil) }, onBackTab: { NSApp.keyWindow?.selectPreviousKeyView(nil) }, onReady: { _ in }).frame(minHeight: 120)
            HStack {
                Button(model.dubbingJob.projectId.isEmpty ? "Start Dubbing" : "Resume Job", action: model.startDubbing).disabled(model.busy).keyboardShortcut(.return, modifiers: .command).accessibilityHint("Command+Enter. Confirm a paid new dubbing project, or resume this saved job without creating another.")
                Button("Open Job...", action: model.openDubbingJob).disabled(model.busy).accessibilityHint("Open a saved dubbing job to check progress or download its output again.")
                Button("New Job") { model.dubbingJob = DubbingJob(); model.dubbingStatus = "Ready." }.disabled(model.busy).accessibilityHint("Start a separate job without deleting the existing saved project.")
            }
        }.onAppear {
            if model.dubbingJob.projectId.isEmpty {
                let last = DubbingJob.folder.appendingPathComponent("Last.dubbing.json")
                if FileManager.default.fileExists(atPath: last.path) { do { model.dubbingJob = try DubbingJob.read(last); model.dubbingStatus = "Saved project: \(model.dubbingJob.projectId). Resume checks the existing job without creating another." } catch { model.dubbingStatus = "Could not restore the previous job: \(error.localizedDescription)" } }
            }
        }
    }
}
