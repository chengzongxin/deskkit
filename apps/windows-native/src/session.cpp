#include "deskkit.hpp"

#include "json.hpp"

#include <windows.h>

#include <algorithm>
#include <cctype>
#include <chrono>
#include <cstdio>
#include <filesystem>

namespace dk {
namespace {

using json = nlohmann::json;
namespace fs = std::filesystem;

std::string LowerAscii(std::string text) {
    for (char& ch : text) {
        if (ch >= 'A' && ch <= 'Z') ch = static_cast<char>(ch - 'A' + 'a');
    }
    return text;
}

bool EqualsInsensitive(const std::string& left, const std::string& right) { return LowerAscii(left) == LowerAscii(right); }

bool ContainsInsensitive(const std::string& haystack, const std::string& needle) {
    return LowerAscii(haystack).find(LowerAscii(needle)) != std::string::npos;
}

uint64_t NowMs() {
    return static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::system_clock::now().time_since_epoch()).count());
}

std::string UtcNowIso() {
    SYSTEMTIME system{};
    GetSystemTime(&system);
    char buffer[40];
    std::snprintf(buffer, sizeof(buffer), "%04u-%02u-%02uT%02u:%02u:%02uZ", system.wYear, system.wMonth, system.wDay, system.wHour, system.wMinute, system.wSecond);
    return buffer;
}

json BindingJson(const HotkeyBinding& binding) {
    return json{{"keyCode", binding.keyCode}, {"alt", binding.alt}, {"command", binding.command}, {"control", binding.control}, {"shift", binding.shift}, {"text", binding.text}};
}

HotkeyBinding BindingFrom(const json& item) {
    HotkeyBinding binding;
    binding.keyCode = item.value("keyCode", 0);
    binding.alt = item.value("alt", false);
    binding.command = item.value("command", false);
    binding.control = item.value("control", false);
    binding.shift = item.value("shift", false);
    binding.text = item.value("text", "");
    return binding;
}

json SettingsJson(const Settings& settings) {
    json hotkeys = json::object();
    for (const auto& [id, binding] : settings.hotkeys) hotkeys[id] = BindingJson(binding);
    json triggers = json::object();
    for (const auto& [id, word] : settings.triggerWords) triggers[id] = word;
    return json{
        {"schemaVersion", settings.schemaVersion},
        {"autoLaunch", settings.autoLaunch},
        {"summonMode", settings.summonMode},
        {"blurHide", settings.blurHide},
        {"multiScreen", settings.multiScreen},
        {"theme", settings.theme},
        {"density", settings.density},
        {"accentToken", settings.accentToken},
        {"backgroundImagePath", settings.backgroundImagePath},
        {"backgroundOpacity", settings.backgroundOpacity},
        {"clipboardRetention", settings.clipboardRetention},
        {"clipboardPaused", settings.clipboardPaused},
        {"cloudSync", settings.cloudSync},
        {"autoUpdate", settings.autoUpdate},
        {"guideDone", settings.guideDone},
        {"starterSeeded", settings.starterSeeded},
        {"hiddenTools", settings.hiddenTools},
        {"toolOrder", settings.toolOrder},
        {"summonKeyCode", settings.summonKeyCode},
        {"summonAlt", settings.summonAlt},
        {"summonCommand", settings.summonCommand},
        {"summonControl", settings.summonControl},
        {"summonShift", settings.summonShift},
        {"lastScreen", settings.lastScreen},
        {"panelPlacedByUser", settings.panelPlacedByUser},
        {"panelX", settings.panelX},
        {"panelY", settings.panelY},
        {"panelWidth", settings.panelWidth},
        {"panelHeight", settings.panelHeight},
        {"dismissedClip", settings.dismissedClip},
        {"hotkeys", hotkeys},
        {"triggerWords", triggers}};
}

