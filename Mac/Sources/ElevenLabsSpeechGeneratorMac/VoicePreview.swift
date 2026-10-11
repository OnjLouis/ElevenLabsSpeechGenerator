import Foundation

enum VoicePreview {
    static func download(_ url: URL, to target: URL) async throws {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForResource = 30
        let (data, response) = try await BoundedResponse(limit: 20 * 1024 * 1024).receive(URLRequest(url: url, timeoutInterval: 30), upload: nil, configuration: configuration)
        guard (response as? HTTPURLResponse)?.statusCode == 200, response.url?.scheme == "https", !data.isEmpty else {
            throw SpeechError.response("The preview could not be downloaded.")
        }
        try Task.checkCancellation()
        try data.write(to: target, options: .atomic)
    }
    static func url(_ voice: CatalogItem?) -> URL? {
        guard let voice, let url = URL(string: voice.string("preview_url")), url.scheme == "https", url.host != nil, url.user == nil, url.password == nil else { return nil }
        return url
    }
}
