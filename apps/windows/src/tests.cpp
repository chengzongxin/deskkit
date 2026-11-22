#include "deskkit.hpp"

#include "json.hpp"

#include <iostream>
#include <stdexcept>

namespace {

using json = nlohmann::json;
int g_failed = 0;

void Expect(bool condition, const std::string& message) {
    if (condition) return;
    std::cerr << "失败 " << message << "\n";
    ++g_failed;
}

dk::StateMachine Reach(const dk::ContractStore& store, const std::string& state) {
    dk::StateMachine machine(store);
    if (state == "TOOLPAGE") {
        machine.Transition("TOOLPAGE");
        return machine;
    }
    machine.Transition("A1");
    machine.Transition("A2");
    machine.Transition(state);
    return machine;
}

std::vector<std::string> Strings(const json& root, const char* name) {
    std::vector<std::string> values;
    for (const auto& item : root.at(name)) values.push_back(item.get<std::string>());
    return values;
}

}  // namespace

int main() {
    const auto root = dk::ContractStore::Locate();
    if (!root) {
        std::cerr << "找不到 contracts\n";
        return 1;
    }
    dk::ContractStore store(*root);
    Expect(store.Hash().size() == 64, "哈希长度");
    Expect(store.Hash() == dk::ContractStore::HashTree(store.Root()), "哈希稳定");
    Expect(store.GroupColors().size() > 3 && store.GroupColors()[3] == "#9B6CF6", "分组第 4 色");
    Expect(store.Layout({"launcher", "width"}) == 800, "启动器宽度");
    Expect(store.Layout({"launcher", "inputRowHeight"}) == 48, "输入行高度");

    const json esc = json::parse(store.FixtureText("esc.json"));
    for (const auto& sequence : esc.at("sequences")) {
        auto machine = Reach(store, sequence.at("from").get<std::string>());
        Expect(machine.EscapeSequence() == Strings(sequence, "steps"), "Esc 链 " + sequence.at("from").get<std::string>());
    }

    dk::StateMachine hidden(store);
    const dk::Result denied = hidden.Transition("A3");
    Expect(!denied.ok && denied.code == "INVALID_ARG" && hidden.State() == "HIDDEN", "隐藏不能跳到结果");

    const json triggers = json::parse(store.FixtureText("triggers.json"));
    for (const auto& item : triggers.at("cases")) {
        Expect(dk::ResolveTrigger(store, item.at("word").get<std::string>()) == item.at("toolId").get<std::string>(), "触发词");
    }
    for (const auto& word : triggers.at("absent")) {
        Expect(!dk::ResolveTrigger(store, word.get<std::string>()), "不存在的触发词");
    }
    const json keys = json::parse(store.FixtureText("keymap.json"));
    for (const auto& item : keys.at("cases")) {
        Expect(store.FormatChord(item.at("platform").get<std::string>(), item.at("action").get<std::string>()) == item.at("display").get<std::string>(), "快捷键文案");
    }

    for (const auto& item : json::parse(store.FixtureText("calc.json")).at("cases")) {
        const dk::Result result = dk::EvaluateExpression(item.at("expr").get<std::string>());
        if (item.contains("error")) Expect(!result.ok && result.code == item.at("error").get<std::string>(), item.at("expr").get<std::string>());
        else Expect(result.ok && result.data == item.at("value").get<std::string>(), item.at("expr").get<std::string>());
    }

    const json search = json::parse(store.FixtureText("search.json"));
    std::vector<dk::IndexRow> rows;
    for (const auto& item : search.at("items")) {
        rows.push_back(dk::IndexRow{item.at("id").get<std::string>(), item.at("kind").get<std::string>(), item.at("title").get<std::string>(), item.at("keywords").get<std::vector<std::string>>()});
    }
    for (const auto& item : search.at("cases")) {
        Expect(dk::MatchKeywords(rows, item.at("query").get<std::string>()) == Strings(item, "expectIds"), "搜索 " + item.at("query").get<std::string>());
    }

    const json groupsFixture = json::parse(store.FixtureText("group-delete.json"));
    std::vector<dk::GroupRecord> groups;
    for (const auto& item : groupsFixture.at("document").at("groups")) {
        groups.push_back(dk::GroupRecord{item.at("id").get<std::string>(), item.at("name").get<std::string>(), item.at("color").get<std::string>(), item.at("itemIds").get<std::vector<std::string>>()});
    }
    std::vector<dk::ItemRecord> items;
    for (const auto& item : groupsFixture.at("document").at("items")) {
        items.push_back(dk::ItemRecord{item.at("id").get<std::string>(), item.at("title").get<std::string>()});
    }
    const dk::Result deleted = dk::DeleteGroup(groups, items, groupsFixture.at("deleteGroupId").get<std::string>(), "ungrouped");
    Expect(deleted.ok, "删除分组");
    std::vector<std::string> groupIds;
    for (const dk::GroupRecord& group : groups) groupIds.push_back(group.id);
    Expect(groupIds == Strings(groupsFixture, "expectRemainingGroupIds"), "剩下的分组");
    const auto ungrouped = std::find_if(groups.begin(), groups.end(), [](const dk::GroupRecord& group) { return group.id == "ungrouped"; });
    Expect(ungrouped != groups.end() && ungrouped->itemIds == Strings(groupsFixture, "expectItemIdsInUngrouped"), "未分组条目");
    std::vector<std::string> itemIds;
    for (const dk::ItemRecord& item : items) itemIds.push_back(item.id);
    Expect(itemIds == Strings(groupsFixture, "expectItemIds"), "剩下的快捷项");
    const auto color = dk::DescribeColor("#3B6CF6");
    Expect(color && color->find("rgb(59,108,246)") != std::string::npos, "颜色描述");

    const dk::StarterCatalog catalog = store.Starter();
    std::vector<std::string> colors;
    for (const dk::StarterGroupSpec& group : catalog.groups) colors.push_back(group.color);
    Expect(colors == std::vector<std::string>({"#3B6CF6", "#18A058", "#F0A020", "#9B6CF6"}), "预置分组颜色");
    Expect(catalog.groups.size() > 3 && catalog.groups[3].name == "应用", "应用分组");
    Expect(catalog.ungrouped.color == "#98A1B0" && catalog.ungrouped.name == "未分组", "未分组");
    const std::vector<dk::StarterFound> found = {
        {"files", "文件资源管理器", "C:\\Windows\\explorer.exe", "system"},
        {"notepad", "记事本", "C:\\Windows\\System32\\notepad.exe", "tools"},
        {"snip", "截图工具", "C:\\Users\\me\\截图工具.lnk", "tools"},
        {"gone", "不存在", "  ", "tools"}};
    const dk::StarterBuilt built = dk::ComposeStarter(catalog.groups, found, catalog.ungrouped);
    std::vector<std::string> builtIds;
    for (const dk::GroupRecord& group : built.groups) builtIds.push_back(group.id);
    Expect(builtIds == std::vector<std::string>({"system", "tools", "ungrouped"}), "空分组被跳过");
    Expect(built.groups[0].itemIds == std::vector<std::string>({"files"}), "系统组");
    Expect(built.groups[1].itemIds == std::vector<std::string>({"notepad", "snip"}), "工具组");
    Expect(built.groups[2].itemIds.empty(), "未分组为空");
    Expect(built.items.size() == 3 && built.items[0].kind == "app" && built.items[2].title == "截图工具", "快捷项种类");
    const dk::StarterBuilt empty = dk::ComposeStarter(catalog.groups, {}, catalog.ungrouped);
    Expect(empty.groups.size() == 1 && empty.groups[0].id == "ungrouped" && empty.items.empty(), "全空只留未分组");

    if (g_failed) {
        std::cerr << g_failed << " 项失败\n";
        return 1;
    }
    std::cout << "夹具通过\n";
    return 0;
}
