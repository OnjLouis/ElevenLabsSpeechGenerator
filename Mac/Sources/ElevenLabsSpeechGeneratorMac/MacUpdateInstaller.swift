import AppKit
import Darwin
import Foundation

@MainActor enum MacUpdateInstaller {
    private static let appName = "ElevenLabs Speech Generator.app"
    private static let bundleID = "me.onj.ElevenLabsSpeechGeneratorMac"
    private static let teamID = "83NN3HS237"
    private static let executableName = "ElevenLabsSpeechGeneratorMac"
    private static let transactionPrefix = "ElevenLabsSpeechUpdate-"
    private static let applications = URL(fileURLWithPath: "/Applications", isDirectory: true)
    private static var signatureRequirement: String {
        "anchor apple generic and certificate leaf[subject.OU] = \"\(teamID)\" and identifier \"\(bundleID)\""
    }

    static func start(_ release: PublishedMacRelease) async throws {
        let target = applications.appendingPathComponent(appName, isDirectory: true)
        guard Bundle.main.bundleURL.standardizedFileURL == target.standardizedFileURL else {
            throw SpeechError.validation("Move the app to Applications before installing an update.")
        }

        let originalID = try directoryIdentity(target)
        let stagingParent = FileManager.default.temporaryDirectory.resolvingSymlinksInPath()
        let staging = stagingParent.appendingPathComponent(transactionPrefix + UUID().uuidString, isDirectory: true)
        _ = try directoryIdentity(stagingParent)
        guard mkdir(staging.path, 0o700) == 0 else {
            throw SpeechError.response("Could not create a new update folder. No files were replaced.")
        }
        let stagingID = try directoryIdentity(staging)
        do {
            var request = URLRequest(url: release.downloadURL, cachePolicy: .reloadIgnoringLocalCacheData)
            request.timeoutInterval = 120
            request.setValue("ElevenLabs Speech Generator updater", forHTTPHeaderField: "User-Agent")
            let (download, response) = try await URLSession.shared.download(for: request)
            guard (response as? HTTPURLResponse)?.statusCode == 200 else {
                throw SpeechError.response("The update download did not complete successfully.")
            }
            guard try directoryIdentity(staging) == stagingID, try directoryIdentity(target) == originalID else {
                throw SpeechError.response("The app or update folder changed during the download.")
            }
            let zip = staging.appendingPathComponent("update.zip")
            try FileManager.default.moveItem(at: download, to: zip)
            let app = try prepare(zip: zip, release: release, staging: staging)

            let helper = staging.appendingPathComponent("install-update.zsh")
            let script = try installScript(stagedApp: app, target: target, staging: staging,
                                          stagingParent: stagingParent, version: release.version, expectedTargetID: originalID,
                                          expectedStagingID: stagingID)
            try Data(script.utf8).write(to: helper, options: .withoutOverwriting)
            let process = Process()
            process.executableURL = URL(fileURLWithPath: "/bin/zsh")
            process.arguments = ["-f", helper.path]
            try process.run()
            NSApp.terminate(nil)
        } catch {
            // An incomplete download or extraction can leave a partially known tree.
            if (try? directoryIdentity(staging)) == stagingID, rmdir(staging.path) == 0 {
                throw error
            }
            throw SpeechError.response("\(error.localizedDescription) The update folder was left untouched at \(staging.path).")
        }
    }

    static func prepare(zip: URL, release: PublishedMacRelease, staging: URL) throws -> URL {
        try validateArchive(zip)

        let extracted = staging.appendingPathComponent("extract", isDirectory: true)
        _ = try directoryIdentity(staging)
        guard mkdir(extracted.path, 0o700) == 0 else {
            throw SpeechError.response("Could not create a fresh update extraction folder.")
        }
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
        try run("/usr/bin/codesign", ["--verify", "--deep", "--strict", "--test-requirement", "=" + signatureRequirement, app.path])
        try run("/usr/sbin/spctl", ["--assess", "--type", "execute", app.path])
        try run("/usr/bin/xcrun", ["stapler", "validate", app.path])
        #if arch(x86_64)
        let architecture = "x86_64"
        #else
        let architecture = "arm64"
        #endif
        try run("/usr/bin/lipo", ["-verify_arch", architecture,
                                    app.appendingPathComponent("Contents/MacOS/" + executableName).path])
    }

    private static func directoryIdentity(_ url: URL) throws -> String {
        let path = url.path
        guard path.hasPrefix("/"), path == url.resolvingSymlinksInPath().path,
              let fields = try? FileManager.default.attributesOfItem(atPath: path),
              fields[.type] as? FileAttributeType == .typeDirectory,
              let device = fields[.systemNumber] as? NSNumber,
              let inode = fields[.systemFileNumber] as? NSNumber else {
            throw SpeechError.response("Unexpected or redirected update path: \(path)")
        }
        return "\(device):\(inode)"
    }

