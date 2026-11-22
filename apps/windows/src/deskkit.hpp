#pragma once

// DeskKit Windows 原生壳的公共类型。
// 业务结果、契约、状态机、搜索和分组在 core；热键、索引、拼音在 platform；
// 一次呼出的数据在 session；ImGui 窗口在 ui。

#include <cstdint>
#include <functional>
#include <optional>
#include <string>
#include <unordered_map>
#include <utility>
#include <vector>

namespace dk {

struct Result {
    bool ok = false;
    std::string code;
    std::string message;
    std::string data;
    static Result Success(std::string data = {});
    static Result Fail(std::string code, std::string message);
};

struct TriggerDef {
    std::string word;
    std::string toolId;
    std::string label;
    std::string placeholder;
};

struct PermissionDef {
    std::string id;
    std::string title;
    std::string reason;
};

struct StagedDiff {
    std::string id;
    std::string category;
    std::string owner;
    std::string dueAt;
    std::string parityPlan;
};

struct StarterSide {
    std::string title;
    std::vector<std::string> paths;
    std::vector<std::string> names;
};

struct StarterEntry {
    std::string id;
    std::string group;
    StarterSide windows;
    StarterSide macos;
};

struct StarterGroupSpec {
    std::string id;
    std::string name;
    std::string color;
};

struct StarterCatalog {
    StarterGroupSpec ungrouped;
    std::vector<StarterGroupSpec> groups;
    std::vector<StarterEntry> items;
};

struct StarterFound {
    std::string id;
    std::string title;
    std::string path;
    std::string groupId;
};

struct GroupRecord {
    std::string id;
    std::string name;
    std::string color;
    std::vector<std::string> itemIds;
};

struct ItemRecord {
    std::string id;
    std::string title;
};

struct StarterShortcut {
    std::string id;
    std::string title;
    std::string path;
    std::string kind;
};

struct StarterBuilt {
    std::vector<GroupRecord> groups;
    std::vector<StarterShortcut> items;
};

struct IndexRow {
    std::string id;
    std::string kind;
    std::string title;
    std::vector<std::string> keywords;
};

class ContractStore {
public:
    explicit ContractStore(std::string root);
    const std::string& Root() const { return root_; }
    const std::string& Hash() const { return hash_; }

    static std::optional<std::string> Locate(const std::string& start = {});
    static std::string HashTree(const std::string& root);

    std::string Text(const std::string& key) const;
    std::string Text(const std::string& key, const std::vector<std::pair<std::string, std::string>>& values) const;
    std::string Color(const std::string& name, bool dark = false) const;
    std::vector<std::string> GroupColors() const;
    double Layout(const std::vector<std::string>& path) const;
    std::string KeyLabel(const std::string& platform, const std::string& key) const;
    std::vector<std::string> ChordParts(const std::string& action) const;
    std::string FormatChord(const std::string& platform, const std::string& action) const;
    std::vector<TriggerDef> Triggers() const;
    std::optional<TriggerDef> Trigger(const std::string& word) const;
    StarterCatalog Starter() const;
    std::vector<PermissionDef> Permissions(const std::string& platform) const;
    std::vector<StagedDiff> StagedDiffs() const;
    std::unordered_map<std::string, std::vector<std::string>> Transitions() const;
    std::unordered_map<std::string, std::vector<std::string>> EscMap() const;
    std::string FixtureText(const std::string& name) const;

private:
    struct Impl;
    std::string root_;
    std::string hash_;
    Impl* impl_;
};

class StateMachine {
public:
    StateMachine(const ContractStore& store, std::function<void(const std::string&)> log = {});
    const std::string& State() const { return state_; }
    bool Reach(const std::string& next);
    Result Transition(const std::string& next);
    Result Escape();
    std::vector<std::string> EscapeSequence() const;

private:
    StateMachine(std::unordered_map<std::string, std::vector<std::string>> transitions,
                 std::unordered_map<std::string, std::vector<std::string>> esc,
                 std::string state);
    std::optional<std::vector<std::string>> ShortestPath(const std::string& next) const;