Settings SettingsFrom(const json& item) {
    Settings settings;
    settings.schemaVersion = item.value("schemaVersion", 1);
    settings.autoLaunch = item.value("autoLaunch", false);
    settings.summonMode = item.value("summonMode", "hotkey");
    settings.blurHide = item.value("blurHide", true);
    settings.multiScreen = item.value("multiScreen", "followCursor");
    settings.theme = item.value("theme", "light");
    settings.density = item.value("density", "standard");
    settings.accentToken = item.value("accentToken", "accent");
    settings.backgroundImagePath = item.value("backgroundImagePath", "");
    settings.backgroundOpacity = item.value("backgroundOpacity", 1.0);
    settings.clipboardRetention = item.value("clipboardRetention", "7d");
    settings.clipboardPaused = item.value("clipboardPaused", false);
    settings.cloudSync = item.value("cloudSync", false);
    settings.autoUpdate = item.value("autoUpdate", true);
    settings.guideDone = item.value("guideDone", false);
    settings.starterSeeded = item.value("starterSeeded", false);
    if (item.contains("hiddenTools")) settings.hiddenTools = item["hiddenTools"].get<std::vector<std::string>>();
    if (item.contains("toolOrder")) settings.toolOrder = item["toolOrder"].get<std::vector<std::string>>();
    settings.summonKeyCode = item.value("summonKeyCode", 49);
    settings.summonAlt = item.value("summonAlt", true);
    settings.summonCommand = item.value("summonCommand", false);
    settings.summonControl = item.value("summonControl", false);
    settings.summonShift = item.value("summonShift", false);
    settings.lastScreen = item.value("lastScreen", "");
    settings.panelPlacedByUser = item.value("panelPlacedByUser", false);
    settings.panelX = item.value("panelX", 0.0);
    settings.panelY = item.value("panelY", 0.0);
    settings.panelWidth = item.value("panelWidth", 0.0);
    settings.panelHeight = item.value("panelHeight", 0.0);
    settings.dismissedClip = item.value("dismissedClip", "");
    if (item.contains("hotkeys") && item["hotkeys"].is_object()) {
        for (auto it = item["hotkeys"].begin(); it != item["hotkeys"].end(); ++it) settings.hotkeys[it.key()] = BindingFrom(it.value());
    }
    if (item.contains("triggerWords") && item["triggerWords"].is_object()) {
        for (auto it = item["triggerWords"].begin(); it != item["triggerWords"].end(); ++it) settings.triggerWords[it.key()] = it.value().get<std::string>();
    }
    return settings;
}

json LibraryJson(const Library& library) {
    json groups = json::array();
    for (const Group& group : library.groups) groups.push_back(json{{"id", group.id}, {"name", group.name}, {"color", group.color}, {"itemIds", group.itemIds}});
    json items = json::array();
    for (const Shortcut& item : library.items) items.push_back(json{{"id", item.id}, {"title", item.title}, {"path", item.path}, {"kind", item.kind}});
    return json{{"schemaVersion", library.schemaVersion}, {"groups", groups}, {"items", items}};
}

Library LibraryFrom(const json& item) {
    Library library;
    library.schemaVersion = item.value("schemaVersion", 1);
    if (item.contains("groups")) {
        for (const auto& group : item["groups"]) {
            Group row;
            row.id = group.value("id", "");
            row.name = group.value("name", "");
            row.color = group.value("color", "#98A1B0");
            if (group.contains("itemIds")) row.itemIds = group["itemIds"].get<std::vector<std::string>>();
            library.groups.push_back(std::move(row));
        }
    }
    if (item.contains("items")) {
        for (const auto& entry : item["items"]) {
            library.items.push_back(Shortcut{entry.value("id", ""), entry.value("title", ""), entry.value("path", ""), entry.value("kind", "file")});
        }
    }
    return library;
}

json ClipsJson(const std::vector<Clip>& clips) {
    json list = json::array();
    for (const Clip& clip : clips) list.push_back(json{{"id", clip.id}, {"kind", clip.kind}, {"text", clip.text}, {"createdAt", clip.createdAt}, {"favorite", clip.favorite}});
    return list;
}

std::vector<Clip> ClipsFrom(const json& item) {
    std::vector<Clip> clips;
    if (!item.is_array()) return clips;
    for (const auto& entry : item) clips.push_back(Clip{entry.value("id", ""), entry.value("kind", "text"), entry.value("text", ""), entry.value("createdAt", ""), entry.value("favorite", false)});
    return clips;
}

template <typename T>
T ReadJson(const std::string& name, T fallback, T (*convert)(const json&)) {
    const std::string text = ReadTextFile(DataFile(name));
    if (text.empty()) return fallback;
    try {
        return convert(json::parse(text));
    } catch (...) {
        return fallback;
    }
}

Library DefaultLibrary() {
    Library library;
    library.groups.push_back(Group{"ungrouped", "未分组", "#98A1B0", {}});
    return library;
}

bool LooksLikeExpression(const std::string& text) {
    bool op = false;
    bool digit = false;
    for (char ch : text) {
        if (ch == '+' || ch == '-' || ch == '*' || ch == '/' || ch == '(' || ch == ')') op = true;
        if (ch >= '0' && ch <= '9') digit = true;
    }
    return op && digit;
}

bool AllDigits(const std::string& text) {
    return !text.empty() && std::all_of(text.begin(), text.end(), [](char ch) { return ch >= '0' && ch <= '9'; });
}

bool AllHex(const std::string& text) {
    return std::all_of(text.begin(), text.end(), [](char ch) {
        return (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
    });
}

Hit ToolHit(const std::string& id, const std::string& title, const std::string& subtitle, const std::string& toolId, const std::optional<std::string>& copy, int score) {
    Hit hit;
    hit.id = id;
    hit.kind = "tool";
    hit.title = title;
    hit.subtitle = subtitle;
    hit.toolId = toolId;
    hit.copyText = copy;
    hit.score = score;
    return hit;
}

std::string RecentPathKey(const std::string& path) { return "path:" + path; }

}  // namespace

