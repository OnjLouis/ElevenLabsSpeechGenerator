import XCTest
@testable import ElevenLabsSpeechGeneratorMac

final class VoiceGroupTests: XCTestCase {
    func testOwnershipCategoryAndSelection() {
        let own = CatalogItem(id: "own", name: "Own", data: ["category": "professional", "is_owner": true])
        let shared = CatalogItem(id: "shared", name: "Shared", data: ["category": "professional", "is_owner": false])
        let standard = CatalogItem(id: "default", name: "Default", data: ["category": "premade"])
        XCTAssertTrue(VoiceGroups.matches(own, group: "Your voices"))
        XCTAssertFalse(VoiceGroups.matches(shared, group: "Your voices"))
        XCTAssertTrue(VoiceGroups.matches(shared, group: "Shared voices"))
        XCTAssertTrue(VoiceGroups.matches(standard, group: "Default voices"))
        let choices = VoiceGroups.choices([own, shared, standard], group: "Your voices", selected: "default")
        XCTAssertEqual(choices.map(\.id), ["own", "default"])
        XCTAssertTrue(choices.last!.name.contains("outside group"))
    }
}
