#include "deskkit.hpp"

#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>
#include <bcrypt.h>
#include <psapi.h>
#include <shellapi.h>

#include <algorithm>
#include <cstdio>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <functional>
#include <mutex>
#include <unordered_map>

namespace dk {
namespace {

namespace fs = std::filesystem;

std::wstring Widen(const std::string& text) {
    if (text.empty()) return {};
    const int size = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
    std::wstring wide(size, 0);
    MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), wide.data(), size);
    return wide;
}

std::string Narrow(std::wstring_view text) {
    if (text.empty()) return {};
    const int size = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    std::string utf8(size, 0);
    WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), utf8.data(), size, nullptr, nullptr);
    return utf8;
}

std::string LowerAscii(std::string text) {
    for (char& ch : text) {
        if (ch >= 'A' && ch <= 'Z') ch = static_cast<char>(ch - 'A' + 'a');
    }
    return text;
}

bool EqualsInsensitive(const std::string& left, const std::string& right) { return LowerAscii(left) == LowerAscii(right); }

std::string Folder(int id) {
    wchar_t path[MAX_PATH];
    if (FAILED(SHGetFolderPathW(nullptr, id, nullptr, 0, path))) return {};
    return Narrow(path);
}

std::string Known(REFKNOWNFOLDERID id) {
    PWSTR path = nullptr;
    if (FAILED(SHGetKnownFolderPath(id, 0, nullptr, &path)) || path == nullptr) return {};
    std::string text = Narrow(path);
    CoTaskMemFree(path);
    return text;
}

std::mutex g_logMu;
std::vector<std::string> g_logPending;
std::function<void(int)> g_onHotkey;
bool Contains(const std::string& text, const std::string& part);

std::string UtcStamp() {
    SYSTEMTIME system{};
    GetSystemTime(&system);
    char buffer[40];
    std::snprintf(buffer, sizeof(buffer), "%04u-%02u-%02uT%02u:%02u:%02uZ", system.wYear, system.wMonth, system.wDay, system.wHour, system.wMinute, system.wSecond);
    return buffer;
}

void FlushLog() {
    std::vector<std::string> lines;
    {
        std::lock_guard lock(g_logMu);
        lines.swap(g_logPending);
    }
    if (lines.empty()) return;
    try {
        const fs::path path = fs::path(Widen(DataRoot())) / "logs" / "deskkit.log";
        fs::create_directories(path.parent_path());
        std::ofstream out(path, std::ios::app | std::ios::binary);
        for (const std::string& line : lines) out << line << "\n";
    } catch (...) {
    }
}

std::unordered_map<uint32_t, std::string> g_pinyin;
bool g_pinyinLoaded = false;

std::string StripTone(const std::string& syllable) {
    std::string plain;
    for (size_t i = 0; i < syllable.size();) {
        const unsigned char c = static_cast<unsigned char>(syllable[i]);
        if (c < 0x80) {
            if (c >= 'A' && c <= 'Z') plain.push_back(static_cast<char>(c - 'A' + 'a'));
            else plain.push_back(static_cast<char>(c));
            ++i;
            continue;
        }
        std::string glyph = syllable.substr(i, c >= 0xE0 ? 3 : 2);
        i += glyph.size();
        if (glyph == "ā" || glyph == "á" || glyph == "ǎ" || glyph == "à") plain += "a";
        else if (glyph == "ē" || glyph == "é" || glyph == "ě" || glyph == "è") plain += "e";
        else if (glyph == "ī" || glyph == "í" || glyph == "ǐ" || glyph == "ì") plain += "i";
        else if (glyph == "ō" || glyph == "ó" || glyph == "ǒ" || glyph == "ò") plain += "o";
        else if (glyph == "ū" || glyph == "ú" || glyph == "ǔ" || glyph == "ù") plain += "u";
        else if (glyph == "ǖ" || glyph == "ǘ" || glyph == "ǚ" || glyph == "ǜ" || glyph == "ü") plain += "v";
        else plain += glyph;
    }
    return plain;
}

