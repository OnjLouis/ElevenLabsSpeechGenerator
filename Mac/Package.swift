// swift-tools-version: 5.9
import PackageDescription

let package = Package(
    name: "ElevenLabsSpeechGeneratorMac",
    platforms: [.macOS(.v14)],
    products: [.executable(name: "ElevenLabsSpeechGeneratorMac", targets: ["ElevenLabsSpeechGeneratorMac"])],
    targets: [
        .executableTarget(name: "ElevenLabsSpeechGeneratorMac"),
        .testTarget(name: "ElevenLabsSpeechGeneratorMacTests", dependencies: ["ElevenLabsSpeechGeneratorMac"])
    ]
)