const std::vector<ToolSpec>& ToolCatalog() {
    static const std::vector<ToolSpec> tools = {
        {"clipboard", "剪贴板", {"clip", "history", "复制"}},
        {"picker", "截图取色", {"color", "picker", "取色"}},
        {"translate", "翻译", {"fy", "translate"}},
        {"calc", "计算器", {"js", "calc", "算式"}},
        {"timestamp", "时间戳", {"ts", "time", "日期"}},
        {"unit", "单位换算", {"unit", "km", "cny"}},
        {"color", "颜色转换", {"hex", "rgb", "hsl"}},
        {"qr", "二维码", {"qr"}}};
    return tools;
}

std::string ToolTitle(const std::string& id) {
    for (const ToolSpec& tool : ToolCatalog()) {
        if (tool.id == id) return tool.title;
    }
    return id;
}

Session::Session(ContractStore store) : store(std::move(store)), machine(this->store, LogInfo) {
    onChange = [] {};
    Load();
    LogInfo("契约已加载，哈希 " + this->store.Hash());
}

void Session::SetIndex(std::vector<Hit> next) {
    index = std::move(next);
    searchRows_.clear();
    for (const Hit& hit : index) searchRows_.push_back(IndexRow{hit.id, hit.kind, hit.title, hit.keywords});
}

void Session::Load() {
    settings = ReadJson<Settings>("settings.json", Settings{}, SettingsFrom);
    if (settings.backgroundOpacity == 0) settings.backgroundOpacity = 1;
    settings.backgroundOpacity = std::min(1.0, std::max(0.15, settings.backgroundOpacity));
    if (settings.toolOrder.empty()) {
        for (const ToolSpec& tool : ToolCatalog()) settings.toolOrder.push_back(tool.id);
        settings.toolOrder.push_back("add");
    }
    library = ReadJson<Library>("groups.json", DefaultLibrary(), LibraryFrom);
    if (library.groups.empty()) library = DefaultLibrary();
    clips = ReadJson<std::vector<Clip>>("clipboard.json", {}, ClipsFrom);
    recents = ReadJson<std::vector<std::string>>("recents.json", {}, [](const json& item) { return item.get<std::vector<std::string>>(); });
    if (PurgeReservedNavGroup()) WriteAtomic(DataFile("groups.json"), LibraryJson(library).dump(2));
    SeedStarterIfNeeded();
}

void Session::Save() {
    WriteAtomic(DataFile("settings.json"), SettingsJson(settings).dump(2));
    WriteAtomic(DataFile("groups.json"), LibraryJson(library).dump(2));
    WriteAtomic(DataFile("clipboard.json"), ClipsJson(clips).dump(2));
    WriteAtomic(DataFile("recents.json"), json(recents).dump(2));
}

void Session::SeedStarterIfNeeded() {
    if (settings.starterSeeded) return;
    if (library.items.empty()) {
        const int added = ApplyStarter(false);
        LogInfo("初始快捷启动已放入 " + std::to_string(added) + " 项");
    }
    settings.starterSeeded = true;
    Save();
}

int Session::ImportStarterTools() {
    const int added = ApplyStarter(true);
    settings.starterSeeded = true;
    Save();
    onChange();
    LogInfo(added == 0 ? "系统工具和已安装应用都已经在快捷启动里" : "已放入 " + std::to_string(added) + " 个快捷项");
    return added;
}

int Session::ApplyStarter(bool merge) {
    const StarterCatalog catalog = store.Starter();
    std::vector<StarterQuery> queries;
    for (const StarterEntry& item : catalog.items) {
        queries.push_back(StarterQuery{item.id, item.group, item.windows.title, item.windows.paths, item.windows.names});
    }
    std::vector<StarterHit> hits = FindStarter(queries);
    std::vector<std::string> taken;
    for (const StarterHit& hit : hits) taken.push_back(hit.path);
    for (const Shortcut& item : library.items) taken.push_back(item.path);
    for (const StarterHit& extra : InstalledApps(taken)) hits.push_back(extra);
    std::vector<StarterFound> found;
    for (const StarterHit& hit : hits) found.push_back(StarterFound{hit.id, hit.title, hit.path, hit.groupId});
    const StarterBuilt built = ComposeStarter(catalog.groups, found, catalog.ungrouped);
    if (!merge) {
        library.schemaVersion = 1;
        library.groups.clear();
        library.items.clear();
        for (const GroupRecord& group : built.groups) library.groups.push_back(Group{group.id, group.name, group.color, group.itemIds});
        for (const StarterShortcut& item : built.items) library.items.push_back(Shortcut{item.id, item.title, item.path, item.kind});
        return static_cast<int>(library.items.size());
    }
    for (const GroupRecord& group : built.groups) {
        if (group.id == catalog.ungrouped.id) continue;
        if (std::any_of(library.groups.begin(), library.groups.end(), [&](const Group& existing) { return existing.id == group.id; })) continue;
        const auto ungrouped = std::find_if(library.groups.begin(), library.groups.end(), [&](const Group& existing) { return existing.id == catalog.ungrouped.id; });
        Group created{group.id, group.name, group.color, {}};
        if (ungrouped == library.groups.end()) library.groups.push_back(created);
        else library.groups.insert(ungrouped, created);
    }
    std::vector<std::string> known;
    for (const Shortcut& item : library.items) known.push_back(LowerAscii(item.path));
    int added = 0;
    for (const StarterShortcut& item : built.items) {
        if (std::find(known.begin(), known.end(), LowerAscii(item.path)) != known.end()) continue;
        const GroupRecord* owner = nullptr;
        for (const GroupRecord& group : built.groups) {
            if (std::find(group.itemIds.begin(), group.itemIds.end(), item.id) != group.itemIds.end()) owner = &group;
        }
        if (!owner) continue;
        const bool duplicateId = std::any_of(library.items.begin(), library.items.end(), [&](const Shortcut& existing) { return existing.id == item.id; });
        const std::string id = duplicateId ? NewId() : item.id;
        library.items.push_back(Shortcut{id, item.title, item.path, item.kind});
        const auto index = std::find_if(library.groups.begin(), library.groups.end(), [&](const Group& existing) { return existing.id == owner->id; });
        if (index != library.groups.end()) index->itemIds.push_back(id);
        known.push_back(LowerAscii(item.path));
        ++added;
    }
    return added;
}