    std::unordered_map<std::string, std::vector<std::string>> transitions_;
    std::unordered_map<std::string, std::vector<std::string>> esc_;
    std::string state_ = "HIDDEN";
    std::function<void(const std::string&)> log_;
};

struct Rgb {
    int r = 0;
    int g = 0;
    int b = 0;
};

Result EvaluateExpression(const std::string& input);
std::string FormatNumber(double value);
std::optional<std::string> ConvertUnit(const std::string& input);
std::optional<Rgb> ParseColor(const std::string& input);
std::optional<std::string> DescribeColor(const std::string& input);
std::optional<std::string> DescribeTimestamp(const std::string& input);
std::pair<std::string, bool> TranslateText(const std::string& input);
std::vector<std::string> MatchKeywords(const std::vector<IndexRow>& rows, const std::string& query);
std::optional<std::string> ResolveTrigger(const ContractStore& store, const std::string& word);
Result DeleteGroup(std::vector<GroupRecord>& groups, std::vector<ItemRecord>& items, const std::string& groupId, const std::string& ungroupedId);
StarterBuilt ComposeStarter(const std::vector<StarterGroupSpec>& groups, const std::vector<StarterFound>& found, const StarterGroupSpec& ungrouped);
std::string KindOfPath(const std::string& path);

void LogInfo(const std::string& message);
void LogWarn(const std::string& message);
std::string DataRoot();
std::string DataFile(const std::string& name);
std::string ReadTextFile(const std::string& path);
void WriteAtomic(const std::string& path, const std::string& text);
std::string NewId();
std::string ExePath();
double WorkingSetMb();
bool IsSystemDark();
void SetAutoStart(bool enabled);
void OpenTarget(const std::string& path);
void RevealTarget(const std::string& path);
void CopyText(const std::string& text);
uint32_t ClipboardSequence();
struct ClipboardSnapshot {
    std::string text;
    std::vector<std::string> files;
};
ClipboardSnapshot ReadClipboard();
void ReleaseAlt();
bool IsLeftButtonDown();
bool IsAltDown();
int ToVirtualKey(int keyCode, bool recordedOnWindows);
std::string Sha256Hex(const std::string& text);
std::string Sha256FilePrefix(const std::string& text, int hexChars);

struct IndexedEntry {
    std::string id;
    std::string kind;
    std::string title;
    std::string subtitle;
    std::string path;
    std::vector<std::string> keywords;
    int score = 0;
};

std::vector<IndexedEntry> CollectIndex();
void LoadPinyinTable(const std::string& path);
std::vector<std::string> PinyinVariants(const std::string& text);

struct StarterQuery {
    std::string id;
    std::string groupId;
    std::string title;
    std::vector<std::string> paths;
    std::vector<std::string> names;
};

struct StarterHit {
    std::string id;
    std::string groupId;
    std::string title;
    std::string path;
};

std::vector<StarterHit> FindStarter(const std::vector<StarterQuery>& items);
std::vector<StarterHit> InstalledApps(const std::vector<std::string>& takenPaths);

struct MonitorBox {
    int x = 0;
    int y = 0;
    int w = 0;
    int h = 0;
};
MonitorBox MonitorAtCursor();
MonitorBox PrimaryMonitor();
std::string ScreenPixelHex(int x, int y);

struct Clip {
    std::string id;
    std::string kind = "text";
    std::string text;
    std::string createdAt;
    bool favorite = false;
};

struct Shortcut {
    std::string id;
    std::string title;
    std::string path;
    std::string kind = "file";
};

struct Group {
    std::string id;
    std::string name;
    std::string color = "#98A1B0";
    std::vector<std::string> itemIds;
};

struct Library {
    int schemaVersion = 1;
    std::vector<Group> groups;
    std::vector<Shortcut> items;
};

struct HotkeyBinding {
    int keyCode = 0;
    bool alt = false;
    bool command = false;
    bool control = false;
    bool shift = false;
    std::string text;
};

struct Settings {
    int schemaVersion = 1;
    bool autoLaunch = false;
    std::string summonMode = "hotkey";
    bool blurHide = true;
    std::string multiScreen = "followCursor";
    std::string theme = "light";
    std::string density = "standard";
    std::string accentToken = "accent";
    std::string backgroundImagePath;
    double backgroundOpacity = 1;
    std::string clipboardRetention = "7d";
    bool clipboardPaused = false;
    bool cloudSync = false;
    bool autoUpdate = true;
    bool guideDone = false;
    bool starterSeeded = false;
    std::vector<std::string> hiddenTools;
    std::vector<std::string> toolOrder;
    int summonKeyCode = 49;
    bool summonAlt = true;
    bool summonCommand = false;
    bool summonControl = false;
    bool summonShift = false;
    std::string lastScreen;
    bool panelPlacedByUser = false;
    double panelX = 0;
    double panelY = 0;
    double panelWidth = 0;
    double panelHeight = 0;
    std::string dismissedClip;
    std::unordered_map<std::string, HotkeyBinding> hotkeys;
    std::unordered_map<std::string, std::string> triggerWords;
};

struct Hit {
    std::string id;
    std::string kind;
    std::string title;
    std::string subtitle;
    std::string path;
    std::vector<std::string> keywords;
    std::string toolId;
    std::optional<std::string> copyText;
    int score = 0;
};

struct ToolSpec {
    std::string id;
    std::string title;
    std::vector<std::string> words;
};

const std::vector<ToolSpec>& ToolCatalog();
std::string ToolTitle(const std::string& id);
std::vector<Hit> ToolHits();

class Session {
public:
    explicit Session(ContractStore store);
    ContractStore store;
    StateMachine machine;
    Settings settings;
    Library library;
    std::vector<Clip> clips;
    std::vector<Hit> index;
    std::vector<std::string> recents;
    std::string query;
    std::string triggerWord;
    std::string payload;
    std::vector<Hit> hits;
    int selection = 0;
    std::function<void()> onChange;
    std::function<void()> onShow;
    std::function<void()> onHide;
    std::function<void(const std::string&)> onOpenTool;