    static func installScript(stagedApp: URL, target: URL, staging: URL,
                              installDirectory: URL? = nil, stagingParent: URL, version: String,
                              expectedTargetID: String? = nil, expectedStagingID: String? = nil,
                              waitForPID: Int32 = ProcessInfo.processInfo.processIdentifier,
                              reopen: Bool = true, notifyOnError: Bool = true) throws -> String {
        let installDirectory = installDirectory ?? applications
        let extract = staging.appendingPathComponent("extract", isDirectory: true)
        let name = staging.lastPathComponent
        guard target.path == installDirectory.appendingPathComponent(appName).path,
              staging.deletingLastPathComponent().path == stagingParent.path,
              stagedApp.path == extract.appendingPathComponent(appName).path,
              name.hasPrefix(transactionPrefix), UUID(uuidString: String(name.dropFirst(transactionPrefix.count))) != nil,
              waitForPID > 0 else {
            throw SpeechError.response("The update paths did not match the expected app and transaction folder.")
        }
        let targetID = try directoryIdentity(target)
        let stagingID = try directoryIdentity(staging)
        guard (expectedTargetID ?? targetID) == targetID, (expectedStagingID ?? stagingID) == stagingID else {
            throw SpeechError.response("The app or update folder changed before installation.")
        }
        let installID = try directoryIdentity(installDirectory)
        let stageParentID = try directoryIdentity(stagingParent)
        let extractID = try directoryIdentity(extract)
        let stagedID = try directoryIdentity(stagedApp)
        let transaction = installDirectory.appendingPathComponent("." + name)
        return #"""
        #!/bin/zsh
        set -eu
        umask 077
        target=\#(quote(target.path))
        install_dir=\#(quote(installDirectory.path))
        install_id=\#(quote(installID))
        target_id=\#(quote(targetID))
        staging=\#(quote(staging.path))
        staging_id=\#(quote(stagingID))
        stage_parent=\#(quote(stagingParent.path))
        stage_parent_id=\#(quote(stageParentID))
        extract="$staging/extract"
        extract_id=\#(quote(extractID))
        staged=\#(quote(stagedApp.path))
        staged_id=\#(quote(stagedID))
        transaction=\#(quote(transaction.path))
        candidate="$transaction/replacement.app"
        backup="$transaction/previous.app"
        failed="$transaction/failed.app"
        zip="$staging/update.zip"
        helper="$staging/install-update.zsh"
        log="$staging/install.log"
        requirement=\#(quote("=" + signatureRequirement))
        bundle_id=\#(quote(bundleID))
        version=\#(quote(version))
        executable_name=\#(quote(executableName))

        directory_id() {
          [[ -d "$1" && ! -L "$1" && "$(cd -P -- "$1" && pwd -P)" == "$1" ]] || return 1
          /usr/bin/stat -f '%d:%i' "$1"
        }
        file_id() {
          [[ -f "$1" && ! -L "$1" ]] && directory_id "${1:h}" >/dev/null || return 1
          [[ "$(/usr/bin/stat -f '%l' "$1")" == 1 ]] || return 1
          /usr/bin/stat -f '%d:%i' "$1"
        }
        owned_dir() { [[ -n "$2" && "$(directory_id "$1")" == "$2" ]]; }
        absent() { [[ ! -e "$1" && ! -L "$1" ]]; }
        signed_bundle() {
          /usr/bin/codesign --verify --deep --strict --test-requirement "$requirement" "$1"
        }
        read_info() {
          file_id "$1/Contents/Info.plist" >/dev/null &&
            /usr/libexec/PlistBuddy -c "Print :$2" "$1/Contents/Info.plist"
        }
        signed_update() {
          [[ "$(read_info "$1" CFBundleShortVersionString)" == "$version" ]] && signed_bundle "$1"
        }
        notify() {
          print -r -- "$1"
          if \#(notifyOnError ? "true" : "false"); then
            /usr/bin/osascript - "$1" <<'APPLESCRIPT' || true
        on run argv
          display alert "Software Update" message (item 1 of argv) buttons {"OK"}
        end run
        APPLESCRIPT
          fi
        }
        refuse() { notify "$1"; exit 1; }
        valid_staging() {
          owned_dir "$stage_parent" "$stage_parent_id" && owned_dir "$staging" "$staging_id"
        }
        valid_staging || refuse "The update folder changed. No files were replaced: $staging"
        [[ "$0" == "$helper" ]] || refuse "The updater was not started from its expected folder."
        zip_id=$(file_id "$zip") && helper_id=$(file_id "$helper") ||
          refuse "The update files changed. No files were replaced: $staging"
        absent "$log" || refuse "An existing update log was left untouched: $log"
        setopt noclobber
        exec >"$log" 2>&1
        log_id=$(file_id "$log")

        for i in {1..100}; do
          if ! kill -0 \#(waitForPID) 2>/dev/null; then break; fi
          /bin/sleep 0.2
        done
        if kill -0 \#(waitForPID) 2>/dev/null; then
          refuse "The previous app did not exit. No files were replaced. Update log: $log"
        fi

        owned_dir "$install_dir" "$install_id" && owned_dir "$target" "$target_id" &&
          [[ "$(read_info "$target" CFBundleIdentifier)" == "$bundle_id" ]] ||
          refuse "The installed app changed or is not the expected app. Nothing was replaced. Update log: $log"
        valid_staging && owned_dir "$extract" "$extract_id" && owned_dir "$staged" "$staged_id" ||
          refuse "The staged app changed. Nothing was replaced. Update log: $log"
        for entry in "$staged"/**/*(DN); do
          [[ ! -L "$entry" && ( -d "$entry" || -f "$entry" ) ]] ||
            refuse "The staged app contains an unexpected link or file type. Update log: $log"
        done
        signed_update "$staged" || refuse "Could not verify the staged app. Update log: $log"
        absent "$transaction" && /bin/mkdir -m 700 -- "$transaction" ||
          refuse "An existing or unavailable transaction folder was left untouched: $transaction"
        transaction_id=$(directory_id "$transaction")

        valid_transaction() {
          owned_dir "$install_dir" "$install_id" && owned_dir "$transaction" "$transaction_id"
        }
        move_bundle() {
          valid_transaction && owned_dir "$1" "$3" && absent "$2" &&
            /bin/mv -hn -- "$1" "$2" && absent "$1" && owned_dir "$2" "$3"
        }
        replacement_id=''
        rollback() {
          if valid_transaction && owned_dir "$backup" "$target_id"; then
            # Only our replacement may be moved out of the live install path.
            if (absent "$target" || move_bundle "$target" "$failed" "$replacement_id") &&
               move_bundle "$backup" "$target" "$target_id"; then
              \#(reopen ? "/usr/bin/open \"$target\" || true" : ":")
              refuse "The update failed and the previous app was restored. Update log: $log. Retained files: $transaction"
            fi
          fi
          refuse "The update could not finish. Files were left in place. Check $target and $transaction (previous.app is the backup if present). Update log: $log"
        }
        valid_transaction && valid_staging && owned_dir "$staged" "$staged_id" && absent "$candidate" &&
          /usr/bin/ditto "$staged" "$candidate" || rollback
        replacement_id=$(directory_id "$candidate") && signed_update "$candidate" || rollback
        move_bundle "$target" "$backup" "$target_id" || rollback
        move_bundle "$candidate" "$target" "$replacement_id" || rollback
        owned_dir "$target" "$replacement_id" && signed_update "$target" || rollback
        \#(reopen ? "/usr/bin/open \"$target\" || rollback" : ":")

        unlink_owned() {
          [[ -n "$2" && "$(file_id "$1")" == "$2" ]] && /bin/rm -- "$1"
        }
        prune_bundle() {
          local bundle=$1 expected=$2 entry relative i
          local -a files=() ids=() dirs=()
          owned_dir "$bundle" "$expected" || return 1
          signed_bundle "$bundle" || return 1
          # Unknown content is preserved. Only clean the layout this updater ships.
          for entry in "$bundle"/**/*(DN); do
            relative=${entry#"$bundle"/}
            case "$relative" in
              Contents|Contents/MacOS|Contents/Resources|Contents/_CodeSignature)
                directory_id "$entry" >/dev/null || return 1
                dirs=("$entry" "${dirs[@]}") ;;
              Contents/Info.plist|Contents/CodeResources|Contents/MacOS/"$executable_name"|Contents/Resources/Manual.html|Contents/Resources/LICENSE.txt|Contents/_CodeSignature/CodeResources)
                i=$(file_id "$entry") || return 1
                files+=("$entry"); ids+=("$i") ;;
              *) return 1 ;;
            esac
          done
          for ((i=1; i<=${#files}; i++)); do
            owned_dir "$bundle" "$expected" && unlink_owned "$files[i]" "$ids[i]" || return 1
          done
          for entry in "${dirs[@]}" "$bundle"; do
            directory_id "$entry" >/dev/null && /bin/rmdir -- "$entry" || return 1
          done
        }
        retained=0
        if ! valid_transaction || ! owned_dir "$target" "$replacement_id" ||
           ! prune_bundle "$backup" "$target_id" || ! /bin/rmdir -- "$transaction"; then
          print -r -- "Retained transaction files: $transaction"
          retained=1
        fi
        if ! valid_staging || ! owned_dir "$extract" "$extract_id" ||
           ! prune_bundle "$staged" "$staged_id" || ! /bin/rmdir -- "$extract" ||
           ! unlink_owned "$zip" "$zip_id" || ! unlink_owned "$helper" "$helper_id"; then
          print -r -- "Retained staged files: $staging"
          retained=1
        fi
        contents=("$staging"/*(DN))
        if (( retained == 0 && ${#contents} == 1 )) &&
           valid_staging && unlink_owned "$log" "$log_id" && /bin/rmdir -- "$staging"; then
          print 'Update installed successfully.'
        else
          notify "The update installed successfully. Some files were left in place to avoid deleting unexpected data. Check $staging and $transaction."
        fi
        """#
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
