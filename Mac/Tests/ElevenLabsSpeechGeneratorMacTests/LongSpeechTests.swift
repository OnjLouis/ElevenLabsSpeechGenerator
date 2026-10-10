import Foundation
import AVFoundation
import XCTest
@testable import ElevenLabsSpeechGeneratorMac

final class LongSpeechTests: XCTestCase {
    @MainActor func testMp3RequestsRemainMp3AndAssembleAsWave() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("MP3Speech-\(UUID())")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        defer { try? FileManager.default.removeItem(at: folder) }
        var request = SpeechProject(); request.voiceId = "test"; request.text = "First sentence. Second sentence."; request.outputFormat = "mp3_44100_128"
        request.variations = 1; var calls = 0
        let outputs = try await LongSpeech.generate(request, model: nil, folder: folder, stem: "MP3 fixture", limit: 20, progress: { _ in }, generate: { part, _ in
            XCTAssertEqual(part.outputFormat, request.outputFormat); calls += 1
            return SpeechResult(data: Data(base64Encoded: Self.tone)!, requestId: "mp3\(calls)", contentType: "audio/mpeg")
        })
        let wave = try AVAudioFile(forReading: outputs[0])
        XCTAssertEqual(wave.processingFormat.sampleRate, 44100)
        XCTAssertGreaterThan(Double(wave.length) / 44100, 0.1 * Double(calls))
    }
    // A generated 100 ms sine tone; no third-party recording is included.
    private static let tone = "//sQxAAABHQTVVSQgDCmCa83GiACAAGtOUAAAVk6PVBQCAYJAfB8HwfKAgCAYRB8H9QIOxOH+INwBJP2wGA4HA4AAAAAACiJKpkUZAjpAkgWo/eFAfATG/AilC+oGhL8JA0qCgAYMAD/+xLEAoPFWB0gHeAAKJsD40GvaEzMCQC8QASGAOB4Z+72pmMDlmHEESYMAH5gQgYGBSBMYF4DxZq0lYeYIIZk+cS0YX4opqvUomp+KKYYQMxz3pn0pmjxm45iQriU4Ju+mjDQ4w4dMdP/+xDEA4PFBB8YDfsiQK2EYoG/bEiDPoMwrxvjUM4cNOsbQwngbTXMAgZuqHV+awbJpaHP1/QkQYmImbHBu8aYlw7RwV9cG/wO4YmoT5wjIZ4jGZn5mTwYsLMHjFPgH/q+ijChEw8cMf/7EsQDA8UAHxgN+yJAtAQkArwABaOzPYYwphyTTb5rNLgb8wlwcDSRAQRvJnb4a4TNZ4N/r+h2RCAKYD4C5gLAuGDEG0aUTCpoEj3mICFWYPoF5gKggmByBiYGoGINABi9vBSNASIBAv/7EMQCgAT8O0gZo4AAkIWgw55gABgvCwoyCJ4VwfJlrDFjJTb9yIpZxwdjebyZAERFmYVBX5Y8LMADgBUMY7RNTxE+AWi8k+E2Mp+nUNVupIgEJCQNQaPfWd4iTEFNRTMuMTAwqqqq"
    func testBoundariesPreserveText() throws {
        let texts = ["First paragraph.\r\nAnother sentence with many words. Last sentence.  ", "One [speaking very softly] word. Another sentence.", "Emoji \u{1f600} and accents e\u{301}. More words. End.", "\u{4e00}\u{4e8c}\u{4e09}\u{3002}\u{56db}\u{4e94}\u{516d}\u{3002}\u{4e03}\u{516b}\u{4e5d}\u{3002}"]
        for text in texts {
            let limit = text.contains("[") ? 32 : 25, parts = try LongSpeech.split(text, limit: limit)
            XCTAssertEqual(parts.joined(), text); XCTAssertTrue(parts.allSatisfy { $0.utf16.count <= limit && !$0.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty })
            var valid: Set<Int> = [text.utf16.count], offset = 0
            for character in text { valid.insert(offset); offset += String(character).utf16.count }
            offset = 0
            for part in parts { offset += part.utf16.count; XCTAssertTrue(valid.contains(offset)); XCTAssertFalse(part.hasSuffix("\r")) }
        }
        let sentence = String(repeating: "a", count: 9978) + ". Next sentence has more than twenty characters."
        XCTAssertEqual(try LongSpeech.split(sentence, limit: 10000)[0].utf16.count, 9980)
        XCTAssertThrowsError(try LongSpeech.split(String(repeating: "a", count: 10001), limit: 10000))
        XCTAssertThrowsError(try LongSpeech.split("One [" + String(repeating: "a", count: 30) + "] word", limit: 20))
        XCTAssertThrowsError(try LongSpeech.split(String(repeating: "x", count: LongSpeech.maximumCharacters + 1), limit: 10000))
    }
    func testContextAndModelLimits() {
        XCTAssertEqual(LongSpeech.modelLimit(nil, id: "eleven_v3"), 5000)
        let previous = (0..<4).map { LongSpeech.Part(hash: "", requestId: "id\($0)", created: Date()) }, chunks = ["One.", "Two.", "Three."]
        let context = LongSpeech.context(model: "eleven_v4", chunks: chunks, index: 1, previous: previous)
        XCTAssertEqual(context["previous_request_ids"] as? [String], ["id1", "id2", "id3"]); XCTAssertEqual(context["next_text"] as? String, "Three.")
        XCTAssertTrue(LongSpeech.context(model: "eleven_v3", chunks: chunks, index: 1, previous: previous).isEmpty)
        XCTAssertNotNil(LongSpeech.context(model: "eleven_v4", chunks: chunks, index: 1, previous: [LongSpeech.Part(hash: "", requestId: "expired", created: Date(timeIntervalSinceNow: -10800))])["previous_text"])
    }
    @MainActor func testInterruptedBatchResumesWithoutRegeneratingParts() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("LongSpeech-\(UUID())")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true); defer { try? FileManager.default.removeItem(at: folder) }
        var p = SpeechProject(); p.voiceId = "test"; p.text = "First sentence. Second sentence. Third sentence."; p.variations = 2; p.outputFormat = "pcm_44100"
        let chunks = try LongSpeech.split(p.text, limit: 20)
        var calls = 0, text: [String] = []
        do {
            _ = try await LongSpeech.generate(p, model: nil, folder: folder, stem: "Fixture", limit: 20, progress: { _ in }, generate: { part, _ in
                calls += 1
                if calls == 2 { throw SpeechError.response("Interrupted fixture") }
                text.append(part.text); XCTAssertEqual(part.outputFormat, "pcm_44100")
                return SpeechResult(data: Data([1, 0, 1, 0]), requestId: "part1", contentType: "audio/pcm")
            })
            XCTFail("Failure was hidden")
        } catch { XCTAssertTrue(error.localizedDescription.contains("Interrupted fixture")) }
        calls = 1
        let outputs = try await LongSpeech.generate(p, model: nil, folder: folder, stem: "Fixture", limit: 20, progress: { _ in }, generate: { part, _ in
            calls += 1; text.append(part.text)
            return SpeechResult(data: Data([UInt8(calls), 0, UInt8(calls), 0]), requestId: "part\(calls)", contentType: "audio/pcm")
        })
        XCTAssertEqual(outputs.count, 2); XCTAssertEqual(text.joined(), p.text + p.text)
        let wave = try AVAudioFile(forReading: outputs[0], commonFormat: .pcmFormatInt16, interleaved: true)
        let buffer = AVAudioPCMBuffer(pcmFormat: wave.processingFormat, frameCapacity: AVAudioFrameCount(wave.length))!
        try wave.read(into: buffer)
        let samples = Array(UnsafeBufferPointer(start: buffer.int16ChannelData![0], count: Int(buffer.frameLength)))
        XCTAssertEqual(samples, (1...chunks.count).flatMap { [Int16($0), Int16($0)] })
        let jobs = folder.appendingPathComponent("Details/Unfinished speech")
        XCTAssertTrue(try FileManager.default.contentsOfDirectory(atPath: jobs.path).isEmpty)
    }
    func testAssemblyRejectsInvalidPartsAndKeepsOutput() throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("LongSpeechAssembly-\(UUID())")
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true); defer { try? FileManager.default.removeItem(at: folder) }
        let part = folder.appendingPathComponent("part.wav"), output = folder.appendingPathComponent("output.wav")
        try Data("bad audio".utf8).write(to: part); try Data("keep existing".utf8).write(to: output)
        XCTAssertThrowsError(try LongSpeech.assemble([part], output: output))
        XCTAssertEqual(try String(contentsOf: output, encoding: .utf8), "keep existing")
        XCTAssertFalse(try FileManager.default.contentsOfDirectory(atPath: folder.path).contains { $0.contains(".partial") })
    }
}
