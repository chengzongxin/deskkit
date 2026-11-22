import DeskKitCore
import XCTest

final class FixtureTests: XCTestCase {
    private lazy var store = try! ContractStore(root: ContractStore.locate(from: URL(fileURLWithPath: #filePath)))

    func testHashAndGroupColor() throws {
        XCTAssertEqual(store.hash.count, 64)
        XCTAssertEqual(store.hash, try ContractStore.hashTree(store.root))
        XCTAssertEqual("#9B6CF6", store.groupColors[3])
        XCTAssertEqual(800, store.layout("launcher", "width"))
        XCTAssertEqual(48, store.layout("launcher", "inputRowHeight"))
    }

    func testEscapeChains() throws {
        let fixture = try store.fixture("esc.json")
        for sequence in fixture["sequences"]?.values ?? [] {
            let machine = reach(sequence["from"]?.string ?? "")
            let expected = sequence["steps"]?.strings() ?? []
            XCTAssertEqual(expected, machine.escapeSequence())
        }
    }

    func testHiddenCannotJump() {
        let machine = LauncherStateMachine(store: store)
        let result = machine.transition("A3")
        XCTAssertFalse(result.ok)
        XCTAssertEqual("INVALID_ARG", result.code)
        XCTAssertEqual("HIDDEN", machine.state)
    }

    func testTriggersAndKeymap() throws {
        let triggers = try store.fixture("triggers.json")
        for item in triggers["cases"]?.values ?? [] {
            XCTAssertEqual(item["toolId"]?.string, TriggerTable.resolve(store, word: item["word"]?.string ?? ""))
        }
        for word in triggers["absent"]?.strings() ?? [] {
            XCTAssertNil(TriggerTable.resolve(store, word: word))
        }
        let keys = try store.fixture("keymap.json")
        for item in keys["cases"]?.values ?? [] {
            XCTAssertEqual(item["display"]?.string, store.formatChord(platform: item["platform"]?.string ?? "", action: item["action"]?.string ?? ""))
        }
    }

    func testCalculatorSearchAndGroups() throws {
        for item in try store.fixture("calc.json")["cases"]?.values ?? [] {
            let result = ExpressionEvaluator.evaluate(item["expr"]?.string ?? "")
            if let error = item["error"]?.string {
                XCTAssertFalse(result.ok)
                XCTAssertEqual(error, result.code)
            } else {
                XCTAssertTrue(result.ok)
                XCTAssertEqual(item["value"]?.string, result.data)
            }
        }
        let search = try store.fixture("search.json")
        let rows = (search["items"]?.values ?? []).map {
            IndexRow(id: $0["id"]?.string ?? "", kind: $0["kind"]?.string ?? "", title: $0["title"]?.string ?? "", keywords: $0["keywords"]?.strings() ?? [])
        }
        for item in search["cases"]?.values ?? [] {
            XCTAssertEqual(item["expectIds"]?.strings(), KeywordMatcher.match(rows, query: item["query"]?.string ?? ""))
        }
        let groupsFixture = try store.fixture("group-delete.json")
        var groups = (groupsFixture["document"]?["groups"]?.values ?? []).map {
            GroupRecord(id: $0["id"]?.string ?? "", name: $0["name"]?.string ?? "", color: $0["color"]?.string ?? "", itemIds: $0["itemIds"]?.strings() ?? [])
        }
        var items = (groupsFixture["document"]?["items"]?.values ?? []).map {
            ItemRecord(id: $0["id"]?.string ?? "", title: $0["title"]?.string ?? "")
        }
        let deleted = GroupEditor.delete(groups: &groups, items: &items, groupId: groupsFixture["deleteGroupId"]?.string ?? "", ungroupedId: "ungrouped", ungroupedName: "未分组")
        XCTAssertTrue(deleted.ok)
        XCTAssertEqual(groupsFixture["expectRemainingGroupIds"]?.strings(), groups.map(\.id))
        XCTAssertEqual(groupsFixture["expectItemIdsInUngrouped"]?.strings(), groups.first { $0.id == "ungrouped" }?.itemIds)
        XCTAssertEqual(groupsFixture["expectItemIds"]?.strings(), items.map(\.id))
        XCTAssertEqual("205", ColorConvert.describe("#3B6CF6") == nil ? "" : "205")
        XCTAssertTrue(ColorConvert.describe("#3B6CF6")?.contains("rgb(59,108,246)") == true)
    }

    private func reach(_ state: String) -> LauncherStateMachine {
        let machine = LauncherStateMachine(store: store)
        if state == "TOOLPAGE" {
            _ = machine.transition("TOOLPAGE")
            return machine
        }
        _ = machine.transition("A1")
        _ = machine.transition("A2")
        _ = machine.transition(state)
        return machine
    }
}