uint32_t NextCodepoint(const std::string& text, size_t& index) {
    if (index >= text.size()) return 0;
    const unsigned char c = static_cast<unsigned char>(text[index]);
    if (c < 0x80) return text[index++];
    if ((c & 0xE0) == 0xC0 && index + 1 < text.size()) {
        uint32_t cp = ((c & 0x1F) << 6) | (static_cast<unsigned char>(text[index + 1]) & 0x3F);
        index += 2;
        return cp;
    }
    if ((c & 0xF0) == 0xE0 && index + 2 < text.size()) {
        uint32_t cp = ((c & 0x0F) << 12) | ((static_cast<unsigned char>(text[index + 1]) & 0x3F) << 6) | (static_cast<unsigned char>(text[index + 2]) & 0x3F);
        index += 3;
        return cp;
    }
    if ((c & 0xF8) == 0xF0 && index + 3 < text.size()) {
        uint32_t cp = ((c & 0x07) << 18) | ((static_cast<unsigned char>(text[index + 1]) & 0x3F) << 12) |
                      ((static_cast<unsigned char>(text[index + 2]) & 0x3F) << 6) | (static_cast<unsigned char>(text[index + 3]) & 0x3F);
        index += 4;
        return cp;
    }
    return text[index++];
}

bool SkipTitle(const std::string& title) {
    if (title.rfind("卸载", 0) == 0 || Contains(title, "Uninstall")) return true;
    return title.find("帮助") != std::string::npos || title.find("自述") != std::string::npos || Contains(title, "Readme") ||
           EqualsInsensitive(title, "Help") || title.size() >= 5 && EqualsInsensitive(title.substr(title.size() - 5), " Help");
}

bool Contains(const std::string& text, const std::string& part) { return LowerAscii(text).find(LowerAscii(part)) != std::string::npos; }

std::string ResolveShortcut(const std::string& path) {
    IShellLinkW* link = nullptr;
    if (FAILED(CoCreateInstance(CLSID_ShellLink, nullptr, CLSCTX_INPROC_SERVER, IID_IShellLinkW, reinterpret_cast<void**>(&link)))) return {};
    IPersistFile* file = nullptr;
    std::string target;
    if (SUCCEEDED(link->QueryInterface(IID_IPersistFile, reinterpret_cast<void**>(&file)))) {
        if (SUCCEEDED(file->Load(Widen(path).c_str(), STGM_READ))) {
            wchar_t buffer[MAX_PATH];
            WIN32_FIND_DATAW data{};
            if (SUCCEEDED(link->GetPath(buffer, MAX_PATH, &data, SLGP_RAWPATH)) && buffer[0]) target = Narrow(buffer);
        }
        file->Release();
    }
    link->Release();
    return target;
}

struct IndexBag {
    std::vector<IndexedEntry> hits;
    std::unordered_map<std::string, bool> seen;
};

void AddHit(IndexBag& bag, const std::string& id, const std::string& kind, const std::string& title, const std::string& subtitle, const std::string& path, int score) {
    const std::string key = LowerAscii(path);
    if (bag.seen[key]) return;
    bag.seen[key] = true;
    std::vector<std::string> words{LowerAscii(title)};
    try {
        for (const std::string& variant : PinyinVariants(title)) words.push_back(variant);
    } catch (...) {
        LogWarn("拼音失败");
    }
    std::sort(words.begin(), words.end());
    words.erase(std::unique(words.begin(), words.end()), words.end());
    bag.hits.push_back(IndexedEntry{id, kind, title, subtitle, path, words, score});
}

template <class Visit>
void ForEachChild(const fs::path& directory, Visit visit) {
    // range-for 的 ++ 遇到「无效参数」会抛异常，把启动过程直接终止。这里改用带错误码的步进。
    std::error_code error;
    fs::directory_iterator cursor(directory, fs::directory_options::skip_permission_denied, error);
    if (error) return;
    const fs::directory_iterator end;
    while (cursor != end) {
        fs::directory_entry entry = *cursor;
        error.clear();
        cursor.increment(error);
        const bool stop = static_cast<bool>(error);
        visit(entry);
        if (stop) break;
    }
}

