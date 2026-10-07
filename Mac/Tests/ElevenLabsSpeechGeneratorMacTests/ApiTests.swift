import Foundation
import XCTest
@testable import ElevenLabsSpeechGeneratorMac

private final class FixtureProtocol: URLProtocol {
    static var reply: ((URLRequest) throws -> (Int, String, Data))?
    static var calls = 0
    static var declaredLength: Int?
    static func body(_ request: URLRequest) throws -> Data {
        if let data = request.httpBody { return data }
        guard let stream = request.httpBodyStream else { throw SpeechError.response("Fixture request body missing.") }
        stream.open(); defer { stream.close() }; var data = Data(), buffer = [UInt8](repeating: 0, count: 4096)
        while true { let n = stream.read(&buffer, maxLength: buffer.count); if n == 0 { return data }; if n < 0 { throw stream.streamError ?? SpeechError.response("Fixture stream failed.") }; data.append(contentsOf: buffer.prefix(n)); if data.count > 1048576 { throw SpeechError.response("Fixture body too large.") } }
    }
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        do {
            Self.calls += 1
            let (code, type, data) = try Self.reply!(request)
            var headers = ["Content-Type": type, "request-id": "fixture"]
            if let length = Self.declaredLength { headers["Content-Length"] = String(length) }
            let r = HTTPURLResponse(url: request.url!, statusCode: code, httpVersion: "HTTP/1.1", headerFields: headers)!
            client?.urlProtocol(self, didReceive: r, cacheStoragePolicy: .notAllowed)
            client?.urlProtocol(self, didLoad: data); client?.urlProtocolDidFinishLoading(self)
        } catch { client?.urlProtocol(self, didFailWithError: error) }
    }
    override func stopLoading() {}
}

