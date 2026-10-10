import Foundation
import CryptoKit
import Darwin
import AVFoundation

enum LongSpeech {
    static let maximumCharacters = 500_000
    private static let maximumWaveBytes = 1024 * 1024 * 1024
    struct Part: Codable { var hash: String; var requestId: String; var created: Date }
    struct Recording: Codable { var parts: [Part] = []; var outputName: String?; var outputHash: String? }
    struct Checkpoint: Codable { var version = 1; var fingerprint: String; var recordings: [Recording] }

    static func modelLimit(_ model: CatalogItem?, id: String) -> Int {
        if let limit = model?.data["maximum_text_length_per_request"] as? Int, limit > 0 { return limit }
        return id == "eleven_v3" ? 5000 : ["eleven_flash_v2_5", "eleven_turbo_v2_5"].contains(id) ? 40000 : id == "eleven_flash_v2" ? 30000 : 10000
    }
    static func sampleRate(_ format: String) -> Int { Int(format.split(separator: "_")[1]) ?? 44100 }
    static func split(_ text: String, limit: Int) throws -> [String] {
        guard !text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty, text.utf16.count <= maximumCharacters, limit >= 2 else { throw SpeechError.validation("Enter speech text of up to 500,000 characters.") }
        let units = Array(text.utf16), ns = text as NSString
        var boundaries: Set<Int> = [units.count], offset = 0
        for character in text { boundaries.insert(offset); offset += String(character).utf16.count }
        func whitespace(_ unit: UInt16) -> Bool { UnicodeScalar(Int(unit)).map { CharacterSet.whitespacesAndNewlines.contains($0) } ?? false }
        let sentenceMarks: Set<UInt16> = [46, 33, 63, 0x3002, 0xff01, 0xff1f], closing: Set<UInt16> = [34, 39, 0x201d, 0x2019, 41]
        var lastNonblank = units.count - 1
        while lastNonblank >= 0 && whitespace(units[lastNonblank]) { lastNonblank -= 1 }
        var start = 0, result: [String] = []
        while units.count - start > limit {
            var word = -1, sentence = -1, paragraph = -1, brackets = 0, lastContent: UInt16 = 0
            for i in start..<(start + limit) {
                let c = units[i], boundary = i + 1
                if !whitespace(c) && !closing.contains(c) { lastContent = c }
                if c == 91 { brackets += 1 } else if c == 93 && brackets > 0 { brackets -= 1 }
                guard brackets == 0, boundaries.contains(boundary), boundary <= lastNonblank else { continue }
                if whitespace(c) {
                    word = boundary
                    if boundary - start >= limit / 2 {
                        if sentenceMarks.contains(lastContent) { sentence = boundary }
                        if c == 10 || c == 13 { paragraph = boundary }
                    }
                } else if [UInt16(0x3002), 0xff01, 0xff1f].contains(c) { word = boundary; if boundary - start >= limit / 2 { sentence = boundary } }
            }
            let cut = paragraph > start ? paragraph : sentence > start ? sentence : word
            guard cut > start else { throw SpeechError.validation("A word, speech tag, or blank passage exceeds this model's request limit. Shorten that passage before generating.") }
            let part = ns.substring(with: NSRange(location: start, length: cut - start))
            guard !part.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw SpeechError.validation("Remove the overlong blank passage before generating.") }
            result.append(part); start = cut
        }
        let tail = ns.substring(from: start)
        guard !tail.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else { throw SpeechError.validation("The text ends with an overlong blank passage. Remove the extra whitespace before generating.") }
        result.append(tail); return result
    }
    static func context(model: String, chunks: [String], index: Int, previous: [Part]) -> [String: Any] {
        guard model != "eleven_v3" else { return [:] }
        var result: [String: Any] = [:]
        if index + 1 < chunks.count { result["next_text"] = String(chunks[index + 1].prefix(1000)) }
        if index > 0 {
            let ids = previous.suffix(3).filter { !$0.requestId.isEmpty && Date().timeIntervalSince($0.created) < 7200 }.map(\.requestId)
            if !ids.isEmpty { result["previous_request_ids"] = ids } else { result["previous_text"] = String(chunks[index - 1].suffix(1000)) }
        }
        return result
    }
    static func generate(_ request: SpeechProject, model: CatalogItem?, folder: URL, stem: String, limit: Int,
        progress: @escaping @MainActor (String) -> Void,
        generate: (SpeechProject, [String: Any]) async throws -> SpeechResult) async throws -> [URL] {
        guard request.mode == .textToSpeech else { throw SpeechError.validation("Long-text splitting is available only for Text to speech.") }
        try request.validate(limit: maximumCharacters)
        let chunks = try split(request.text, limit: limit)
        var fingerprintData = try SpeechFiles.json(request)
        fingerprintData.append(Data("\n\(limit)\n".utf8))
        fingerprintData.append(try JSONSerialization.data(withJSONObject: request.voiceSettings(model: model), options: [.sortedKeys]))
        let fingerprint = SHA256.hash(data: fingerprintData).map { String(format: "%02x", $0) }.joined()
        let job = folder.appendingPathComponent("Details/Unfinished speech/\(fingerprint)"), manifest = job.appendingPathComponent("Progress.json"), fm = FileManager.default
        let jobs = job.deletingLastPathComponent()
        try fm.createDirectory(at: jobs, withIntermediateDirectories: true)
        // Lock the retained parent directory: unlinking a lock file could admit a second job during cleanup.
        let descriptor = open(jobs.path, O_RDONLY | O_NOFOLLOW)
        guard descriptor >= 0 else { throw SpeechError.response("Could not reserve the unfinished speech job.") }
        defer { close(descriptor) }
        guard flock(descriptor, LOCK_EX | LOCK_NB) == 0 else { throw SpeechError.response("This speech job is already running in another app window.") }
        try fm.createDirectory(at: job, withIntermediateDirectories: true)
        guard try job.resourceValues(forKeys: [.isSymbolicLinkKey]).isSymbolicLink != true else { throw SpeechError.validation("The unfinished speech folder must not redirect to another location.") }
        var checkpoint: Checkpoint
        if fm.fileExists(atPath: manifest.path) {
            guard try manifest.resourceValues(forKeys: [.fileSizeKey]).fileSize ?? 0 <= 2 * 1024 * 1024 else { throw SpeechError.validation("The unfinished speech record is too large.") }
            checkpoint = try JSONDecoder().decode(Checkpoint.self, from: Data(contentsOf: manifest))
            guard checkpoint.version == 1, checkpoint.fingerprint == fingerprint, checkpoint.recordings.count == request.variations, checkpoint.recordings.allSatisfy({ $0.parts.count <= chunks.count }) else { throw SpeechError.validation("The unfinished speech record does not match this request. No audio was sent.") }
        } else {
            guard try fm.contentsOfDirectory(at: job, includingPropertiesForKeys: nil).isEmpty else { throw SpeechError.validation("Unrecorded speech parts need inspection before this job can resume.") }
            checkpoint = Checkpoint(fingerprint: fingerprint, recordings: Array(repeating: Recording(), count: request.variations)); try save(checkpoint, at: manifest)
        }
        func partURL(_ v: Int, _ i: Int) -> URL { job.appendingPathComponent("v\(v + 1)-part\(i + 1)" + (request.outputFormat.hasPrefix("pcm_") ? ".wav" : ".mp3")) }
        for (v, recording) in checkpoint.recordings.enumerated() {
            for (i, part) in recording.parts.enumerated() {
                let path = partURL(v, i); try validate(path, hash: part.hash)
                let decoded = try AVAudioFile(forReading: path, commonFormat: .pcmFormatInt16, interleaved: true)
                guard decoded.length > 0, (1...2).contains(Int(decoded.processingFormat.channelCount)) else { throw SpeechError.response("A retained speech part could not be decoded as complete PCM audio. No request was sent.") }
            }
            if let hash = recording.outputHash {
                guard let name = recording.outputName, URL(fileURLWithPath: name).lastPathComponent == name, name.hasSuffix(".wav") else { throw SpeechError.validation("Invalid completed speech filename.") }
                try validate(folder.appendingPathComponent(name), hash: hash)
            }
        }
        var results: [URL] = []
        for v in 0..<request.variations {
            try Task.checkCancellation()
            if checkpoint.recordings[v].outputHash != nil, let name = checkpoint.recordings[v].outputName { await progress("Keeping completed variation \(v + 1)."); results.append(folder.appendingPathComponent(name)); continue }
            for i in checkpoint.recordings[v].parts.count..<chunks.count {
                try Task.checkCancellation(); await progress("Generating variation \(v + 1), part \(i + 1) of \(chunks.count).")
                let path = partURL(v, i)
                guard !fm.fileExists(atPath: path.path) else { throw SpeechError.validation("A completed but unrecorded speech part needs inspection. It was not overwritten or generated again.") }
                var part = request; part.text = chunks[i]; part.variations = 1
                let result = try await generate(part, context(model: request.modelId, chunks: chunks, index: i, previous: checkpoint.recordings[v].parts))
                let audio = try SpeechService.audio(result, format: part.outputFormat, channels: 1)
                try audio.write(to: path, options: .withoutOverwriting)
                checkpoint.recordings[v].parts.append(Part(hash: try hash(path), requestId: result.requestId, created: Date())); try save(checkpoint, at: manifest)
                let decoded = try AVAudioFile(forReading: path, commonFormat: .pcmFormatInt16, interleaved: true)
                guard decoded.length > 0, (1...2).contains(Int(decoded.processingFormat.channelCount)) else { throw SpeechError.response("A speech part could not be decoded as complete PCM audio.") }
            }
            let output = SpeechFiles.next(folder: folder, stem: stem + (request.variations > 1 ? "_v\(v + 1)" : ""), ext: ".wav")
            await progress("Assembling variation \(v + 1) into one WAV.")
            try assemble((0..<chunks.count).map { partURL(v, $0) }, output: output)
            checkpoint.recordings[v].outputName = output.lastPathComponent; checkpoint.recordings[v].outputHash = try hash(output); try save(checkpoint, at: manifest); results.append(output)
        }
        for v in 0..<request.variations { for i in 0..<chunks.count { try fm.removeItem(at: partURL(v, i)) } }
        try fm.removeItem(at: manifest)
        if try fm.contentsOfDirectory(atPath: job.path).isEmpty { try fm.removeItem(at: job) }
        return results
    }
    private static func save(_ checkpoint: Checkpoint, at url: URL) throws { try SpeechFiles.json(checkpoint).write(to: url, options: .atomic) }
    private static func hash(_ url: URL) throws -> String {
        let values = try url.resourceValues(forKeys: [.isSymbolicLinkKey, .fileSizeKey])
        guard values.isSymbolicLink != true, let size = values.fileSize, size > 0, size <= maximumWaveBytes else { throw SpeechError.validation("A speech recording is missing, redirected or too large.") }
        let reader = try FileHandle(forReadingFrom: url); defer { try? reader.close() }; var sha = SHA256()
        while let data = try reader.read(upToCount: 65536), !data.isEmpty { sha.update(data: data) }
        return sha.finalize().map { String(format: "%02x", $0) }.joined()
    }
    private static func validate(_ url: URL, hash expected: String) throws { guard try hash(url) == expected else { throw SpeechError.validation("A retained speech recording is missing or changed. No request was sent.") } }
    static func assemble(_ parts: [URL], output: URL) throws {
        let partial = output.deletingLastPathComponent().appendingPathComponent("\(UUID().uuidString).partial.wav"), fm = FileManager.default
        defer { try? fm.removeItem(at: partial) }
        var writer: AVAudioFile?, total = 0
        for part in parts {
            try Task.checkCancellation()
            let reader = try AVAudioFile(forReading: part, commonFormat: .pcmFormatInt16, interleaved: true), format = reader.processingFormat
            guard reader.length > 0, (1...2).contains(Int(format.channelCount)) else { throw SpeechError.response("A speech part could not be decoded as complete PCM audio.") }
            if let writer {
                guard writer.processingFormat == format else { throw SpeechError.response("Speech parts have different audio formats.") }
            } else {
                writer = try AVAudioFile(forWriting: partial, settings: [AVFormatIDKey: kAudioFormatLinearPCM, AVSampleRateKey: format.sampleRate, AVNumberOfChannelsKey: format.channelCount, AVLinearPCMBitDepthKey: 16, AVLinearPCMIsFloatKey: false, AVLinearPCMIsBigEndianKey: false], commonFormat: .pcmFormatInt16, interleaved: true)
            }
            guard let buffer = AVAudioPCMBuffer(pcmFormat: format, frameCapacity: 8192) else { throw SpeechError.response("Could not allocate the speech assembly buffer.") }
            while reader.framePosition < reader.length {
                try Task.checkCancellation(); try reader.read(into: buffer)
                guard buffer.frameLength > 0 else { throw SpeechError.response("A speech part ended unexpectedly.") }
                total += Int(buffer.frameLength) * Int(format.channelCount) * 2
                guard total <= maximumWaveBytes else { throw SpeechError.validation("The assembled recording exceeds 1 GB. Generate shorter passages.") }
                try writer?.write(from: buffer)
            }
        }
        guard writer != nil else { throw SpeechError.response("No speech parts were available.") }
        writer = nil; try Task.checkCancellation()
        try fm.moveItem(at: partial, to: output)
    }
}
