import XCTest
@testable import ElevenLabsSpeechGeneratorMac

final class DubbingTests: XCTestCase {
    func testJobRoundTripAndResumeSafety() throws {
        var job = DubbingJob(); job.useUrl = true; job.sourceUrl = "https://example.invalid/source.mp3"; job.targetLanguage = "es"
        try job.validate(creating: true)
        job.projectId = "proj_test"; job.languageId = "lang_test"
        XCTAssertThrowsError(try job.validate(creating: true)); try job.validate(creating: false)
        let saved = try JSONDecoder().decode(DubbingJob.self, from: SpeechFiles.json(job))
        XCTAssertEqual(saved.projectId, job.projectId); XCTAssertEqual(saved.targetLanguage, "es")
        XCTAssertFalse(DubbingJob.validId("../../escape")); XCTAssertFalse(DubbingJob.validId("project\n"))
    }
    func testStatusAndSecureDownloads() throws {
        XCTAssertNil(try DubbingService.completedAudio(["status": "queued"]))
        XCTAssertNil(try DubbingService.completedAudio(["status": "processing"]))
        XCTAssertEqual(try DubbingService.completedAudio(["status": "completed", "outputs": ["lossless_audio": "https://example.invalid/audio.wav"]])?.scheme, "https")
        for state in ["failed", "stale", "unknown"] { XCTAssertThrowsError(try DubbingService.completedAudio(["status": state])) }
        XCTAssertThrowsError(try DubbingService.completedAudio(["status": "completed", "outputs": ["lossless_audio": "http://example.invalid/audio.wav"]]))
        XCTAssertThrowsError(try DubbingService.completedAudio(["status": "completed", "outputs": ["lossless_audio": "https://user:password@example.invalid/audio.wav"]]))
    }
}