void Session::NoteClipboard(const std::string& text, const std::vector<std::string>& files) {
    if (settings.clipboardPaused) return;
    std::string body;
    std::string kind;
    if (!text.empty()) {
        body = text;
        kind = "text";
    } else if (!files.empty()) {
        for (size_t i = 0; i < files.size(); ++i) {
            if (i) body += "\n";
            body += files[i];
        }
        kind = "file";
    } else {
        return;
    }
    if (!clips.empty() && clips[0].text == body) return;
    clips.insert(clips.begin(), Clip{NewId(), kind, body, UtcNowIso(), false});
    if (clips.size() > 200) clips.resize(200);
    Save();
    PruneClips();
    LogInfo("记录剪贴板 " + kind + "，长度 " + std::to_string(body.size()));
    onChange();
}

void Session::Search() {
    const auto parsed = [&]() -> std::optional<std::tuple<std::string, std::string, std::string>> {
        const auto space = query.find(' ');
        if (space == std::string::npos || space == 0) return std::nullopt;
        const std::string word = query.substr(0, space);
        const auto found = ResolveTriggerWord(word);
        if (!found) return std::nullopt;
        return std::tuple{word, query.substr(space + 1), found->toolId};
    }();
    if (parsed) {
        triggerWord = std::get<0>(*parsed);
        payload = std::get<1>(*parsed);
        if (machine.State() != "TOOL") Go("TOOL");
        hits = ToolModeHits(std::get<2>(*parsed), payload);
        selection = 0;
        return;
    }
    triggerWord.clear();
    payload.clear();
    std::string trimmed = query;
    size_t begin = 0;
    size_t end = trimmed.size();
    while (begin < end && std::isspace(static_cast<unsigned char>(trimmed[begin]))) ++begin;
    while (end > begin && std::isspace(static_cast<unsigned char>(trimmed[end - 1]))) --end;
    trimmed = trimmed.substr(begin, end - begin);
    if (trimmed.empty()) {
        hits.clear();
        if (machine.State() != "HIDDEN") Go("A1");
        return;
    }
    std::vector<Hit> found;
    if (LooksLikeExpression(trimmed)) {
        const Result value = EvaluateExpression(trimmed);
        if (value.ok) found.push_back(ToolHit("calc", "= " + value.data, "计算器", "calc", value.data, 100));
    }
    if (const auto unit = ConvertUnit(trimmed)) found.push_back(ToolHit("unit", *unit, "单位换算", "unit", *unit, 95));
    if ((trimmed.rfind('#', 0) == 0 || (trimmed.size() == 6 && AllHex(trimmed))) && DescribeColor(trimmed)) {
        const std::string color = *DescribeColor(trimmed);
        found.push_back(ToolHit("color", color, "颜色转换", "color", color, 90));
    }
    if (AllDigits(trimmed) && (trimmed.size() == 10 || trimmed.size() == 13) && DescribeTimestamp(trimmed)) {
        const std::string time = *DescribeTimestamp(trimmed);
        found.push_back(ToolHit("ts", time, "时间戳", "timestamp", time, 90));
    }
    const std::vector<std::string> ids = MatchKeywords(searchRows_, trimmed);
    std::vector<Hit> matched;
    for (const Hit& hit : index) {
        if (std::find(ids.begin(), ids.end(), hit.id) != ids.end()) matched.push_back(hit);
    }
    std::sort(matched.begin(), matched.end(), [](const Hit& left, const Hit& right) { return left.score > right.score; });
    for (const char* kind : {"tool", "app", "file", "url"}) {
        for (const Hit& hit : matched) {
            if (hit.kind == kind) found.push_back(hit);
        }
    }
    hits = std::move(found);
    LogInfo("搜索「" + trimmed + "」命中 " + std::to_string(hits.size()) + " 条，不截断");
    selection = 0;
    Go(hits.empty() ? "A4" : "A3");
}