bool ReparsePoint(const fs::directory_entry& entry) {
    std::error_code error;
    return entry.is_symlink(error) || static_cast<bool>(error);
}

void WalkShortcuts(const fs::path& directory, IndexBag& bag) {
    if (bag.hits.size() > 1500) return;
    ForEachChild(directory, [&](const fs::directory_entry& entry) {
        if (ReparsePoint(entry)) return;
        std::error_code status;
        if (entry.is_directory(status) || status) return;
        if (!EqualsInsensitive(entry.path().extension().string(), ".lnk")) return;
        const std::string title = Narrow(entry.path().stem().wstring());
        if (title.rfind("卸载", 0) == 0 || Contains(title, "Uninstall")) return;
        std::string target;
        try {
            target = ResolveShortcut(entry.path().string());
        } catch (...) {
            LogWarn("跳过快捷方式");
        }
        if (target.empty()) target = entry.path().string();
        const std::string kind = target.rfind("http", 0) == 0 ? "url" : "app";
        const std::string id = kind == "url" ? "url-" + target : "app-" + entry.path().string();
        AddHit(bag, id, kind, title, target, entry.path().string(), 30);
    });
    if (bag.hits.size() > 1500) return;
    ForEachChild(directory, [&](const fs::directory_entry& entry) {
        if (ReparsePoint(entry)) return;
        std::error_code status;
        if (entry.is_directory(status) && !status) WalkShortcuts(entry.path(), bag);
    });
}

