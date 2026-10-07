import AppKit
import Foundation

struct DubbingJob: Codable {
    var schemaVersion = 1
    var inputFile = "", sourceUrl = "", useUrl = false, sourceLanguage = "", targetLanguage = "en"
    var name = "", projectId = "", languageId = "", outputFile = ""
    enum CodingKeys: String, CodingKey {
        case schemaVersion = "SchemaVersion", inputFile = "InputFile", sourceUrl = "SourceUrl", useUrl = "UseUrl", sourceLanguage = "SourceLanguage", targetLanguage = "TargetLanguage", name = "Name", projectId = "ProjectId", languageId = "LanguageId", outputFile = "OutputFile"
    }
    static func validId(_ id: String) -> Bool { id.range(of: #"\A[A-Za-z0-9_-]{1,128}\z"#, options: .regularExpression) != nil }
    func validate(creating: Bool) throws {
        guard schemaVersion == 1, name.count <= 500,
              targetLanguage.range(of: #"\A[a-z]{2,3}(-[A-Za-z0-9]{2,8})*\z"#, options: .regularExpression) != nil,
              sourceLanguage.isEmpty || sourceLanguage.range(of: #"\A[a-z]{2,3}(-[A-Za-z0-9]{2,8})*\z"#, options: .regularExpression) != nil,
              projectId.isEmpty || Self.validId(projectId), languageId.isEmpty || Self.validId(languageId) && !projectId.isEmpty else { throw SpeechError.validation("The dubbing job contains invalid settings or IDs.") }
        if !creating { guard !projectId.isEmpty else { throw SpeechError.validation("Open an existing dubbing job first.") }; return }
        guard projectId.isEmpty else { throw SpeechError.validation("Resume this job instead of submitting it again.") }
        if useUrl {
            guard let url = URL(string: sourceUrl), url.scheme == "https", url.host != nil, url.user == nil, url.password == nil else { throw SpeechError.validation("Enter a public HTTPS source URL without a username or password.") }
        } else {
            let url = URL(fileURLWithPath: inputFile), attrs = try url.resourceValues(forKeys: [.fileSizeKey, .isRegularFileKey])
            guard attrs.isRegularFile == true, let size = attrs.fileSize, size > 0, size <= 500 * 1024 * 1024 else { throw SpeechError.validation("Choose an audio or video file between 1 byte and 500 MB.") }
        }
    }
    static var folder: URL { DraftStore.folder.appendingPathComponent("Dubbing jobs") }
    func save(to folder: URL = Self.folder) throws {
        try validate(creating: false); try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let data = try SpeechFiles.json(self)
        try data.write(to: folder.appendingPathComponent(projectId + ".dubbing.json"), options: .atomic)
        try data.write(to: folder.appendingPathComponent("Last.dubbing.json"), options: .atomic)
    }
    static func read(_ url: URL) throws -> Self {
        guard (try url.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0) <= 1024 * 1024 else { throw SpeechError.validation("The dubbing job file is too large.") }
        let job = try JSONDecoder().decode(Self.self, from: Data(contentsOf: url)); try job.validate(creating: false); return job
    }
    static let languages: [(String, String)] = [("en", "English"), ("es", "Spanish"), ("fr", "French"), ("de", "German"), ("it", "Italian"), ("pt", "Portuguese"), ("nl", "Dutch"), ("ja", "Japanese"), ("zh", "Chinese"), ("ko", "Korean"), ("ar", "Arabic"), ("hi", "Hindi"), ("pl", "Polish"), ("ru", "Russian"), ("uk", "Ukrainian"), ("sv", "Swedish"), ("da", "Danish"), ("fi", "Finnish"), ("el", "Greek"), ("cs", "Czech"), ("tr", "Turkish"), ("id", "Indonesian")]
}

final class DubbingService {
    static let projectPath = "/v1/dubbing/project"
    let api: SpeechService
    init(api: SpeechService) { self.api = api }
    func submit(_ input: DubbingJob) async throws -> DubbingJob {
        try input.validate(creating: true)
        var fields = ["model_id": "dubbing_v2", "target_language": input.targetLanguage]
        if !input.name.isEmpty { fields["reference"] = input.name }
        if !input.sourceLanguage.isEmpty { fields["source_language"] = input.sourceLanguage }
        if input.useUrl { fields["source_url"] = input.sourceUrl }
        let response = try await api.upload(Self.projectPath, fields: fields, files: input.useUrl ? [] : [("file", URL(fileURLWithPath: input.inputFile))])
        let data = try Self.object(response.data)
        var job = input; job.projectId = data["project_id"] as? String ?? ""; job.languageId = (data["language_ids"] as? [String])?.first ?? ""
        guard DubbingJob.validId(job.projectId) else { throw SpeechError.response("The server did not return a usable project ID. Check your ElevenLabs dubbing projects before submitting again; the request may have been accepted.") }
        try job.validate(creating: false); return job
    }
    static func object(_ data: Data) throws -> [String: Any] {
        guard let object = try JSONSerialization.jsonObject(with: data) as? [String: Any] else { throw SpeechError.response("Invalid dubbing response.") }; return object
    }
    static func checkFailure(_ data: [String: Any], title: String) throws {
        if data["status"] as? String == "failed" { throw SpeechError.response(title + " failed: " + ((data["error"] as? [String: Any])?["message"] as? String ?? "Check the project in ElevenLabs for details.")) }
    }
    static func completedAudio(_ data: [String: Any]) throws -> URL? {
        try checkFailure(data, title: "Dubbing")
        let state = data["status"] as? String ?? ""
        if state == "queued" || state == "processing" { return nil }
        if state == "stale" { throw SpeechError.response("This output is out of date after transcript edits. Regenerate it in ElevenLabs before downloading.") }
        guard state == "completed", let value = (data["outputs"] as? [String: Any])?["lossless_audio"] as? String,
              let url = URL(string: value), url.scheme == "https", url.host != nil, url.user == nil, url.password == nil else { throw SpeechError.response("The completed job did not provide a secure audio download, or its status is unrecognised.") }
        return url
    }
    @MainActor func wait(_ input: DubbingJob, folder: URL, progress: (String) -> Void, persist: (DubbingJob) throws -> Void) async throws -> DubbingJob {
        var job = input; try job.validate(creating: false)
        let start = Date(); var last = ""
        while Date().timeIntervalSince(start) < 7200 {
            try Task.checkCancellation()
            let path = Self.projectPath + "/" + job.projectId
            let project = try Self.object(await api.get(path)); try Self.checkFailure(project, title: "Source preparation")
            if job.languageId.isEmpty {
                let list = try Self.object(await api.get(path + "/language?page_size=100"))
                let candidates = (list["languages"] as? [[String: Any]] ?? []).filter { $0["target_language"] as? String == job.targetLanguage }
                guard candidates.count == 1, let id = candidates[0]["language_id"] as? String, DubbingJob.validId(id) else { throw SpeechError.response("Could not identify this job's language target. Check the project in ElevenLabs; no new language was submitted.") }
                job.languageId = id
            }
            let data = try Self.object(await api.get(path + "/language/" + job.languageId)); try persist(job)
            let state = data["status"] as? String ?? ""
            if state != last { progress("Dubbing status: \(state)."); last = state }
            if let url = try Self.completedAudio(data) {
                try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
                let output = SpeechFiles.next(folder: folder, stem: (job.name.isEmpty ? "Dubbed audio" : job.name) + " - " + job.targetLanguage, ext: ".wav")
                job.outputFile = try await DubbingDownload().receive(url, output: output).path; try persist(job); return job
            }
            try await Task.sleep(for: .seconds(5))
        }
        throw SpeechError.response("Stopped waiting after two hours. Your job is saved; resume it later without submitting it again.")
    }
}

final class DubbingDownload: NSObject, URLSessionDataDelegate, @unchecked Sendable {
    private let lock = NSLock()
    private var continuation: CheckedContinuation<URL, Error>?
    private var session: URLSession?
    private var task: URLSessionDataTask?
    private var writer: FileHandle?
    private var partial: URL?
    private var output: URL?
    private var total = 0
    private var failure: Error?
    private var cancelled = false
    static func losslessExtension(_ header: Data) throws -> String {
        if header.count >= 4 && String(decoding: header.prefix(4), as: UTF8.self) == "fLaC" { return ".flac" }
        if header.count >= 12 && String(decoding: header.prefix(4), as: UTF8.self) == "RIFF" && String(decoding: header[8..<12], as: UTF8.self) == "WAVE" { return ".wav" }
        throw SpeechError.response("The server did not return supported lossless audio (WAV or FLAC).")
    }
    func receive(_ url: URL, output: URL) async throws -> URL {
        guard url.scheme == "https", url.user == nil, url.password == nil else { throw SpeechError.validation("A secure audio URL is required.") }
        return try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { (continuation: CheckedContinuation<URL, Error>) in
                lock.lock(); defer { lock.unlock() }
                if cancelled { continuation.resume(throwing: CancellationError()); return }
                let partial = output.appendingPathExtension(UUID().uuidString + ".partial")
                do {
                    guard FileManager.default.createFile(atPath: partial.path, contents: nil) else { throw SpeechError.response("Could not create the audio download.") }
                    writer = try FileHandle(forWritingTo: partial); self.partial = partial; self.output = output; self.continuation = continuation
                    let config = URLSessionConfiguration.ephemeral; config.timeoutIntervalForResource = 600
                    let session = URLSession(configuration: config, delegate: self, delegateQueue: nil); self.session = session
                    // This request deliberately has no account credentials or saved cookies.
                    let task = session.dataTask(with: url); self.task = task; task.resume()
                } catch { try? FileManager.default.removeItem(at: partial); continuation.resume(throwing: error) }
            }
        } onCancel: { self.lock.lock(); self.cancelled = true; let task = self.task; self.lock.unlock(); task?.cancel() }
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(request.url?.scheme == "https" ? request : nil) }
    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse, completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        guard let http = response as? HTTPURLResponse, (200..<300).contains(http.statusCode), response.url?.scheme == "https", response.expectedContentLength <= 1024 * 1024 * 1024 else { failure = SpeechError.response("The audio download failed or exceeded 1 GB."); completionHandler(.cancel); return }
        completionHandler(.allow)
    }
    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive data: Data) {
        do { total += data.count; guard total <= 1024 * 1024 * 1024 else { throw SpeechError.response("The dubbed audio exceeds the 1 GB download limit.") }; try writer?.write(contentsOf: data) }
        catch { failure = error; dataTask.cancel() }
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        lock.lock(); let cancelled = self.cancelled; let continuation = self.continuation; self.continuation = nil; lock.unlock()
        defer { if let partial { try? FileManager.default.removeItem(at: partial) }; session.finishTasksAndInvalidate(); self.session = nil; self.task = nil }
        do {
            try writer?.synchronize(); try writer?.close(); writer = nil
            if cancelled { throw CancellationError() }; if let failure { throw failure }; if let error { throw error }
            guard let partial, let output, total >= 12 else { throw SpeechError.response("The audio download is empty or incomplete.") }
            let reader = try FileHandle(forReadingFrom: partial); let header = try reader.read(upToCount: 12) ?? Data(); try reader.close()
            let ext = try Self.losslessExtension(header)
            let target = output.pathExtension == String(ext.dropFirst()) ? output : SpeechFiles.next(folder: output.deletingLastPathComponent(), stem: output.deletingPathExtension().lastPathComponent, ext: ext)
            try FileManager.default.moveItem(at: partial, to: target); continuation?.resume(returning: target)
        } catch { try? writer?.close(); writer = nil; continuation?.resume(throwing: error) }
    }
}