std::vector<Hit> Session::RecentHits() const {
    std::vector<Hit> rows;
    for (const std::string& id : recents) {
        if (auto hit = TryResolveRecent(id)) rows.push_back(*hit);
        if (rows.size() >= 6) break;
    }
    return rows;
}

void Session::TouchRecentFromPath(const std::string& path, const std::string&, const std::string&) {
    if (path.find_first_not_of(' ') == std::string::npos) return;
    for (const Hit& item : index) {
        if (EqualsInsensitive(item.path, path)) {
            Remember(item.id);
            return;
        }
    }
    Remember(RecentPathKey(path));
}

std::optional<Hit> Session::TryResolveRecent(const std::string& id) const {
    for (const Hit& item : index) {
        if (item.id == id) return item;
    }
    if (id.rfind("path:", 0) == 0) {
        const std::string path = id.substr(5);
        for (const Hit& item : index) {
            if (EqualsInsensitive(item.path, path)) return item;
        }
        if (path.empty()) return std::nullopt;
        Hit hit;
        if (path.rfind("http://", 0) == 0 || path.rfind("https://", 0) == 0) {
            hit.id = "url-" + path;
            hit.kind = "url";
            hit.title = path;
            hit.subtitle = "网址";
            hit.path = path;
            hit.score = 20;
            return hit;
        }
        const auto slash = path.find_last_of("\\/");
        std::string title = slash == std::string::npos ? path : path.substr(slash + 1);
        const auto dot = title.find_last_of('.');
        if (dot != std::string::npos) title = title.substr(0, dot);
        if (title.empty()) title = path;
        const bool app = EqualsInsensitive(path.size() > 4 ? path.substr(path.size() - 4) : "", ".lnk") || EqualsInsensitive(path.size() > 4 ? path.substr(path.size() - 4) : "", ".exe");
        hit.id = RecentPathKey(path);
        hit.kind = app ? "app" : "file";
        hit.title = title;
        hit.subtitle = app ? "应用" : "文件";
        hit.path = path;
        hit.score = 20;
        return hit;
    }
    const Shortcut* shortcut = nullptr;
    for (const Shortcut& item : library.items) {
        if (item.id == id) shortcut = &item;
    }
    if (!shortcut) return std::nullopt;
    for (const Hit& item : index) {
        if (EqualsInsensitive(item.path, shortcut->path)) return item;
    }
    Hit hit;
    hit.id = shortcut->id;
    hit.kind = shortcut->kind;
    hit.title = shortcut->title;
    hit.subtitle = shortcut->kind == "url" ? "网址" : shortcut->kind == "app" ? "应用" : "文件";
    hit.path = shortcut->path;
    hit.score = 20;
    return hit;
}

void Session::Execute(const Hit& hit) {
    if (hit.copyText) {
        CopyText(*hit.copyText);
        LogInfo("已复制结果");
    } else if (hit.kind == "tool" && !hit.toolId.empty()) {
        LogInfo("打开工具 " + hit.toolId);
        if (onOpenTool) onOpenTool(hit.toolId);
        return;
    } else if (!hit.path.empty()) {
        OpenTarget(hit.path);
        LogInfo("已打开 " + hit.title);
    }
    Remember(hit.id);
    Hide();
}

void Session::ExecuteSelection() {
    if (hits.empty()) return;
    const int index = std::clamp(selection, 0, static_cast<int>(hits.size()) - 1);
    Execute(hits[index]);
}

void Session::Escape() {
    machine.Escape();
    if (machine.State() == "HIDDEN") {
        if (onHide) onHide();
    } else if (onChange) {
        onChange();
    }
}

void Session::Summon() {
    if (machine.State() == "HIDDEN") {
        shownAt_ = NowMs();
        machine.Transition("A1");
        query.clear();
        triggerWord.clear();
        payload.clear();
        hits.clear();
        LogInfo("呼出浮层");
        if (onShow) onShow();
    } else if (NowMs() - shownAt_ < 160) {
        LogInfo("忽略同一次按键带出的重复呼出");
    } else {
        LogInfo("再次按下热键，收起浮层");
        Hide();
    }
}

void Session::Hide() {
    if (machine.State() != "HIDDEN") machine.Transition("HIDDEN");
    LogInfo("隐藏浮层");
    if (onHide) onHide();
}

