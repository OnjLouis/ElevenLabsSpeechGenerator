import Foundation

struct PublishedMacRelease {
    let version: String
    let page: URL
    let downloadURL: URL
    let assetName: String
}

enum MacPackageArchitecture {
    case appleSilicon
    case intel

    static var current: Self {
        #if arch(x86_64)
        return .intel
        #else
        return .appleSilicon
        #endif
    }

    func assetName(version: String) -> String {
        switch self {
        case .appleSilicon: return "ElevenLabs-Speech-Generator-Mac-\(version).zip"
        case .intel: return "ElevenLabs-Speech-Generator-Mac-Intel-\(version).zip"
        }
    }
}

enum ReleaseCatalog {
    static func newerMacRelease(in data: Data, currentVersion: String,
                                architecture: MacPackageArchitecture = .current) throws -> PublishedMacRelease? {
        guard let releases = try JSONSerialization.jsonObject(with: data) as? [[String: Any]],
              let current = versionParts(currentVersion) else {
            throw SpeechError.response("The release list could not be read.")
        }
        return releases.compactMap { item -> (release: PublishedMacRelease, parts: [Int])? in
            guard item["draft"] as? Bool == false,
                  item["prerelease"] as? Bool == false,
                  let tag = item["tag_name"] as? String,
                  let parts = versionParts(tag), current.lexicographicallyPrecedes(parts),
                  let pageText = item["html_url"] as? String,
                  let page = URL(string: pageText), page.scheme == "https", page.host == "github.com",
                  let assets = item["assets"] as? [[String: Any]],
                  let asset = assets.first(where: { asset in
                      asset["name"] as? String == architecture.assetName(version: parts.map(String.init).joined(separator: "."))
                  }),
                  let name = asset["name"] as? String,
                  let downloadText = asset["browser_download_url"] as? String,
                  let downloadURL = URL(string: downloadText),
                  downloadURL.absoluteString == "https://github.com/OnjLouis/ElevenLabsSpeechGenerator/releases/download/\(tag)/\(name)" else { return nil }
            return (PublishedMacRelease(version: parts.map(String.init).joined(separator: "."), page: page,
                                        downloadURL: downloadURL, assetName: name), parts)
        }.max(by: { $0.parts.lexicographicallyPrecedes($1.parts) })?.release
    }

    private static func versionParts(_ text: String) -> [Int]? {
        let clean = text.trimmingCharacters(in: CharacterSet(charactersIn: "vV"))
        let parts = clean.split(separator: ".").compactMap { Int($0) }
        return parts.count == 3 ? parts : nil
    }
}
