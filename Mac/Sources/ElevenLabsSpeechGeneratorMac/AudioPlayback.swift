import AppKit

protocol PlaybackSound: AnyObject {
    var delegate: NSSoundDelegate? { get set }
    var playbackDeviceIdentifier: String? { get set }
    func play() -> Bool
    func stop() -> Bool
}
extension NSSound: PlaybackSound {}

@MainActor final class AudioPlayback: NSObject, NSSoundDelegate {
    private var pending: [URL] = []
    private var current: PlaybackSound?
    private var output = ""
    private let makeSound: (URL) -> PlaybackSound?
    var onError: ((String) -> Void)?
    var isPlaying: Bool { current != nil }

    init(makeSound: @escaping (URL) -> PlaybackSound? = { NSSound(contentsOf: $0, byReference: true) }) {
        self.makeSound = makeSound
    }
    func play(_ url: URL, device: String) { playSequence([url], device: device) }
    func playSequence(_ urls: [URL], device: String) {
        stop()
        output = device
        pending = urls
        startNext()
    }
    func stop() {
        pending.removeAll()
        let old = current
        current = nil
        old?.delegate = nil
        _ = old?.stop()
    }
    private func startNext() {
        guard !pending.isEmpty else { return }
        let url = pending.removeFirst()
        guard let next = makeSound(url) else { fail(); return }
        current = next
        next.delegate = self
        if !output.isEmpty { next.playbackDeviceIdentifier = output }
        if !next.play() { fail() }
    }
    private func fail() { stop(); onError?("The audio file could not be played. Check the playback device and try again.") }
    // Stop/replacement detaches the old sound; an already queued callback must not advance the new queue.
    func finished(_ sound: PlaybackSound, success: Bool) {
        guard current === sound else { return }
        current?.delegate = nil
        current = nil
        if !success { fail(); return }
        startNext()
    }
    nonisolated func sound(_ sound: NSSound, didFinishPlaying flag: Bool) {
        Task { @MainActor [weak self] in self?.finished(sound, success: flag) }
    }
}