void Session::ReorderGroup(const std::string& sourceId, const std::string& targetId, bool placeAfter) {
    if (sourceId == targetId) return;
    const auto from = std::find_if(library.groups.begin(), library.groups.end(), [&](const Group& group) { return group.id == sourceId; });
    auto to = std::find_if(library.groups.begin(), library.groups.end(), [&](const Group& group) { return group.id == targetId; });
    if (from == library.groups.end() || to == library.groups.end()) return;
    Group group = *from;
    const auto fromIndex = from - library.groups.begin();
    library.groups.erase(from);
    to = std::find_if(library.groups.begin(), library.groups.end(), [&](const Group& item) { return item.id == targetId; });
    auto insert = to - library.groups.begin();
    if (placeAfter) ++insert;
    if (fromIndex < static_cast<long long>(insert) && !placeAfter) {
        // 删除源之后，目标下标已经减过。placeAfter 的加一在新数组上计算。
    }
    library.groups.insert(library.groups.begin() + insert, group);
    Save();
    LogInfo("分组已移动到「" + library.groups[insert].name + "」位置");
    onChange();
}

void Session::MoveShortcut(const std::string& itemId, const std::string& sourceGroupId, const std::string& targetGroupId) {
    if (sourceGroupId == targetGroupId) return;
    auto source = std::find_if(library.groups.begin(), library.groups.end(), [&](const Group& group) { return group.id == sourceGroupId; });
    auto target = std::find_if(library.groups.begin(), library.groups.end(), [&](const Group& group) { return group.id == targetGroupId; });
    if (source == library.groups.end() || target == library.groups.end()) return;
    if (std::find(source->itemIds.begin(), source->itemIds.end(), itemId) == source->itemIds.end()) return;
    source->itemIds.erase(std::remove(source->itemIds.begin(), source->itemIds.end(), itemId), source->itemIds.end());
    if (std::find(target->itemIds.begin(), target->itemIds.end(), itemId) == target->itemIds.end()) target->itemIds.push_back(itemId);
    Save();
    onChange();
}

void Session::ReorderShortcut(const std::string& itemId, const std::string& groupId, const std::string& beforeId, bool placeAfter) {
    if (itemId == beforeId) return;
    auto group = std::find_if(library.groups.begin(), library.groups.end(), [&](const Group& item) { return item.id == groupId; });
    if (group == library.groups.end()) return;
    auto& ids = group->itemIds;
    const auto from = std::find(ids.begin(), ids.end(), itemId);
    if (from == ids.end()) return;
    ids.erase(from);
    const auto anchor = std::find(ids.begin(), ids.end(), beforeId);
    if (anchor == ids.end()) return;
    auto insert = anchor;
    if (placeAfter) ++insert;
    ids.insert(insert, itemId);
    Save();
    LogInfo("快捷项已在分组内调整顺序");
    onChange();
}

void Session::RenameGroup(const std::string& id, const std::string& name) {
    std::string trimmed = name;
    size_t begin = 0;
    size_t end = trimmed.size();
    while (begin < end && std::isspace(static_cast<unsigned char>(trimmed[begin]))) ++begin;
    while (end > begin && std::isspace(static_cast<unsigned char>(trimmed[end - 1]))) --end;
    trimmed = trimmed.substr(begin, end - begin);
    auto group = std::find_if(library.groups.begin(), library.groups.end(), [&](const Group& item) { return item.id == id; });
    if (group == library.groups.end() || trimmed.empty()) {
        LogWarn("分组名称不能为空");
        return;
    }
    if (group->name == trimmed) return;
    group->name = trimmed;
    Save();
    LogInfo("分组已重命名为「" + trimmed + "」");
    onChange();
}

void Session::DeleteGroupById(const std::string& id) {
    // 「全部」只是左侧的总览，不是一个分组。其余分组，包括改过名的默认分组，都可以删。
    if (id.empty() || id == "all" || id == "fav") return;
    const auto group = std::find_if(library.groups.begin(), library.groups.end(), [&](const Group& item) { return item.id == id; });
    if (group == library.groups.end()) {
        LogWarn("分组不存在");
        return;
    }
    const std::string name = group->name;
    const std::vector<std::string> removing = group->itemIds;
    library.groups.erase(group);
    for (Group& entry : library.groups) {
        entry.itemIds.erase(std::remove_if(entry.itemIds.begin(), entry.itemIds.end(), [&](const std::string& itemId) {
                                return std::find(removing.begin(), removing.end(), itemId) != removing.end();
                            }),
                            entry.itemIds.end());
    }
    library.items.erase(std::remove_if(library.items.begin(), library.items.end(), [&](const Shortcut& item) {
                            return std::find(removing.begin(), removing.end(), item.id) != removing.end();
                        }),
                        library.items.end());
    Save();
    LogInfo("已删除分组「" + name + "」，并移除 " + std::to_string(removing.size()) + " 个快捷项");
    onChange();
}

