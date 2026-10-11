import XCTest
@testable import ElevenLabsSpeechGeneratorMac

final class CatalogPaginationTests: XCTestCase {
    func testForwardBackwardFailureAndQueryChanges() {
        for scope in ["History", "Account voices", "Shared voices", "Dictionaries"] {
            var pages = CatalogPagination()
            XCTAssertFalse(pages.canPrevious(scope)); XCTAssertFalse(pages.canNext(scope))
            pages.commit(.refresh, scope: scope, page: "", following: "one")
            XCTAssertNil(pages.request(.previous, scope: scope))
            XCTAssertEqual(pages.request(.next, scope: scope), "one")
            pages.commit(.next, scope: scope, page: "one", following: "two")
            XCTAssertEqual(pages.request(.previous, scope: scope), "")
            XCTAssertEqual(pages.request(.next, scope: scope), "two")
            // A failed request does not commit; retry must use the same cursor.
            XCTAssertEqual(pages.request(.next, scope: scope), "two")
            pages.commit(.next, scope: scope, page: "two", following: "")
            XCTAssertFalse(pages.canNext(scope)); XCTAssertTrue(pages.canPrevious(scope))
            XCTAssertEqual(pages.request(.previous, scope: scope), "one")
            pages.commit(.previous, scope: scope, page: "one", following: "two")
            pages.commit(.previous, scope: scope, page: "", following: "one")
            XCTAssertFalse(pages.canPrevious(scope)); XCTAssertTrue(pages.canNext(scope))
            XCTAssertFalse(pages.canNext(scope + "changed")); XCTAssertFalse(pages.canPrevious(scope + "changed"))
            XCTAssertEqual(pages.request(.next, scope: scope + "changed"), "")
            pages.commit(.refresh, scope: scope + "changed", page: "", following: "one")
            pages.commit(.next, scope: scope + "changed", page: "one", following: "one")
            XCTAssertFalse(pages.canNext(scope + "changed"))
            pages.commit(.refresh, scope: scope, page: "", following: "")
            XCTAssertFalse(pages.canPrevious(scope)); XCTAssertFalse(pages.canNext(scope))
        }
    }
}
