import Foundation
import XCTest
@testable import ElevenLabsSpeechGeneratorMac

final class VoiceAuditionTests: XCTestCase {
    private func voice(_ id: String) -> CatalogItem { CatalogItem(id: id, name: id, data: ["preview_url": "https://example.invalid/\(id)", "category": "cloned", "is_owner": true]) }
    @MainActor func testLatestSelectionStopAndCleanup() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: folder) }
        var waiting: [String: CheckedContinuation<Void, Never>] = [:]
        var played: [String] = [], errors: [Error] = [], playing = false
        let audition = VoiceAudition(folder: folder, download: { url, path in
            await withCheckedContinuation { waiting[url.lastPathComponent] = $0 }
            try Data(url.lastPathComponent.utf8).write(to: path)
        }, play: { path in played.append((try? String(contentsOf: path)) ?? ""); playing = true }, stop: { playing = false }, isPlaying: { playing }, failed: { errors.append($0) })
        audition.toggle(voice("old")); let old = audition.lastRequest!; await Task.yield()
        audition.toggle(voice("new")); let new = audition.lastRequest!; await Task.yield()
        waiting["new"]?.resume(); await new.value
        waiting["old"]?.resume(); await old.value
        XCTAssertEqual(played, ["new"])
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: folder.path).count, 1)
        audition.toggle(voice("new")); XCTAssertFalse(playing)
        XCTAssertTrue(try FileManager.default.contentsOfDirectory(atPath: folder.path).isEmpty)
        audition.toggle(voice("cancelled")); let cancelled = audition.lastRequest!; await Task.yield()
        audition.toggle(voice("cancelled")); waiting["cancelled"]?.resume(); await cancelled.value
        XCTAssertEqual(played, ["new"])
        XCTAssertTrue(try FileManager.default.contentsOfDirectory(atPath: folder.path).isEmpty)
        audition.select(CatalogItem(id: "missing", name: "Missing"), automatic: true); XCTAssertTrue(errors.isEmpty)
        audition.toggle(nil); XCTAssertEqual(errors.count, 1)
        audition.toggle(voice("closed")); let closed = audition.lastRequest!; await Task.yield()
        audition.stop(); waiting["closed"]?.resume(); await closed.value
        XCTAssertEqual(played, ["new"])
        XCTAssertTrue(try FileManager.default.contentsOfDirectory(atPath: folder.path).isEmpty)
    }
    @MainActor func testAutomaticDebounceAndReplay() async throws {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString)
        defer { try? FileManager.default.removeItem(at: folder) }
        var downloads = 0, plays = 0, playing = false
        let audition = VoiceAudition(folder: folder, download: { _, path in downloads += 1; try Data([1]).write(to: path) }, play: { _ in plays += 1; playing = true }, stop: { playing = false }, isPlaying: { playing }, failed: { XCTFail($0.localizedDescription) })
        audition.select(voice("first"), automatic: true); let first = audition.lastRequest!
        audition.select(voice("last"), automatic: true); let last = audition.lastRequest!
        await first.value; await last.value
        XCTAssertEqual(downloads, 1); XCTAssertEqual(plays, 1)
        playing = false; audition.toggle(voice("last")); await audition.lastRequest?.value
        XCTAssertEqual(plays, 2)
        audition.select(nil, automatic: false); XCTAssertFalse(playing)
        XCTAssertTrue(try FileManager.default.contentsOfDirectory(atPath: folder.path).isEmpty)
    }
    func testOwnershipNamesAndPreferences() throws {
        XCTAssertTrue(VoiceNames.canRename(voice("own")))
        for data: [String: Any] in [["is_owner": false, "category": "cloned"], ["category": "cloned"], ["is_owner": true, "category": "premade"]] {
            XCTAssertFalse(VoiceNames.canRename(CatalogItem(id: "other", name: "Other", data: data)))
        }
        XCTAssertEqual(try VoiceNames.validate("  New name  "), "New name")
        for name in ["", " ", "two\nlines"] { XCTAssertThrowsError(try VoiceNames.validate(name)) }
        XCTAssertFalse(AppPreferences().autoPreviewVoices == true)
        var preferences = AppPreferences(); preferences.autoPreviewVoices = true
        XCTAssertEqual(try JSONDecoder().decode(AppPreferences.self, from: JSONEncoder().encode(preferences)).autoPreviewVoices, true)
        var legacy = try JSONSerialization.jsonObject(with: JSONEncoder().encode(preferences)) as! [String: Any]; legacy.removeValue(forKey: "autoPreviewVoices")
        XCTAssertFalse(try JSONDecoder().decode(AppPreferences.self, from: JSONSerialization.data(withJSONObject: legacy)).autoPreviewVoices == true)
        XCTAssertGreaterThanOrEqual(SpeechTags.examples.count, 70)
        XCTAssertEqual(Set(SpeechTags.examples.map { $0.lowercased() }).count, SpeechTags.examples.count)
        for tag in SpeechTags.examples { XCTAssertEqual(try SpeechTags.normalize(tag), tag) }
    }
}