bool Session::AddShortcut(const std::string& path, const std::string& groupId) {
    std::string full = path;
    while (!full.empty() && (full.back() == '\\' || full.back() == '/')) full.pop_back();
    // 路径是 UTF-8。先转成宽字符，再问文件系统它在不在。
    const int wideCount = MultiByteToWideChar(CP_UTF8, 0, full.data(), static_cast<int>(full.size()), nullptr, 0);
    std::wstring wide(wideCount, 0);
    MultiByteToWideChar(CP_UTF8, 0, full.data(), static_cast<int>(full.size()), wide.data(), wideCount);
    std::error_code error;
    const bool directory = fs::is_directory(wide, error);
    const bool exists = directory || fs::is_regular_file(wide, error);
    if (!exists) {
        LogWarn("快捷项路径不存在 " + full);
        return false;
    }
    std::string extension;
    const auto dot = full.find_last_of('.');
    if (dot != std::string::npos) extension = LowerAscii(full.substr(dot));
    const std::string kind = !directory && (extension == ".exe" || extension == ".lnk") ? "app" : "file";
    const auto slash = full.find_last_of("\\/");
    std::string title = slash == std::string::npos ? full : full.substr(slash + 1);
    if (kind == "app") {
        const auto ext = title.find_last_of('.');
        if (ext != std::string::npos) title = title.substr(0, ext);
    }
    const std::string label = kind == "app" ? "应用" : directory ? "文件夹" : "文件";
    return PutShortcut(full, title.empty() ? full : title, kind, label, groupId);
}

bool Session::AddUrl(const std::string& url, const std::string& groupId) {
    std::string text = url;
    size_t begin = 0;
    while (begin < text.size() && std::isspace(static_cast<unsigned char>(text[begin]))) ++begin;
    text = text.substr(begin);
    if (text.rfind("http://", 0) != 0 && text.rfind("https://", 0) != 0) {
        LogWarn("不能放入快捷启动 " + text);
        return false;
    }
    std::string host = text;
    const auto scheme = text.find("://");
    if (scheme != std::string::npos) {
        const auto slash = text.find('/', scheme + 3);
        host = text.substr(scheme + 3, slash == std::string::npos ? std::string::npos : slash - scheme - 3);
    }
    return PutShortcut(text, host.empty() ? text : host, "url", "网址", groupId);
}

bool Session::RemoveShortcut(const std::string& itemId) {
    for (Group& group : library.groups) {
        group.itemIds.erase(std::remove(group.itemIds.begin(), group.itemIds.end(), itemId), group.itemIds.end());
    }
    const auto before = library.items.size();
    library.items.erase(std::remove_if(library.items.begin(), library.items.end(), [&](const Shortcut& item) { return item.id == itemId; }), library.items.end());
    if (library.items.size() == before) return false;
    Save();
    onChange();
    return true;
}

std::optional<TriggerDef> Session::ResolveTriggerWord(const std::string& word) const {
    for (const auto& [toolId, renamed] : settings.triggerWords) {
        if (EqualsInsensitive(renamed, word)) {
            for (const TriggerDef& item : store.Triggers()) {
                if (item.toolId == toolId) return item;
            }
        }
    }
    const auto found = store.Trigger(word);
    if (!found) return std::nullopt;
    const auto renamed = settings.triggerWords.find(found->toolId);
    if (renamed != settings.triggerWords.end() && !EqualsInsensitive(renamed->second, word)) return std::nullopt;
    return found;
}

void Session::PruneClips() {
    std::optional<double> days = 7;
    if (settings.clipboardRetention == "30d") days = 30;
    else if (settings.clipboardRetention == "forever") days = std::nullopt;
    if (!days) return;
    const uint64_t cutoff = NowMs() - static_cast<uint64_t>(*days * 24 * 60 * 60 * 1000);
    const size_t before = clips.size();
    clips.erase(std::remove_if(clips.begin(), clips.end(), [&](const Clip& clip) {
                    if (clip.createdAt.size() < 19) return false;
                    SYSTEMTIME system{};
                    if (std::sscanf(clip.createdAt.c_str(), "%hu-%hu-%huT%hu:%hu:%hu", &system.wYear, &system.wMonth, &system.wDay, &system.wHour, &system.wMinute, &system.wSecond) != 6) {
                        return false;
                    }
                    FILETIME file{};
                    if (!SystemTimeToFileTime(&system, &file)) return false;
                    ULARGE_INTEGER stamp;
                    stamp.LowPart = file.dwLowDateTime;
                    stamp.HighPart = file.dwHighDateTime;
                    const uint64_t unixMs = (stamp.QuadPart - 116444736000000000ULL) / 10000ULL;
                    return unixMs < cutoff;
                }),
                clips.end());
    if (clips.size() != before) {
        Save();
        LogInfo("按保留策略清理剪贴板，剩余 " + std::to_string(clips.size()) + " 条");
    }
}

void Session::Enter(const std::string& next) { machine.Reach(next); }

void Session::Remember(const std::string& id) {
    recents.erase(std::remove(recents.begin(), recents.end(), id), recents.end());
    recents.insert(recents.begin(), id);
    if (recents.size() > 20) recents.resize(20);
    Save();
}

void Session::Go(const std::string& next) { machine.Reach(next); }