void AddAppPaths(IndexBag& bag, HKEY hive) {
    HKEY key = nullptr;
    if (RegOpenKeyExW(hive, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\App Paths", 0, KEY_READ, &key) != ERROR_SUCCESS) return;
    DWORD index = 0;
    wchar_t name[512];
    while (true) {
        DWORD size = 512;
        if (RegEnumKeyExW(key, index++, name, &size, nullptr, nullptr, nullptr, nullptr) != ERROR_SUCCESS) break;
        HKEY sub = nullptr;
        if (RegOpenKeyExW(key, name, 0, KEY_READ, &sub) != ERROR_SUCCESS) continue;
        wchar_t value[MAX_PATH];
        DWORD bytes = sizeof(value);
        DWORD type = 0;
        if (RegQueryValueExW(sub, nullptr, nullptr, &type, reinterpret_cast<LPBYTE>(value), &bytes) == ERROR_SUCCESS && type == REG_SZ) {
            const std::string path = Narrow(value);
            if (!path.empty() && fs::exists(Widen(path))) {
                const std::string title = fs::path(Widen(Narrow(name))).stem().string();
                AddHit(bag, "app-" + path, "app", Narrow(fs::path(name).stem().wstring()), path, path, 28);
            }
        }
        RegCloseKey(sub);
    }
    RegCloseKey(key);
}

void AppendFile(const fs::path& path, IndexBag& bag, int depth, int& count) {
    if (count > 600) return;
    const std::string name = Narrow(path.filename().wstring());
    if (name.empty() || name[0] == '.' || EqualsInsensitive(path.extension().string(), ".lnk")) return;
    AddHit(bag, "file-" + path.string(), "file", name, path.string(), path.string(), 20);
    ++count;
    std::error_code directory;
    if (depth >= 1 || !fs::is_directory(path, directory) || directory) return;
    int taken = 0;
    ForEachChild(path, [&](const fs::directory_entry& child) {
        if (taken >= 40) return;
        AppendFile(child.path(), bag, depth + 1, count);
        ++taken;
    });
}

const std::vector<std::string>& SkipFolders() {
    static const std::vector<std::string> names = {
        "Startup", "启动", "Windows Administrative Tools", "Administrative Tools", "Windows 管理工具", "管理工具",
        "Windows Ease of Access", "Ease of Access", "Windows 轻松使用", "轻松使用", "辅助功能", "Accessibility",
        "Windows PowerShell", "Windows PowerShell (x86)", "Maintenance", "维护", "Windows Kits",
        "System Tools", "Windows 系统", "Accessories", "Windows Accessories", "Windows 附件", "附件"};
    return names;
}

bool FolderSkipped(const fs::path& path) {
    const std::string name = Narrow(path.filename().wstring());
    for (const std::string& skip : SkipFolders()) {
        if (EqualsInsensitive(name, skip)) return true;
    }
    return false;
}

bool SkipTarget(const std::string& target) {
    const std::string name = LowerAscii(fs::path(Widen(target)).filename().string());
    return name == "msiexec.exe" || name == "explorer.exe" || name == "hh.exe";
}

void CollectInstalled(const fs::path& directory, std::vector<StarterHit>& hits, std::unordered_map<std::string, bool>& taken) {
    ForEachChild(directory, [&](const fs::directory_entry& entry) {
        if (ReparsePoint(entry)) return;
        std::error_code status;
        if (entry.is_directory(status) || status) return;
        status.clear();
        if (!entry.is_regular_file(status) || status || !EqualsInsensitive(entry.path().extension().string(), ".lnk")) return;
        const std::string title = Narrow(entry.path().stem().wstring());
        if (SkipTitle(title)) return;
        const std::string target = ResolveShortcut(entry.path().string());
        std::string stored = entry.path().string();
        if (!target.empty()) {
            std::error_code kind;
            if (target.rfind("http", 0) == 0 || fs::is_directory(Widen(target), kind) || SkipTarget(target)) return;
            kind.clear();
            if (fs::exists(Widen(target), kind) && !kind) stored = target;
        }
        if (taken[LowerAscii(stored)]) return;
        taken[LowerAscii(stored)] = true;
        hits.push_back(StarterHit{Sha256FilePrefix(LowerAscii(stored), 12).insert(0, "app-"), "apps", title, stored});
    });
    ForEachChild(directory, [&](const fs::directory_entry& entry) {
        if (ReparsePoint(entry)) return;
        std::error_code status;
        if (entry.is_directory(status) && !status && !FolderSkipped(entry.path())) CollectInstalled(entry.path(), hits, taken);
    });
}

HWND g_hotkeyWindow = nullptr;
std::vector<int> g_hotkeyIds;

LRESULT CALLBACK HotkeyProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    if (msg == WM_HOTKEY && g_onHotkey) g_onHotkey(static_cast<int>(wParam));
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

}  // namespace

void LogInfo(const std::string& message) {
    const std::string line = UtcStamp() + " INFO " + message;
    std::fwrite(line.data(), 1, line.size(), stderr);
    std::fputc('\n', stderr);
    {
        std::lock_guard lock(g_logMu);
        g_logPending.push_back(line);
    }
    FlushLog();
}

void LogWarn(const std::string& message) {
    const std::string line = UtcStamp() + " WARN " + message;
    std::fwrite(line.data(), 1, line.size(), stderr);
    std::fputc('\n', stderr);
    {
        std::lock_guard lock(g_logMu);
        g_logPending.push_back(line);
    }
    FlushLog();
}

std::string DataRoot() {
    static std::string root = [] {
        const std::string path = Folder(CSIDL_APPDATA) + "\\DeskKit";
        fs::create_directories(Widen(path));
        return path;
    }();
    return root;
}

std::string DataFile(const std::string& name) { return DataRoot() + "\\" + name; }

std::string ReadTextFile(const std::string& path) {
    std::ifstream in(fs::path(Widen(path)), std::ios::binary);
    if (!in) return {};
    std::ostringstream buffer;
    buffer << in.rdbuf();
    return buffer.str();
}

void WriteAtomic(const std::string& path, const std::string& text) {
    // 先写临时文件，再替换。中途失败不会把原来的设置写坏。
    const std::wstring target = Widen(path);
    const std::wstring temporary = Widen(path + ".tmp");
    {
        std::ofstream out(temporary.c_str(), std::ios::binary | std::ios::trunc);
        out.write(text.data(), static_cast<std::streamsize>(text.size()));
    }
    MoveFileExW(temporary.c_str(), target.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH);
}

std::string NewId() {
    GUID id{};
    CoCreateGuid(&id);
    char buffer[40];
    std::snprintf(buffer, sizeof(buffer), "%08lx-%04x-%04x-%02x%02x-%02x%02x%02x%02x%02x%02x",
                  id.Data1, id.Data2, id.Data3, id.Data4[0], id.Data4[1], id.Data4[2], id.Data4[3], id.Data4[4], id.Data4[5], id.Data4[6], id.Data4[7]);
    return buffer;
}

std::string ExePath() {
    wchar_t buffer[MAX_PATH];
    GetModuleFileNameW(nullptr, buffer, MAX_PATH);
    return Narrow(buffer);
}

double WorkingSetMb() {
    PROCESS_MEMORY_COUNTERS counters{};
    if (!GetProcessMemoryInfo(GetCurrentProcess(), &counters, sizeof(counters))) return 0;
    return counters.WorkingSetSize / 1024.0 / 1024.0;
}

bool IsSystemDark() {
    HKEY key = nullptr;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize", 0, KEY_READ, &key) != ERROR_SUCCESS) return false;
    DWORD value = 1;
    DWORD size = sizeof(value);
    RegQueryValueExW(key, L"AppsUseLightTheme", nullptr, nullptr, reinterpret_cast<LPBYTE>(&value), &size);
    RegCloseKey(key);
    return value == 0;
}

void SetAutoStart(bool enabled) {
    HKEY key = nullptr;
    if (RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", 0, nullptr, 0, KEY_SET_VALUE, nullptr, &key, nullptr) != ERROR_SUCCESS) return;
    if (!enabled) {
        RegDeleteValueW(key, L"DeskKit");
    } else {
        const std::wstring command = L"\"" + Widen(ExePath()) + L"\" --background";
        RegSetValueExW(key, L"DeskKit", 0, REG_SZ, reinterpret_cast<const BYTE*>(command.c_str()), static_cast<DWORD>((command.size() + 1) * sizeof(wchar_t)));
    }
    RegCloseKey(key);
}

void OpenTarget(const std::string& path) {
    const std::wstring wide = Widen(path);
    if (path.rfind("http://", 0) != 0 && path.rfind("https://", 0) != 0 && !fs::exists(wide)) {
        LogWarn("快捷项已经不在原位置 " + path);
    }
    ShellExecuteW(nullptr, L"open", wide.c_str(), nullptr, nullptr, SW_SHOWNORMAL);
}

void RevealTarget(const std::string& path) {
    if (path.rfind("http://", 0) == 0 || path.rfind("https://", 0) == 0) return;
    const std::wstring wide = Widen(path);
    if (fs::is_directory(wide)) {
        ShellExecuteW(nullptr, L"open", L"explorer.exe", (L"\"" + wide + L"\"").c_str(), nullptr, SW_SHOWNORMAL);
        return;
    }
    if (fs::exists(wide)) {
        const std::wstring args = L"/select,\"" + wide + L"\"";
        ShellExecuteW(nullptr, L"open", L"explorer.exe", args.c_str(), nullptr, SW_SHOWNORMAL);
        return;
    }
    const fs::path folder = fs::path(wide).parent_path();
    if (fs::is_directory(folder)) ShellExecuteW(nullptr, L"open", L"explorer.exe", (L"\"" + folder.wstring() + L"\"").c_str(), nullptr, SW_SHOWNORMAL);
}

void CopyText(const std::string& text) {
    const std::wstring wide = Widen(text);
    if (!OpenClipboard(nullptr)) return;
    EmptyClipboard();
    const size_t bytes = (wide.size() + 1) * sizeof(wchar_t);
    HGLOBAL memory = GlobalAlloc(GMEM_MOVEABLE, bytes);
    if (memory) {
        std::memcpy(GlobalLock(memory), wide.c_str(), bytes);
        GlobalUnlock(memory);
        SetClipboardData(CF_UNICODETEXT, memory);
    }
    CloseClipboard();
}

uint32_t ClipboardSequence() { return GetClipboardSequenceNumber(); }

ClipboardSnapshot ReadClipboard() {
    ClipboardSnapshot snap;
    if (!OpenClipboard(nullptr)) return snap;
    if (HANDLE memory = GetClipboardData(CF_UNICODETEXT)) {
        const wchar_t* text = static_cast<const wchar_t*>(GlobalLock(memory));
        if (text) snap.text = Narrow(text);
        GlobalUnlock(memory);
    }
    if (snap.text.empty()) {
        if (HDROP drop = static_cast<HDROP>(GetClipboardData(CF_HDROP))) {
            const UINT count = DragQueryFileW(drop, 0xFFFFFFFF, nullptr, 0);
            for (UINT i = 0; i < count; ++i) {
                wchar_t buffer[MAX_PATH];
                if (DragQueryFileW(drop, i, buffer, MAX_PATH) > 0) snap.files.emplace_back(Narrow(buffer));
            }
        }
    }
    CloseClipboard();
    return snap;
}

bool IsAltDown() {
    return (GetAsyncKeyState(VK_MENU) & 0x8000) || (GetAsyncKeyState(VK_LMENU) & 0x8000) || (GetAsyncKeyState(VK_RMENU) & 0x8000);
}

bool IsLeftButtonDown() { return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0; }

void ReleaseAlt() {
    // 还按着 Alt 就不要补抬起，否则热键会卡住，下一次 Alt+Space 进不来。
    if (IsAltDown()) return;
    keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, 0);
}

int ToVirtualKey(int keyCode, bool recordedOnWindows) {
    if (recordedOnWindows) return keyCode;
    switch (keyCode) {
        case 2: return 0x44;
        case 49: return 0x20;
        case 36: return 0x0D;
        case 48: return 0x09;
        case 53: return 0x1B;
        case 43: return 0xBC;
        default: return keyCode;
    }
}

std::string Sha256Hex(const std::string& text) {
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0);
    BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0);
    BCryptHashData(hash, reinterpret_cast<PUCHAR>(const_cast<char*>(text.data())), static_cast<ULONG>(text.size()), 0);
    unsigned char digest[32];
    BCryptFinishHash(hash, digest, 32, 0);
    BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm, 0);
    static const char* kHex = "0123456789abcdef";
    std::string out(64, '0');
    for (int i = 0; i < 32; ++i) {
        out[i * 2] = kHex[digest[i] >> 4];
        out[i * 2 + 1] = kHex[digest[i] & 0xF];
    }
    return out;
}

