import Foundation

struct SpeechResult { var data: Data; var requestId: String; var contentType: String }
final class AuthenticatedRequestDelegate: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
}
final class BoundedResponse: NSObject, URLSessionDataDelegate, @unchecked Sendable {
    private let lock = NSLock()
    private let limit: Int
    private var continuation: CheckedContinuation<(Data, URLResponse), Error>?
    private var session: URLSession?
    private var task: URLSessionTask?
    private var cancelled = false
    private var tooLarge = false
    private var data = Data()
    private var response: URLResponse?
    private var lastUploadPercent = -1
    private let uploadProgress: (@Sendable (String) -> Void)?
    init(limit: Int, uploadProgress: (@Sendable (String) -> Void)? = nil) { self.limit = limit; self.uploadProgress = uploadProgress }
    func urlSession(_ session: URLSession, task: URLSessionTask, didSendBodyData bytesSent: Int64, totalBytesSent: Int64, totalBytesExpectedToSend: Int64) {
        guard totalBytesExpectedToSend > 0 else { return }
        let percent = Int(min(100, totalBytesSent * 100 / totalBytesExpectedToSend))
        if percent / 5 != lastUploadPercent / 5 || percent == 100 {
            lastUploadPercent = percent
            uploadProgress?(percent == 100 ? "Upload sent. Waiting for ElevenLabs to create the voice..." : "Sending samples: \(percent)% of upload data.")
        }
    }
    func receive(_ request: URLRequest, upload: URL?, configuration: URLSessionConfiguration) async throws -> (Data, URLResponse) {
        try await withTaskCancellationHandler {
            try await withCheckedThrowingContinuation { continuation in
                lock.lock()
                if cancelled { lock.unlock(); continuation.resume(throwing: CancellationError()); return }
                self.continuation = continuation
                let session = URLSession(configuration: configuration, delegate: self, delegateQueue: nil)
                self.session = session
                let task: URLSessionTask = upload.map { session.uploadTask(with: request, fromFile: $0) } ?? session.dataTask(with: request)
                self.task = task
                lock.unlock()
                task.resume()
            }
        } onCancel: {
            self.lock.lock(); self.cancelled = true; let task = self.task; self.lock.unlock(); task?.cancel()
        }
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive response: URLResponse, completionHandler: @escaping (URLSession.ResponseDisposition) -> Void) {
        self.response = response
        if response.expectedContentLength > limit { tooLarge = true; completionHandler(.cancel) }
        else { completionHandler(.allow) }
    }
    func urlSession(_ session: URLSession, dataTask: URLSessionDataTask, didReceive chunk: Data) {
        guard chunk.count <= limit - data.count else { tooLarge = true; dataTask.cancel(); return }
        data.append(chunk)
    }
    func urlSession(_ session: URLSession, task: URLSessionTask, didCompleteWithError error: Error?) {
        lock.lock(); let continuation = self.continuation; self.continuation = nil; self.task = nil; self.session = nil; let cancelled = self.cancelled; lock.unlock()
        session.finishTasksAndInvalidate()
        if tooLarge { continuation?.resume(throwing: SpeechError.response("The response is too large.")) }
        else if cancelled { continuation?.resume(throwing: CancellationError()) }
        else if let error { continuation?.resume(throwing: error) }
        else if let response { continuation?.resume(returning: (data, response)) }
        else { continuation?.resume(throwing: SpeechError.response("No response was received.")) }
    }
}
final class SpeechService {
    private let key: String
    private let configuration: URLSessionConfiguration
    private let origin: URL
    init(key: String, session: URLSession? = nil, origin: URL = URL(string: "https://api.elevenlabs.io")!) { self.key = key; self.configuration = session?.configuration ?? .ephemeral; self.configuration.timeoutIntervalForResource = 600; self.origin = origin }
    func get(_ path: String) async throws -> Data { try await send("GET", path: path).data }
    func post(_ path: String, body: [String: Any]) async throws -> Data { try await send("POST", path: path, body: JSONSerialization.data(withJSONObject: body)).data }
    func delete(_ path: String) async throws { _ = try await send("DELETE", path: path) }
    func generate(_ p: SpeechProject, model: CatalogItem?, context: [String: Any] = [:]) async throws -> SpeechResult {
        let suffix = "?output_format=\(p.outputFormat)"
        if p.mode == .voiceIsolation { return try await upload("/v1/audio-isolation", fields: ["file_format": "other"], files: [("audio", URL(fileURLWithPath: p.inputFile))]) }
        if p.mode == .forcedAlignment { return try await upload("/v1/forced-alignment", fields: ["text": p.text], files: [("file", URL(fileURLWithPath: p.inputFile))]) }
        if p.mode.needsAudio {
            var fields = ["model_id": p.modelId]
            if p.mode == .transcription { fields["diarize"] = p.diarize ? "true" : "false"; fields["tag_audio_events"] = p.audioEvents ? "true" : "false"; fields["timestamps_granularity"] = "word"; if !p.language.isEmpty { fields["language_code"] = p.language } }
            else { fields["voice_settings"] = String(data: try JSONSerialization.data(withJSONObject: p.voiceSettings(model: model)), encoding: .utf8); fields["remove_background_noise"] = p.removeNoise ? "true" : "false" }
            return try await upload(p.mode == .transcription ? "/v1/speech-to-text" : "/v1/speech-to-speech/\(escape(p.voiceId))\(suffix)", fields: fields, files: [(p.mode == .transcription ? "file" : "audio", URL(fileURLWithPath: p.inputFile))])
        }
        let path = p.mode == .dialogue ? "/v1/text-to-dialogue" : p.mode == .voiceDesign ? "/v1/text-to-voice/design" : p.mode == .voiceRemix ? "/v1/text-to-voice/\(escape(p.voiceId))/remix" : "/v1/text-to-speech/\(escape(p.voiceId))"
        var body = p.body(model: model)
        if p.mode == .textToSpeech { for (key, value) in context { body[key] = value } }
        return try await send("POST", path: path + suffix, body: JSONSerialization.data(withJSONObject: body))
    }
    func upload(_ path: String, fields: [String: String], files: [(String, URL)], progress: (@Sendable (String) -> Void)? = nil) async throws -> SpeechResult {
        progress?("Preparing voice samples for upload...")
        let boundary = "Speech\(UUID().uuidString)", temp = FileManager.default.temporaryDirectory.appendingPathComponent("SpeechUpload-\(UUID().uuidString)")
        guard FileManager.default.createFile(atPath: temp.path, contents: nil) else { throw SpeechError.response("Could not stage the upload.") }
        defer { try? FileManager.default.removeItem(at: temp) }
        let writer = try FileHandle(forWritingTo: temp)
        do {
            func write(_ s: String) throws { try writer.write(contentsOf: Data(s.utf8)) }
            for (name, value) in fields { try write("--\(boundary)\r\nContent-Disposition: form-data; name=\"\(name)\"\r\n\r\n\(value)\r\n") }
            for (name, url) in files {
                let size = try url.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0
                guard size > 0 && size <= 500 * 1024 * 1024 else { throw SpeechError.validation("Select a file smaller than 500 MB.") }
                let filename = url.lastPathComponent.replacingOccurrences(of: "\"", with: "_").replacingOccurrences(of: "\r", with: "_").replacingOccurrences(of: "\n", with: "_")
                try write("--\(boundary)\r\nContent-Disposition: form-data; name=\"\(name)\"; filename=\"\(filename)\"\r\nContent-Type: application/octet-stream\r\n\r\n")
                let reader = try FileHandle(forReadingFrom: url)
                do { while let chunk = try reader.read(upToCount: 65536), !chunk.isEmpty { try Task.checkCancellation(); try writer.write(contentsOf: chunk) }; try reader.close() }
                catch { try? reader.close(); throw error }
                try write("\r\n")
            }
            try write("--\(boundary)--\r\n"); try writer.close()
        } catch { try? writer.close(); throw error }
        progress?("Sending voice samples...")
        return try await send("POST", path: path, upload: temp, contentType: "multipart/form-data; boundary=\(boundary)", progress: progress)
    }
    private func send(_ method: String, path: String, body: Data? = nil, upload: URL? = nil, contentType: String = "application/json", progress: (@Sendable (String) -> Void)? = nil) async throws -> SpeechResult {
        guard !key.isEmpty, !key.contains("\r"), !key.contains("\n") else { throw SpeechError.validation("Enter a valid API key in Settings first.") }
        var r = URLRequest(url: URL(string: path, relativeTo: origin)!.absoluteURL); r.httpMethod = method; r.timeoutInterval = 600
        r.setValue(key, forHTTPHeaderField: "xi-api-key"); r.setValue("ElevenLabs Speech Generator/1.1.0", forHTTPHeaderField: "User-Agent")
        if body != nil || upload != nil { r.setValue(contentType, forHTTPHeaderField: "Content-Type") }; r.httpBody = body
        let result = try await BoundedResponse(limit: 256 * 1024 * 1024, uploadProgress: progress).receive(r, upload: upload, configuration: configuration)
        try Task.checkCancellation()
        guard let response = result.1 as? HTTPURLResponse else { throw SpeechError.response("No response was received.") }
        guard (200..<300).contains(response.statusCode) else {
            let json = (try? JSONSerialization.jsonObject(with: result.0)) as? [String: Any]
            let detail = (json?["detail"] as? [String: Any])?["message"] as? String ?? json?["detail"] as? String ?? "The service rejected this request."
            throw SpeechError.response("ElevenLabs returned HTTP \(response.statusCode): \(detail)")
        }
        return SpeechResult(data: result.0, requestId: response.value(forHTTPHeaderField: "request-id") ?? "", contentType: response.value(forHTTPHeaderField: "Content-Type") ?? "")
    }
    static func isolatedAudio(_ result: SpeechResult) throws -> Data {
        guard result.data.count >= 12 else { throw SpeechError.response("The isolation service returned incomplete audio.") }
        _ = try isolationExtension(result.data)
        return result.data
    }
    static func isolationExtension(_ data: Data) throws -> String {
        let header = [UInt8](data.prefix(12))
        if header.count >= 3 && (String(decoding: header.prefix(3), as: UTF8.self) == "ID3" || header[0] == 255 && header[1] & 0xe0 == 0xe0 && header[1] & 6 == 2 && header[1] & 0x18 != 8 && header[2] & 0xf0 != 0xf0) { return ".mp3" }
        return try DubbingDownload.losslessExtension(data.prefix(12))
    }
    static func audio(_ result: SpeechResult, format: String, channels: Int) throws -> Data {
        guard !result.data.isEmpty, !result.contentType.contains("json") else { throw SpeechError.response("The service did not return audio.") }
        guard format.hasPrefix("pcm_"), let rate = Int(format.dropFirst(4)) else { return result.data }
        guard result.data.count % (channels * 2) == 0, result.data.count <= Int(UInt32.max) - 36 else { throw SpeechError.response("Incomplete PCM audio.") }
        var h = Data()
        func ascii(_ s: String) { h.append(Data(s.utf8)) }
        func word<T: FixedWidthInteger>(_ n: T) { var v = n.littleEndian; withUnsafeBytes(of: &v) { h.append(contentsOf: $0) } }
        ascii("RIFF"); word(UInt32(result.data.count + 36)); ascii("WAVEfmt "); word(UInt32(16)); word(UInt16(1)); word(UInt16(channels)); word(UInt32(rate)); word(UInt32(rate * channels * 2)); word(UInt16(channels * 2)); word(UInt16(16)); ascii("data"); word(UInt32(result.data.count)); h.append(result.data); return h
    }
    private func escape(_ s: String) -> String { s.addingPercentEncoding(withAllowedCharacters: .alphanumerics) ?? "" }
}
