import Foundation

enum RunCreditUsage {
    static func parse(_ header: String?) -> Decimal? {
        guard let text = header?.trimmingCharacters(in: .whitespaces), !text.isEmpty, text.utf8.count <= 24 else { return nil }
        let parts = text.split(separator: ".", omittingEmptySubsequences: false)
        guard parts.count <= 2, parts.allSatisfy({ !$0.isEmpty && $0.utf8.allSatisfy({ (48...57).contains($0) }) }) else { return nil }
        return Decimal(string: text, locale: Locale(identifier: "en_US_POSIX"))
    }

    static func completionLine(_ reported: [Decimal?]) -> String? {
        guard !reported.isEmpty else { return nil }
        var total: Decimal = 0
        for amount in reported {
            guard let amount, amount >= 0 else { return nil }
            total += amount
        }
        let formatter = NumberFormatter()
        formatter.numberStyle = .decimal
        formatter.maximumFractionDigits = 23
        guard let value = formatter.string(from: total as NSDecimalNumber) else { return nil }
        return "Credits used this run: \(value)."
    }
}