std::string Sha256FilePrefix(const std::string& text, int hexChars) { return Sha256Hex(text).substr(0, hexChars); }

void LoadPinyinTable(const std::string& path) {
    std::ifstream in(fs::path(Widen(path)), std::ios::binary);
    if (!in) {
        LogWarn("没有拼音表，中文只能按原文匹配");
        return;
    }
    std::string line;
    while (std::getline(in, line)) {
        if (line.rfind("U+", 0) != 0) continue;
        const auto colon = line.find(':');
        if (colon == std::string::npos) continue;
        const uint32_t cp = static_cast<uint32_t>(std::strtoul(line.substr(2, colon - 2).c_str(), nullptr, 16));
        std::string syllable = line.substr(colon + 1);
        const auto comma = syllable.find(',');
        if (comma != std::string::npos) syllable = syllable.substr(0, comma);
        const auto comment = syllable.find('#');
        if (comment != std::string::npos) syllable = syllable.substr(0, comment);
        size_t begin = 0;
        while (begin < syllable.size() && std::isspace(static_cast<unsigned char>(syllable[begin]))) ++begin;
        size_t end = syllable.size();
        while (end > begin && std::isspace(static_cast<unsigned char>(syllable[end - 1]))) --end;
        syllable = StripTone(syllable.substr(begin, end - begin));
        if (!syllable.empty()) g_pinyin[cp] = syllable;
    }
    g_pinyinLoaded = true;
    LogInfo("拼音表 " + std::to_string(g_pinyin.size()) + " 字");
}