final class ApiTests: XCTestCase {
    private var session: URLSession!
    private var api: SpeechService!
    override func setUp() {
        let c = URLSessionConfiguration.ephemeral; c.protocolClasses = [FixtureProtocol.self]
        session = URLSession(configuration: c, delegate: AuthenticatedRequestDelegate(), delegateQueue: nil)
        api = SpeechService(key: "fixture-not-a-key", session: session)
        FixtureProtocol.calls = 0
        FixtureProtocol.declaredLength = nil
    }
    override func tearDown() { session.invalidateAndCancel(); FixtureProtocol.reply = nil }
    func testSpeechRequest() async throws {
        var p = SpeechProject(); p.voiceId = "voice-a"; p.text = "Hello"
        FixtureProtocol.reply = { r in
            XCTAssertEqual(r.httpMethod, "POST"); XCTAssertEqual(r.url?.path, "/v1/text-to-speech/voice-a")
            XCTAssertEqual(r.value(forHTTPHeaderField: "xi-api-key"), "fixture-not-a-key")
            let d = try JSONSerialization.jsonObject(with: FixtureProtocol.body(r)) as! [String: Any]
            XCTAssertEqual(d["text"] as? String, "Hello"); XCTAssertEqual((d["voice_settings"] as? [String: Any])?.count, 2)
            return (200, "audio/mpeg", Data("ID3fixture".utf8))
        }
        let r = try await api.generate(p, model: nil); XCTAssertEqual(r.requestId, "fixture"); XCTAssertEqual(r.data, Data("ID3fixture".utf8))
    }
    func testDialogueRequest() async throws {
        var p = SpeechProject(); p.mode = .dialogue; p.dialogue = [DialogueLine(voiceId: "voice-a", voiceName: "A", text: "Hi")]
        FixtureProtocol.reply = { r in
            XCTAssertEqual(r.url?.path, "/v1/text-to-dialogue")
            let d = try JSONSerialization.jsonObject(with: FixtureProtocol.body(r)) as! [String: Any]
            XCTAssertNotNil(d["inputs"]); XCTAssertNotNil(d["settings"]); XCTAssertNil(d["voice_settings"])
            return (200, "audio/mpeg", Data([1, 2]))
        }
        _ = try await api.generate(p, model: nil)
    }
    func testDesignRequest() async throws {
        var p = SpeechProject(); p.mode = .voiceDesign; p.modelId = "eleven_ttv_v3"; p.text = "A warm British adult narrator."
        FixtureProtocol.reply = { r in
            XCTAssertEqual(r.url?.path, "/v1/text-to-voice/design")
            let d = try JSONSerialization.jsonObject(with: FixtureProtocol.body(r)) as! [String: Any]; XCTAssertEqual(d["auto_generate_text"] as? Bool, true)
            return (200, "application/json", Data("{\"previews\":[]}".utf8))
        }
        let result = try await api.generate(p, model: nil); XCTAssertTrue(result.contentType.contains("json"))
    }
    func testRemixRequest() async throws {
        var p = SpeechProject(); p.mode = .voiceRemix; p.voiceId = "voice-a"; p.text = "Make this voice warmer."
        FixtureProtocol.reply = { r in
            XCTAssertEqual(r.url?.path, "/v1/text-to-voice/voice-a/remix")
            let d = try JSONSerialization.jsonObject(with: FixtureProtocol.body(r)) as! [String: Any]
            XCTAssertEqual(d["voice_description"] as? String, p.text); XCTAssertNil(d["model_id"])
            return (200, "application/json", Data("{\"previews\":[]}".utf8))
        }
        _ = try await api.generate(p, model: nil)
    }
    func testUploadModes() async throws {
        let file = FileManager.default.temporaryDirectory.appendingPathComponent("SpeechFixture-\(UUID()).wav")
        try Data(repeating: 0, count: 80).write(to: file); defer { try? FileManager.default.removeItem(at: file) }
        var p = SpeechProject(); p.inputFile = file.path; p.voiceId = "voice-a"
        for (mode, path) in [(SpeechMode.transcription, "/v1/speech-to-text"), (.voiceChanger, "/v1/speech-to-speech/voice-a"), (.voiceIsolation, "/v1/audio-isolation"), (.forcedAlignment, "/v1/forced-alignment")] {
            p.mode = mode
            FixtureProtocol.reply = { r in
                XCTAssertEqual(r.url?.path, path); XCTAssertEqual(r.httpMethod, "POST")
                XCTAssertTrue(r.value(forHTTPHeaderField: "Content-Type")?.hasPrefix("multipart/form-data; boundary=Speech") == true)
                return (200, mode == .transcription ? "application/json" : "audio/mpeg", Data("fixture".utf8))
            }
            _ = try await api.generate(p, model: nil)
        }
        XCTAssertEqual(FixtureProtocol.calls, 4)
    }
    func testLosslessAudioHeaders() throws {
        let flac = Data("fLaCfixture!".utf8)
        XCTAssertEqual(try DubbingDownload.losslessExtension(flac), ".flac")
        XCTAssertEqual(try SpeechService.isolationExtension(Data("ID3fixtureaudio".utf8)), ".mp3")
        XCTAssertThrowsError(try SpeechService.isolatedAudio(SpeechResult(data: Data("unknown format".utf8), requestId: "", contentType: "audio/unknown")))
        XCTAssertThrowsError(try DubbingDownload.losslessExtension(Data("not an audio".utf8)))
    }
    func testDubbingSubmission() async throws {
        var job = DubbingJob(); job.useUrl = true; job.sourceUrl = "https://example.invalid/recording.mp3"; job.targetLanguage = "fr"
        FixtureProtocol.reply = { r in
            XCTAssertEqual(r.url?.path, "/v1/dubbing/project"); XCTAssertEqual(r.httpMethod, "POST")
            return (201, "application/json", Data("{\"project_id\":\"proj_test\",\"language_ids\":[\"lang_test\"]}".utf8))
        }
        let created = try await DubbingService(api: api).submit(job)
        XCTAssertEqual(created.projectId, "proj_test"); XCTAssertEqual(created.languageId, "lang_test"); XCTAssertEqual(FixtureProtocol.calls, 1)
        do { _ = try await DubbingService(api: api).submit(created); XCTFail("Existing job resubmitted") } catch { XCTAssertEqual(FixtureProtocol.calls, 1) }
    }
    @MainActor func testDubbingResumeNeverPosts() async {
        var job = DubbingJob(); job.projectId = "proj_test"; job.languageId = "lang_test"
        FixtureProtocol.reply = { r in
            XCTAssertEqual(r.httpMethod, "GET")
            let json = r.url?.path.hasSuffix("lang_test") == true ? "{\"status\":\"failed\",\"error\":{\"message\":\"fixture failure\"}}" : "{\"status\":\"ready\"}"
            return (200, "application/json", Data(json.utf8))
        }
        do { _ = try await DubbingService(api: api).wait(job, folder: FileManager.default.temporaryDirectory, progress: { _ in }, persist: { _ in }); XCTFail("Failure ignored") }
        catch { XCTAssertTrue(error.localizedDescription.contains("fixture failure")) }
        XCTAssertEqual(FixtureProtocol.calls, 2)
    }
    func testErrorNotRetried() async {
        FixtureProtocol.reply = { _ in (500, "application/json", Data("{\"detail\":{\"message\":\"Fixture failure\"}}".utf8)) }
        do { _ = try await api.post("/v1/text-to-dialogue", body: [:]); XCTFail("Error accepted") } catch { XCTAssertTrue(error.localizedDescription.contains("HTTP 500")) }
        XCTAssertEqual(FixtureProtocol.calls, 1)
    }
    func testDeleteMethod() async throws {
        FixtureProtocol.reply = { r in XCTAssertEqual(r.httpMethod, "DELETE"); return (204, "application/json", Data()) }
        try await api.delete("/v1/voices/disposable")
    }
    func testNoRedirectDelegation() {
        let delegate = AuthenticatedRequestDelegate(), url = URL(string: "https://example.invalid")!
        var called = false
        delegate.urlSession(session, task: session.dataTask(with: url), willPerformHTTPRedirection: HTTPURLResponse(url: url, statusCode: 302, httpVersion: nil, headerFields: nil)!, newRequest: URLRequest(url: url)) { request in called = true; XCTAssertNil(request) }
        XCTAssertTrue(called)
    }
    func testBoundedResponseRejectsOversizedChunks() async {
        FixtureProtocol.reply = { _ in (200, "application/octet-stream", Data(repeating: 0, count: 17)) }
        do {
            _ = try await BoundedResponse(limit: 16).receive(URLRequest(url: URL(string: "https://example.invalid")!), upload: nil, configuration: session.configuration)
            XCTFail("Oversized response accepted")
        } catch { XCTAssertTrue(error.localizedDescription.contains("too large")) }
    }
    func testBoundedResponseRejectsDeclaredOversize() async {
        FixtureProtocol.declaredLength = 100
        FixtureProtocol.reply = { _ in (200, "application/octet-stream", Data([0])) }
        do {
            _ = try await BoundedResponse(limit: 16).receive(URLRequest(url: URL(string: "https://example.invalid")!), upload: nil, configuration: session.configuration)
            XCTFail("Oversized declared response accepted")
        } catch { XCTAssertTrue(error.localizedDescription.contains("too large")) }
    }
    func testBoundedResponseCancellation() async {
        FixtureProtocol.reply = { _ in (200, "application/octet-stream", Data([0])) }
        let receiver = BoundedResponse(limit: 16), configuration = session.configuration
        let task = Task { try await receiver.receive(URLRequest(url: URL(string: "https://example.invalid")!), upload: nil, configuration: configuration) }
        task.cancel()
        do { _ = try await task.value; XCTFail("Cancelled response accepted") }
        catch { XCTAssertTrue(error is CancellationError || (error as NSError).code == NSURLErrorCancelled) }
    }
    func testBoundedResponseDoesNotRedirectCredentials() {
        let receiver = BoundedResponse(limit: 16), url = URL(string: "https://example.invalid")!
        var called = false
        receiver.urlSession(session, task: session.dataTask(with: url), willPerformHTTPRedirection: HTTPURLResponse(url: url, statusCode: 302, httpVersion: nil, headerFields: nil)!, newRequest: URLRequest(url: url)) { request in called = true; XCTAssertNil(request) }
        XCTAssertTrue(called)
    }
    func testReusableGenerationDetails() throws {
        var p = SpeechProject(); p.text = "Reused"
        let object = try JSONSerialization.jsonObject(with: SpeechFiles.json(p))
        let data = try JSONSerialization.data(withJSONObject: ["project": object, "request_id": "fixture"])
        XCTAssertEqual(try SpeechFiles.project(data).text, "Reused")
        XCTAssertThrowsError(try SpeechFiles.project(Data("{\"text\":\"Transcript\"}".utf8)))
    }
}
