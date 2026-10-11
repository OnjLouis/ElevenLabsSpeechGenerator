import Foundation

enum CatalogPageMove { case refresh, next, previous }

struct CatalogPagination {
    private var previous: [String] = []
    private var context: String?
    private var current = ""
    private var next = ""
    func canPrevious(_ scope: String) -> Bool { context == scope && !previous.isEmpty }
    func canNext(_ scope: String) -> Bool { context == scope && !next.isEmpty }
    func request(_ move: CatalogPageMove, scope: String) -> String? {
        if move == .refresh || context != scope { return "" }
        return move == .previous ? previous.last : canNext(scope) ? next : nil
    }
    mutating func commit(_ move: CatalogPageMove, scope: String, page: String, following: String) {
        // Failed requests never commit, preserving the previous page and navigation state.
        if move == .refresh || context != scope { previous = [] }
        else if move == .previous { previous.removeLast() }
        else { previous.append(current) }
        context = scope; current = page; next = following
        if next == current || previous.contains(next) { next = "" }
    }
}