std::vector<std::string> PinyinVariants(const std::string& text) {
    if (text.find_first_not_of(" \t\r\n") == std::string::npos) return {};
    std::string full;
    std::string initials;
    for (size_t i = 0; i < text.size();) {
        const uint32_t cp = NextCodepoint(text, i);
        const auto found = g_pinyin.find(cp);
        if (found != g_pinyin.end() && !found->second.empty()) {
            full += found->second;
            initials.push_back(found->second[0]);
        } else if (cp < 128 && std::isalnum(static_cast<unsigned char>(cp))) {
            const char lower = static_cast<char>(std::tolower(static_cast<unsigned char>(cp)));
            full.push_back(lower);
            initials.push_back(lower);
        }
    }
    std::vector<std::string> words;
    auto push = [&](const std::string& word) {
        if (!word.empty() && std::find(words.begin(), words.end(), word) == words.end()) words.push_back(word);
    };
    push(full);
    push(initials);
    push(LowerAscii(text));
    return words;
}

std::vector<IndexedEntry> CollectIndex() {
    IndexBag bag;
    const std::string start = Known(FOLDERID_StartMenu);
    const std::string commonStart = Known(FOLDERID_CommonStartMenu);
    const std::string desktop = Known(FOLDERID_Desktop);
    const std::string publicDesktop = Known(FOLDERID_PublicDesktop);
    for (const std::string& root : {start, commonStart, desktop, publicDesktop}) {
        if (!root.empty() && fs::exists(Widen(root))) WalkShortcuts(fs::path(Widen(root)), bag);
        if (bag.hits.size() > 1500) break;
    }
    AddAppPaths(bag, HKEY_LOCAL_MACHINE);
    AddAppPaths(bag, HKEY_CURRENT_USER);
    int count = 0;
    const std::string home = Folder(CSIDL_PROFILE);
    for (const char* name : {"Desktop", "Documents", "Downloads"}) {
        const fs::path root = fs::path(Widen(home)) / Widen(name);
        if (!fs::exists(root)) continue;
        std::error_code error;
        for (const auto& child : fs::directory_iterator(root, error)) {
            if (error) break;
            AppendFile(child.path(), bag, 0, count);
            if (count > 600) break;
        }
    }
    LogInfo("索引完成，" + std::to_string(bag.hits.size()) + " 条");
    return bag.hits;
}