    void Load();
    void Save();
    void SetIndex(std::vector<Hit> next);
    int ImportStarterTools();
    void NoteClipboard(const std::string& text, const std::vector<std::string>& files);
    void Search();
    std::vector<Hit> RecentHits() const;
    void TouchRecentFromPath(const std::string& path, const std::string& kind, const std::string& title);
    void Execute(const Hit& hit);
    void ExecuteSelection();
    void Escape();
    void Summon();
    void Hide();
    void ReorderGroup(const std::string& sourceId, const std::string& targetId, bool placeAfter);
    void MoveShortcut(const std::string& itemId, const std::string& sourceGroupId, const std::string& targetGroupId);
    void ReorderShortcut(const std::string& itemId, const std::string& groupId, const std::string& beforeId, bool placeAfter);
    void RenameGroup(const std::string& id, const std::string& name);
    void DeleteGroupById(const std::string& id);
    bool AddShortcut(const std::string& path, const std::string& groupId);
    bool AddUrl(const std::string& url, const std::string& groupId);
    bool RemoveShortcut(const std::string& itemId);
    std::optional<TriggerDef> ResolveTriggerWord(const std::string& word) const;
    void PruneClips();
    void Enter(const std::string& next);

private:
    void SeedStarterIfNeeded();
    int ApplyStarter(bool merge);
    void Remember(const std::string& id);
    void Go(const std::string& next);
    std::optional<Hit> TryResolveRecent(const std::string& id) const;
    std::vector<Hit> ToolModeHits(const std::string& toolId, const std::string& payloadText) const;
    std::string ResolveGroup(const std::string& groupId);
    bool PutShortcut(const std::string& stored, const std::string& title, const std::string& kind, const std::string& label, const std::string& groupId);
    bool PurgeReservedNavGroup();
    uint64_t shownAt_ = 0;
    std::vector<IndexRow> searchRows_;
};

struct GroupHit {
    std::string id;
    float x = 0.f;
    float y = 0.f;
    float w = 0.f;
    float h = 0.f;
};

struct Shell {
    Session* session = nullptr;
    bool hotkeyReady = false;
    bool suppressBlur = false;
    std::string recordingHotkey;
    bool showLauncher = false;
    bool showGroups = false;
    bool showTools = false;
    bool showSettings = false;
    bool showGuide = false;
    std::string toolMode = "grid";
    std::string activeTool;
    std::string settingsTab = "general";
    int guideStep = 1;
    std::string toolOutput;
    std::string toolInput;
    std::string selectedGroup = "all";
    bool externalDrop = false;
    bool groupsEditing = false;
    double editWobbleStart = -1;
    std::string hoverGroup;
    float dropX = -10000.f;
    float dropY = -10000.f;
    std::vector<GroupHit> groupHits;
    std::vector<std::string> foldedGroups;
    std::string renamingGroup;
    std::string renameBuffer;
    std::string confirmDeleteId;
    bool renameFocus = false;
    std::string urlDraft;
    std::string groupFilter;
    bool groupSearchOpen = false;
    bool groupSearchFocus = false;
    std::vector<Hit> home;
    int launcherShow = 0;
    int generation = 0;
    bool wantFocus = false;
    // 大于 0 时，接下来几帧要把键盘焦点交给搜索框。窗口焦点和输入框焦点是两件事。
    int focusQuery = 0;
    bool hadFocus = false;
    uint64_t blurSince = 0;
    uint64_t blurGraceUntil = 0;
    uint64_t ignoreEscapeUntil = 0;
    bool escapeSeenUp = true;
    std::function<void()> onHotkeyChanged;
    bool quitRequested = false;
    void* hostWindow = nullptr;
    bool pickingColor = false;
    std::string pickedHex;

