import Foundation

struct SubscriptionBalance: Decodable {
    enum CreditExtension: Decodable {
        case unlimited
        case capped(Int64)
        case unknown

        init(from decoder: Decoder) throws {
            let value = try decoder.singleValueContainer()
            if let text = try? value.decode(String.self) {
                if text.lowercased() == "unlimited" { self = .unlimited }
                else if let amount = Int64(text) { self = .capped(amount) }
                else { self = .unknown }
            } else if let amount = try? value.decode(Int64.self) {
                self = .capped(amount)
            } else {
                self = .unknown
            }
        }

        var isEnabled: Bool? {
            switch self {
            case .unlimited: return true
            case .capped(let amount): return amount >= 0 ? amount > 0 : nil
            case .unknown: return nil
            }
        }
    }

    struct Overage: Decodable {
        let amount: String?
        let currency: String?
    }

    let characterCount: Int64
    let characterLimit: Int64
    let nextResetUnix: Int64?
    let maxCreditLimitExtension: CreditExtension?
    let currentOverage: Overage?

    enum CodingKeys: String, CodingKey {
        case characterCount = "character_count"
        case characterLimit = "character_limit"
        case nextResetUnix = "next_character_count_reset_unix"
        case maxCreditLimitExtension = "max_credit_limit_extension"
        case currentOverage = "current_overage"
    }

    var remaining: Int64 { max(0, characterLimit - characterCount) }

    static func decode(_ data: Data) throws -> Self {
        let value = try JSONDecoder().decode(Self.self, from: data)
        guard value.characterCount >= 0, value.characterLimit >= 0,
              value.nextResetUnix.map({ $0 > 0 && $0 <= 253_402_300_799 }) ?? true else {
            throw SpeechError.response("ElevenLabs returned an invalid subscription count.")
        }
        return value
    }

    func display(now: Date = Date()) -> String {
        let left = NumberFormatter.localizedString(from: NSNumber(value: remaining), number: .decimal)
        let limit = NumberFormatter.localizedString(from: NSNumber(value: characterLimit), number: .decimal)
        let used = NumberFormatter.localizedString(from: NSNumber(value: characterCount), number: .decimal)
        var percent = ""
        if characterLimit > 0 {
            let ratio = NSDecimalNumber(decimal: 100 * Decimal(remaining) / Decimal(characterLimit))
            let whole = NumberFormatter.localizedString(from: NSNumber(value: ratio.intValue), number: .decimal)
            percent = " (\(whole)%)"
        }
        var result = "Included allowance remaining: \(left) of \(limit) credits\(percent).\nUsed this period: \(used) credits."
        if characterCount > characterLimit {
            let excess = NumberFormatter.localizedString(from: NSNumber(value: characterCount - characterLimit), number: .decimal)
            result += "\nIncluded allowance exceeded by \(excess) credits."
            result += "\nYour total spendable balance is not available from this check."
        }
        result += "\n" + resetDisplay(now: now)
        if maxCreditLimitExtension?.isEnabled == true {
            result += "\nOverage billing: on."
        }
        if let rawAmount = currentOverage?.amount,
           let amount = Decimal(string: rawAmount, locale: Locale(identifier: "en_US_POSIX")), amount > 0,
           let currency = currentOverage?.currency?.uppercased(),
           currency.utf8.count == 3, currency.utf8.allSatisfy({ (65...90).contains($0) }) {
            let formatter = NumberFormatter()
            formatter.numberStyle = .decimal
            formatter.minimumFractionDigits = 2
            formatter.maximumFractionDigits = 2
            if let formatted = formatter.string(from: amount as NSDecimalNumber) {
                result += "\nCurrent overage charge: \(currency) \(formatted)."
            }
        }
        return result
    }

    private func resetDisplay(now: Date) -> String {
        guard let nextResetUnix, nextResetUnix > 0 else { return "Next reset: unavailable." }
        let reset = Date(timeIntervalSince1970: TimeInterval(nextResetUnix))
        let date = DateFormatter()
        date.dateStyle = .medium
        date.timeStyle = .short
        date.timeZone = .current
        var result = "Next reset: \(date.string(from: reset)) \(TimeZone.current.abbreviation() ?? "local time")."
        let seconds = reset.timeIntervalSince(now)
        if seconds > 0 {
            let minutes = Int(ceil(seconds / 60))
            let days = minutes / 1440
            let hours = minutes % 1440 / 60
            var parts: [String] = []
            if days > 0 { parts.append("\(days) \(days == 1 ? "day" : "days")") }
            if hours > 0 { parts.append("\(hours) \(hours == 1 ? "hour" : "hours")") }
            if minutes % 60 > 0 { parts.append("\(minutes % 60) \(minutes % 60 == 1 ? "minute" : "minutes")") }
            result += " Approximately \(parts.joined(separator: ", ")) remaining."
        }
        return result
    }
}
