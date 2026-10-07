import Foundation

enum VoicePreview {
    static func url(_ voice: CatalogItem?) -> URL? {
        guard let voice, let url = URL(string: voice.string("preview_url")), url.scheme == "https", url.host != nil, url.user == nil, url.password == nil else { return nil }
        return url
    }
}