    void ShowLauncher();
    void HideLauncher();
    void ToggleGroups();
    void ShowGroups();
    void ShowTools();
    void ShowTool(const std::string& id);
    void CloseTool();
    void ShowSettings(const std::string& tab = "general");
    void ShowGuide();
    void NoteHotkey(bool ready);
    std::string PanelHotkeyText() const;
};

void DrawInterface(Shell& shell);
void DrawLauncher(Shell& shell);
void DrawGroups(Shell& shell);
void DrawTools(Shell& shell);
void DrawSettings(Shell& shell);
void DrawGuide(Shell& shell);
void DrawPicker(Shell& shell);
void PaintChrome(const Shell& shell);
void FinishChrome(const Shell& shell);
void DrawPanelShadow(const Shell& shell, float pad);
float ShadowPad(const Shell& shell);
void BindIconDevice(void* device);
void SyncPicker(Shell& shell);
float Px(double dip);
void ApplyTheme(const Shell& shell);
bool LoadUiFont(float scale, float inputDip);
void PresentHost(void* hwnd, Shell& shell, bool reveal = true);
void TickBlur(Shell& shell);

struct HotkeySlot {
    int id = 0;
    bool alt = false;
    bool control = false;
    bool shift = false;
    bool win = false;
    int virtualKey = 0;
};

bool InstallHotkeys(const std::vector<HotkeySlot>& slots);
void RemoveHotkeys();
void SetHotkeyHandler(std::function<void(int)> handler);

}  // namespace dk

struct ImFont;
extern ImFont* g_inputFont;
extern ImFont* g_iconFont;
extern ImFont* g_smallFont;