std::vector<Hit> Session::ToolModeHits(const std::string& toolId, const std::string& payloadText) const {
    if (toolId == "translate") {
        const auto translated = TranslateText(payloadText);
        return {ToolHit("tr", translated.first, store.Text("translate.offline"), "translate", translated.first, 100)};
    }
    if (toolId == "calc") {
        if (const auto unit = ConvertUnit(payloadText)) return {ToolHit("unit", *unit, "单位换算", "unit", *unit, 100)};
        const Result value = EvaluateExpression(payloadText);
        return {ToolHit("calc", value.ok ? "= " + value.data : value.message, "计算器", "calc", value.ok ? std::optional<std::string>(value.data) : std::nullopt, 100)};
    }
    if (toolId == "color") {
        const std::string color = DescribeColor(payloadText).value_or("无法识别颜色");
        return {ToolHit("color", color, "颜色转换", "color", color, 100)};
    }
    if (toolId == "timestamp") {
        const std::string time = DescribeTimestamp(payloadText).value_or("无法识别时间");
        return {ToolHit("ts", time, "时间戳", "timestamp", time, 100)};
    }
    if (toolId == "clipboard") {
        std::vector<Hit> rows;
        for (const Clip& clip : clips) {
            if (!payloadText.empty() && !ContainsInsensitive(clip.text, payloadText)) continue;
            rows.push_back(ToolHit(clip.id, clip.text, "剪贴板", "clipboard", clip.text, 80));
            if (rows.size() >= 8) break;
        }
        return rows;
    }
    return {};
}

std::string Session::ResolveGroup(const std::string& groupId) {
    if (std::any_of(library.groups.begin(), library.groups.end(), [&](const Group& group) { return group.id == groupId; })) return groupId;
    if (std::any_of(library.groups.begin(), library.groups.end(), [](const Group& group) { return group.id == "ungrouped"; })) return "ungrouped";
    // 没有「未分组」时不要塞进列表里的第一组。那样从「全部」拖进去的东西会跑错地方。
    library.groups.push_back(Group{"ungrouped", "未分组", "#98A1B0", {}});
    return "ungrouped";
}

bool Session::PutShortcut(const std::string& stored, const std::string& title, const std::string& kind, const std::string&, const std::string& groupId) {
    const std::string target = ResolveGroup(groupId);
    const auto existing = std::find_if(library.items.begin(), library.items.end(), [&](const Shortcut& item) { return EqualsInsensitive(item.path, stored); });
    auto group = std::find_if(library.groups.begin(), library.groups.end(), [&](const Group& item) { return item.id == target; });
    if (group == library.groups.end()) return false;
    if (existing != library.items.end()) {
        if (std::find(group->itemIds.begin(), group->itemIds.end(), existing->id) != group->itemIds.end()) {
            LogInfo("快捷项已在这个分组里，跳过 " + existing->title);
            return false;
        }
        group->itemIds.push_back(existing->id);
        Save();
        onChange();
        return true;
    }
    const std::string id = NewId();
    library.items.push_back(Shortcut{id, title, stored, kind});
    group->itemIds.push_back(id);
    Save();
    onChange();
    LogInfo("已添加快捷项 " + title + " 到「" + group->name + "」");
    return true;
}

bool Session::PurgeReservedNavGroup() {
    const auto index = std::find_if(library.groups.begin(), library.groups.end(), [](const Group& group) { return group.id == "fav"; });
    if (index == library.groups.end()) return false;
    const std::vector<std::string> orphaned = index->itemIds;
    library.groups.erase(index);
    if (orphaned.empty()) return true;
    auto ungrouped = std::find_if(library.groups.begin(), library.groups.end(), [](const Group& group) { return group.id == "ungrouped"; });
    if (ungrouped == library.groups.end()) {
        library.groups.push_back(Group{"ungrouped", "未分组", "#98A1B0", orphaned});
        return true;
    }
    for (const std::string& itemId : orphaned) {
        if (std::find(ungrouped->itemIds.begin(), ungrouped->itemIds.end(), itemId) == ungrouped->itemIds.end()) ungrouped->itemIds.push_back(itemId);
    }
    return true;
}

std::vector<Hit> ToolHits() {
    std::vector<Hit> hits;
    for (const ToolSpec& tool : ToolCatalog()) {
        Hit hit;
        hit.id = "tool-" + tool.id;
        hit.kind = "tool";
        hit.title = tool.title;
        hit.subtitle = "内置工具";
        hit.toolId = tool.id;
        hit.score = 40;
        hit.keywords = tool.words;
        hit.keywords.push_back(tool.title);
        for (const std::string& variant : PinyinVariants(tool.title)) hit.keywords.push_back(variant);
        std::sort(hit.keywords.begin(), hit.keywords.end());
        hit.keywords.erase(std::unique(hit.keywords.begin(), hit.keywords.end()), hit.keywords.end());
        hits.push_back(std::move(hit));
    }
    return hits;
}

}  // namespace dk