std::vector<StarterHit> FindStarter(const std::vector<StarterQuery>& items) {
    std::unordered_map<std::string, std::string> names;
    bool namesReady = false;
    auto loadNames = [&] {
        if (namesReady) return;
        namesReady = true;
        for (const std::string& root : {Known(FOLDERID_StartMenu), Known(FOLDERID_CommonStartMenu)}) {
            std::error_code present;
            if (root.empty() || !fs::exists(Widen(root), present) || present) continue;
            std::function<void(const fs::path&)> walk = [&](const fs::path& directory) {
                ForEachChild(directory, [&](const fs::directory_entry& entry) {
                    if (ReparsePoint(entry)) return;
                    std::error_code status;
                    if (entry.is_directory(status)) {
                        if (!status) walk(entry.path());
                        return;
                    }
                    status.clear();
                    if (!entry.is_regular_file(status) || status || !EqualsInsensitive(entry.path().extension().string(), ".lnk")) return;
                    const std::string name = Narrow(entry.path().stem().wstring());
                    if (name.rfind("卸载", 0) == 0 || Contains(name, "Uninstall")) return;
                    if (!names.contains(LowerAscii(name))) names[LowerAscii(name)] = entry.path().string();
                });
            };
            walk(fs::path(Widen(root)));
        }
    };
    std::vector<StarterHit> hits;
    for (const StarterQuery& item : items) {
        if (item.title.find_first_not_of(' ') == std::string::npos) continue;
        std::string path;
        for (const std::string& raw : item.paths) {
            if (raw.find_first_not_of(' ') == std::string::npos) continue;
            wchar_t expanded[MAX_PATH];
            ExpandEnvironmentStringsW(Widen(raw).c_str(), expanded, MAX_PATH);
            if (fs::exists(expanded)) {
                path = Narrow(expanded);
                break;
            }
        }
        if (path.empty() && !item.names.empty()) {
            loadNames();
            for (const std::string& name : item.names) {
                const auto found = names.find(LowerAscii(name));
                if (found != names.end()) {
                    path = found->second;
                    break;
                }
            }
        }
        if (!path.empty()) hits.push_back(StarterHit{item.id, item.groupId, item.title, path});
    }
    return hits;
}

