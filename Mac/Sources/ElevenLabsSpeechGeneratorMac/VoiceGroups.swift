import Foundation

enum VoiceGroups {
    static let names = ["All voices", "Your voices", "Cloned voices", "Designed voices", "Default voices", "Shared voices"]
    static func matches(_ voice: CatalogItem, group: String) -> Bool {
        let category = voice.string("category")
        let owner = voice.data["is_owner"] as? Bool
        switch group {
        case "Your voices": return owner == true && category != "premade"
        case "Cloned voices": return category == "cloned" || category == "professional"
        case "Designed voices": return category == "generated"
        case "Default voices": return category == "premade"
        case "Shared voices": return owner == false && category != "premade"
        default: return true
        }
    }
    static func choices(_ voices: [CatalogItem], group: String, selected: String) -> [CatalogItem] {
        var filtered = voices.filter { matches($0, group: group) }
        if let current = voices.first(where: { $0.id == selected }), !filtered.contains(where: { $0.id == selected }) {
            filtered.append(CatalogItem(id: current.id, name: current.name + " (current, outside group)", data: current.data))
        }
        return CatalogItem.preservingSelection(filtered, id: selected)
    }
}
