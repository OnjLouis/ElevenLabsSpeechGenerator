import AppKit
import Foundation

@MainActor enum MacUpdateInstaller {
    private static let appName = "ElevenLabs Speech Generator.app"
    private static let bundleID = "me.onj.ElevenLabsSpeechGeneratorMac"
    private static let teamID = "83NN3HS237"

    static func start(_ release: PublishedMacRelease) async throws {
        let target = URL(fileURLWithPath: "/Applications/\(appName)")
        guard Bundle.main.bundleURL.standardizedFileURL == target.standardizedFileURL else {
            throw SpeechError.validation("Move the app to Applications before installing an update.")
        }

        let staging = FileManager.default.temporaryDirectory
            .appendingPathComponent("ElevenLabsSpeechUpdate-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: staging, withIntermediateDirectories: true)
        var helperStarted = false
        defer { if !helperStarted { try? FileManager.default.removeItem(at: staging) } }

        var request = URLRequest(url: release.downloadURL, cachePolicy: .reloadIgnoringLocalCacheData)
        request.timeoutInterval = 120
        request.setValue("ElevenLabs Speech Generator updater", forHTTPHeaderField: "User-Agent")
        let (download, response) = try await URLSession.shared.download(for: request)
        guard (response as? HTTPURLResponse)?.statusCode == 200 else {
            throw SpeechError.response("The update download did not complete successfully.")
        }
        let zip = staging.appendingPathComponent(release.assetName)
        try FileManager.default.moveItem(at: download, to: zip)
        let app = try prepare(zip: zip, release: release, staging: staging)

        let helper = staging.appendingPathComponent("install-update.zsh")
        try installScript(stagedApp: app, target: target, staging: staging).write(to: helper, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: helper.path)
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/zsh")
        process.arguments = [helper.path]
        try process.run()
        helperStarted = true
        NSApp.terminate(nil)
    }

    static func prepare(zip: URL, release: PublishedMacRelease, staging: URL) throws -> URL {
        try validateArchive(zip)

        let extracted = staging.appendingPathComponent("extract", isDirectory: true)
        try FileManager.default.createDirectory(at: extracted, withIntermediateDirectories: true)
        try run("/usr/bin/ditto", ["-x", "-k", zip.path, extracted.path])
        let app = extracted.appendingPathComponent(appName, isDirectory: true)
        try verify(app, version: release.version)
        return app
    }

    private static func validateArchive(_ zip: URL) throws {
        let listing = try output("/usr/bin/unzip", ["-Z", "-1", zip.path])
        let details = try output("/usr/bin/zipinfo", ["-l", zip.path])
        let root = appName + "/"
        let entries = listing.split(whereSeparator: \.isNewline).map(String.init)
        guard !entries.isEmpty, entries.allSatisfy({ entry in
            entry.hasPrefix(root) && !entry.contains("\\") &&
                !entry.split(separator: "/").contains(where: { $0 == "." || $0 == ".." })
        }), !details.split(whereSeparator: \.isNewline).contains(where: { line in
            line.hasPrefix("l") && line.contains(root)
        }) else {
            throw SpeechError.response("The update ZIP has unexpected contents.")
        }
    }

    private static func verify(_ app: URL, version: String) throws {
        guard let bundle = Bundle(url: app), bundle.bundleIdentifier == bundleID,
              bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String == version else {
            throw SpeechError.response("The update contains the wrong app or version.")
        }
        let requirement = "anchor apple generic and certificate leaf[subject.OU] = \"\(teamID)\" and identifier \"\(bundleID)\""
        try run("/usr/bin/codesign", ["--verify", "--deep", "--strict", "--test-requirement", "=" + requirement, app.path])
        try run("/usr/sbin/spctl", ["--assess", "--type", "execute", app.path])
        try run("/usr/bin/xcrun", ["stapler", "validate", app.path])
        #if arch(x86_64)
        let architecture = "x86_64"
        #else
        let architecture = "arm64"
        #endif
        try run("/usr/bin/lipo", ["-verify_arch", architecture,
                                    app.appendingPathComponent("Contents/MacOS/ElevenLabsSpeechGeneratorMac").path])
    }

    static func installScript(stagedApp: URL, target: URL, staging: URL,
                              waitForPID: Int32 = ProcessInfo.processInfo.processIdentifier,
                              reopen: Bool = true, notifyOnError: Bool = true,
                              logPath: String? = nil) -> String {
        let replacement = target.path + ".new-\(UUID().uuidString)"
        let backup = target.path + ".old-\(UUID().uuidString)"
        let log = logPath ?? FileManager.default.homeDirectoryForCurrentUser
            .appendingPathComponent("Library/Logs/ElevenLabsSpeechGeneratorUpdate.log").path
        return """
        #!/bin/zsh
        set -eu
        exec >\(quote(log)) 2>&1
        target=\(quote(target.path))
        candidate=\(quote(replacement))
        backup=\(quote(backup))
        staged=\(quote(stagedApp.path))
        staging=\(quote(staging.path))
        for i in {1..100}; do
          if ! kill -0 \(waitForPID) 2>/dev/null; then break; fi
          sleep 0.2
        done
        if kill -0 \(waitForPID) 2>/dev/null; then
          print 'The previous app did not exit. Installation cancelled.'
          \(notifyOnError ? "/usr/bin/osascript -e 'display alert \"Update Could Not Finish\" message \"The app did not close in time, so nothing was replaced. Please try again.\" as critical' || true" : ":")
          exit 1
        fi
        rollback() {
          if [[ -d "$backup" ]]; then
            [[ -e "$target" ]] && /bin/rm -rf -- "$target"
            /bin/mv -- "$backup" "$target"
            \(reopen ? "/usr/bin/open \"$target\" || true" : ":")
          fi
          [[ -e "$candidate" ]] && /bin/rm -rf -- "$candidate"
          print 'Update failed; the previous app was restored.'
          \(notifyOnError ? "/usr/bin/osascript -e 'display alert \"Update Failed\" message \"The previous app was restored. Please try again or use Version History to install manually.\" as critical' || true" : ":")
        }
        trap rollback ERR
        /usr/bin/ditto "$staged" "$candidate"
        /bin/mv -- "$target" "$backup"
        /bin/mv -- "$candidate" "$target"
        /usr/bin/codesign --verify --deep --strict "$target"
        \(reopen ? "/usr/bin/open \"$target\"" : ":")
        trap - ERR
        /bin/rm -rf -- "$backup" "$staging"
        print 'Update installed successfully.'
        """
    }

    private static func quote(_ value: String) -> String {
        "'" + value.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }

    private static func run(_ executable: String, _ arguments: [String]) throws {
        _ = try output(executable, arguments)
    }

    private static func output(_ executable: String, _ arguments: [String]) throws -> String {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: executable)
        process.arguments = arguments
        let pipe = Pipe()
        process.standardOutput = pipe
        process.standardError = pipe
        try process.run()
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else {
            throw SpeechError.response("Update verification failed (\(URL(fileURLWithPath: executable).lastPathComponent), exit \(process.terminationStatus)).")
        }
        return String(decoding: data, as: UTF8.self)
    }
}