std::vector<StarterHit> InstalledApps(const std::vector<std::string>& takenPaths) {
    std::unordered_map<std::string, bool> taken;
    for (const std::string& path : takenPaths) {
        if (path.find_first_not_of(' ') != std::string::npos) taken[LowerAscii(path)] = true;
    }
    std::vector<StarterHit> hits;
    for (const std::string& root : {Known(FOLDERID_StartMenu), Known(FOLDERID_CommonStartMenu)}) {
        if (root.empty() || !fs::exists(Widen(root))) continue;
        const fs::path programs = fs::path(Widen(root)) / L"Programs";
        CollectInstalled(fs::exists(programs) ? programs : fs::path(Widen(root)), hits, taken);
    }
    std::sort(hits.begin(), hits.end(), [](const StarterHit& left, const StarterHit& right) { return LowerAscii(left.title) < LowerAscii(right.title); });
    if (hits.size() > 150) hits.resize(150);
    return hits;
}

MonitorBox BoxFrom(HMONITOR monitor) {
    MONITORINFO info{};
    info.cbSize = sizeof(info);
    if (!GetMonitorInfoW(monitor, &info)) return {};
    return MonitorBox{info.rcWork.left, info.rcWork.top, info.rcWork.right - info.rcWork.left, info.rcWork.bottom - info.rcWork.top};
}

MonitorBox MonitorAtCursor() {
    POINT point;
    GetCursorPos(&point);
    return BoxFrom(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST));
}

MonitorBox PrimaryMonitor() { return BoxFrom(MonitorFromPoint(POINT{0, 0}, MONITOR_DEFAULTTOPRIMARY)); }

std::string ScreenPixelHex(int x, int y) {
    HDC screen = GetDC(nullptr);
    const COLORREF color = GetPixel(screen, x, y);
    ReleaseDC(nullptr, screen);
    if (color == CLR_INVALID) return {};
    char buffer[8];
    std::snprintf(buffer, sizeof(buffer), "#%02X%02X%02X", GetRValue(color), GetGValue(color), GetBValue(color));
    return buffer;
}

void SetHotkeyHandler(std::function<void(int)> handler) { g_onHotkey = std::move(handler); }

bool InstallHotkeys(const std::vector<HotkeySlot>& slots) {
    if (!g_hotkeyWindow) {
        WNDCLASSW klass{};
        klass.lpfnWndProc = HotkeyProc;
        klass.hInstance = GetModuleHandleW(nullptr);
        klass.lpszClassName = L"DeskKitHotkey";
        RegisterClassW(&klass);
        g_hotkeyWindow = CreateWindowExW(0, L"DeskKitHotkey", L"DeskKitHotkey", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, GetModuleHandleW(nullptr), nullptr);
    }
    RemoveHotkeys();
    bool summonOk = false;
    for (const HotkeySlot& slot : slots) {
        UINT mods = MOD_NOREPEAT;
        if (slot.alt) mods |= MOD_ALT;
        if (slot.control) mods |= MOD_CONTROL;
        if (slot.shift) mods |= MOD_SHIFT;
        if (slot.win) mods |= MOD_WIN;
        const bool ok = RegisterHotKey(g_hotkeyWindow, slot.id, mods, static_cast<UINT>(slot.virtualKey));
        if (ok) g_hotkeyIds.push_back(slot.id);
        else LogWarn(slot.id == 1 ? "呼出热键注册失败，组合键可能已被占用" : "快捷启动热键注册失败，组合键可能已被占用");
        if (slot.id == 1) summonOk = ok;
    }
    return summonOk;
}

void RemoveHotkeys() {
    if (!g_hotkeyWindow) return;
    for (int id : g_hotkeyIds) UnregisterHotKey(g_hotkeyWindow, id);
    g_hotkeyIds.clear();
}

}  // namespace dk
