#include "deskkit.hpp"

#include "json.hpp"

#include <windows.h>
#include <bcrypt.h>

#include <algorithm>
#include <cctype>
#include <cmath>
#include <cstdio>
#include <filesystem>
#include <fstream>
#include <queue>
#include <sstream>
#include <stdexcept>

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

bool ContainsInsensitive(const std::string& haystack, const std::string& needle) {
    if (needle.empty()) return true;
    const std::string hay = LowerAscii(haystack);
    const std::string pin = LowerAscii(needle);
    return hay.find(pin) != std::string::npos;
}

bool EqualsInsensitive(const std::string& left, const std::string& right) {
    return LowerAscii(left) == LowerAscii(right);
}

std::string ReadAll(const fs::path& path) {
    std::ifstream in(path, std::ios::binary);
    if (!in) throw std::runtime_error("打不开 " + path.string());
    std::ostringstream buffer;
    buffer << in.rdbuf();
    return buffer.str();
}

std::string JsonString(const json& item, const char* name) {
    if (!item.is_object() || !item.contains(name) || !item[name].is_string()) return {};
    return item[name].get<std::string>();
}

std::vector<std::string> JsonStrings(const json& item, const char* name) {
    std::vector<std::string> values;
    if (!item.is_object() || !item.contains(name) || !item[name].is_array()) return values;
    for (const auto& part : item[name]) {
        values.push_back(part.is_string() ? part.get<std::string>() : "");
    }
    return values;
}

// 和 C# IncrementalHash 一样：路径 UTF-8、一个 0、再跟上文件字节。
void HashBytes(BCRYPT_HASH_HANDLE hash, const void* data, size_t size) {
    if (size == 0) return;
    if (BCryptHashData(hash, static_cast<PUCHAR>(const_cast<void*>(data)), static_cast<ULONG>(size), 0) != 0) {
        throw std::runtime_error("SHA256 失败");
    }
}

}  // namespace

Result Result::Success(std::string data) { return Result{true, "OK", "", std::move(data)}; }
Result Result::Fail(std::string code, std::string message) { return Result{false, std::move(code), std::move(message), ""}; }

struct ContractStore::Impl {
    std::unordered_map<std::string, json> files;
};

ContractStore::ContractStore(std::string root) : root_(std::move(root)), impl_(new Impl) {
    static const char* kRequired[] = {
        "contract-version.json", "tokens.json", "layout.json", "ui-state.json", "triggers.json",
        "keymap.json", "i18n.json", "permissions.json", "platform-diffs.json",
        "schema/index-item.json", "schema/groups.json", "schema/clipboard.json", "schema/settings.json",
        "starter-pack.json"};
    for (const char* relative : kRequired) {
        fs::path path = fs::path(root_) / relative;
        if (!fs::exists(path)) throw std::runtime_error(std::string("缺少契约文件 ") + relative);
        impl_->files[relative] = json::parse(ReadAll(path));
    }
    if (impl_->files["contract-version.json"].value("contractVersion", "") != "1") {
        throw std::runtime_error("contractVersion 必须是 1");
    }
    for (const auto& item : impl_->files["platform-diffs.json"]["staged"]) {
        for (const char* field : {"owner", "dueAt", "parityPlan"}) {
            if (JsonString(item, field).empty()) throw std::runtime_error(std::string("阶段性差异缺少 ") + field);
        }
    }
    hash_ = HashTree(root_);
}

std::optional<std::string> ContractStore::Locate(const std::string& start) {
    if (const char* env = std::getenv("DESKKIT_CONTRACTS"); env && *env) return std::string(env);
    std::vector<fs::path> seeds;
    if (!start.empty()) seeds.emplace_back(start);
    wchar_t module[MAX_PATH];
    if (GetModuleFileNameW(nullptr, module, MAX_PATH) > 0) seeds.emplace_back(fs::path(module).parent_path());
    seeds.emplace_back(fs::current_path());
    for (fs::path cursor : seeds) {
        for (int i = 0; i < 12 && !cursor.empty(); ++i) {
            if (fs::exists(cursor / "contract-version.json")) return cursor.string();
            if (fs::exists(cursor / "contracts" / "contract-version.json")) return (cursor / "contracts").string();
            if (!cursor.has_parent_path() || cursor.parent_path() == cursor) break;
            cursor = cursor.parent_path();
        }
    }
    return std::nullopt;
}

void CollectFiles(const fs::path& directory, const fs::path& root, std::vector<std::string>& files) {
    // 不用 recursive_directory_iterator。MinGW 碰到无法进入的目录会抛异常，程序直接退出。
    std::error_code error;
    fs::directory_iterator cursor(directory, fs::directory_options::skip_permission_denied, error);
    if (error) return;
    const fs::directory_iterator end;
    while (cursor != end) {
        fs::directory_entry entry = *cursor;
        error.clear();
        cursor.increment(error);
        const bool stop = static_cast<bool>(error);
        std::error_code status;
        if (entry.is_symlink(status) || status) {
            if (stop) break;
            continue;
        }
        status.clear();
        if (entry.is_directory(status) && !status) CollectFiles(entry.path(), root, files);
        else if (!status) {
            status.clear();
            if (entry.is_regular_file(status) && !status) {
                std::string relative = entry.path().lexically_relative(root).generic_string();
                if (!(relative.size() >= 9 && relative.ends_with(".DS_Store"))) files.push_back(relative);
            }
        }
        if (stop) break;
    }
}

std::string ContractStore::HashTree(const std::string& root) {
    std::vector<std::string> files;
    CollectFiles(fs::path(root), fs::path(root), files);
    std::sort(files.begin(), files.end());

    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_HASH_HANDLE hash = nullptr;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) != 0) {
        throw std::runtime_error("打不开 SHA256");
    }
    if (BCryptCreateHash(algorithm, &hash, nullptr, 0, nullptr, 0, 0) != 0) {
        BCryptCloseAlgorithmProvider(algorithm, 0);
        throw std::runtime_error("建不起 SHA256");
    }
    for (const std::string& relative : files) {
        HashBytes(hash, relative.data(), relative.size());
        const unsigned char zero = 0;
        HashBytes(hash, &zero, 1);
        const std::string bytes = ReadAll(fs::path(root) / fs::path(relative));
        HashBytes(hash, bytes.data(), bytes.size());
    }
    unsigned char digest[32];
    BCryptFinishHash(hash, digest, sizeof(digest), 0);
    BCryptDestroyHash(hash);
    BCryptCloseAlgorithmProvider(algorithm, 0);
    static const char* kHex = "0123456789abcdef";
    std::string text(64, '0');
    for (int i = 0; i < 32; ++i) {
        text[i * 2] = kHex[digest[i] >> 4];
        text[i * 2 + 1] = kHex[digest[i] & 0xF];
    }
    return text;
}

std::string ContractStore::Text(const std::string& key) const {
    const auto& strings = impl_->files.at("i18n.json").at("strings");
    if (strings.contains(key) && strings[key].is_string()) return strings[key].get<std::string>();
    return key;
}

std::string ContractStore::Text(const std::string& key, const std::vector<std::pair<std::string, std::string>>& values) const {
    std::string output = Text(key);
    for (const auto& [name, value] : values) {
        const std::string token = "{" + name + "}";
        for (size_t at = output.find(token); at != std::string::npos; at = output.find(token, at)) {
            output.replace(at, token.size(), value);
            at += value.size();
        }
    }
    return output;
}

std::string ContractStore::Color(const std::string& name, bool dark) const {
    const auto& tokens = impl_->files.at("tokens.json");
    if (dark && tokens.contains("dark") && tokens["dark"].contains(name) && tokens["dark"][name].is_string()) {
        const std::string value = tokens["dark"][name].get<std::string>();
        if (!value.empty()) return value;
    }
    if (tokens["color"].contains(name) && tokens["color"][name].is_string()) return tokens["color"][name].get<std::string>();
    return "#000000";
}

std::vector<std::string> ContractStore::GroupColors() const {
    std::vector<std::string> colors;
    for (const auto& item : impl_->files.at("tokens.json").at("chip.group")) colors.push_back(item.get<std::string>());
    return colors;
}

double ContractStore::Layout(const std::vector<std::string>& path) const {
    const json* node = &impl_->files.at("layout.json");
    for (const std::string& key : path) {
        if (!node->is_object() || !node->contains(key)) return 0;
        node = &(*node)[key];
    }
    return node->is_number() ? node->get<double>() : 0;
}

std::string ContractStore::KeyLabel(const std::string& platform, const std::string& key) const {
    const auto& keys = impl_->files.at("keymap.json").at("keys");
    if (keys.contains(platform) && keys[platform].contains(key) && keys[platform][key].is_string()) {
        return keys[platform][key].get<std::string>();
    }
    return LowerAscii(key).empty() ? key : key;
}

std::vector<std::string> ContractStore::ChordParts(const std::string& action) const {
    const auto& chords = impl_->files.at("keymap.json").at("chords");
    if (!chords.contains(action) || !chords[action].is_array()) return {};
    return JsonStrings(json{{"parts", chords[action]}}, "parts");
}

std::string ContractStore::FormatChord(const std::string& platform, const std::string& action) const {
    std::string text;
    for (const std::string& part : ChordParts(action)) {
        if (!text.empty()) text += "+";
        text += KeyLabel(platform, part);
    }
    return text;
}

std::vector<TriggerDef> ContractStore::Triggers() const {
    std::vector<TriggerDef> items;
    for (const auto& item : impl_->files.at("triggers.json").at("triggers")) {
        items.push_back(TriggerDef{JsonString(item, "word"), JsonString(item, "toolId"), JsonString(item, "label"), JsonString(item, "placeholder")});
    }
    return items;
}

std::optional<TriggerDef> ContractStore::Trigger(const std::string& word) const {
    for (const TriggerDef& item : Triggers()) {
        if (EqualsInsensitive(item.word, word)) return item;
    }
    return std::nullopt;
}

StarterCatalog ContractStore::Starter() const {
    const auto& root = impl_->files.at("starter-pack.json");
    auto groupOf = [](const json& item) {
        return StarterGroupSpec{JsonString(item, "id"), JsonString(item, "name"), JsonString(item, "color")};
    };
    auto sideOf = [](const json& item, const char* name) {
        if (!item.contains(name) || !item[name].is_object()) return StarterSide{};
        const auto& side = item[name];
        return StarterSide{JsonString(side, "title"), JsonStrings(side, "paths"), JsonStrings(side, "names")};
    };
    StarterCatalog catalog;
    catalog.ungrouped = groupOf(root.at("ungrouped"));
    for (const auto& item : root.at("groups")) catalog.groups.push_back(groupOf(item));
    for (const auto& item : root.at("items")) {
        catalog.items.push_back(StarterEntry{JsonString(item, "id"), JsonString(item, "group"), sideOf(item, "windows"), sideOf(item, "macos")});
    }
    return catalog;
}

std::vector<PermissionDef> ContractStore::Permissions(const std::string& platform) const {
    std::vector<PermissionDef> items;
    const auto& platforms = impl_->files.at("permissions.json").at("platforms");
    if (!platforms.contains(platform)) return items;
    for (const auto& item : platforms[platform]) {
        items.push_back(PermissionDef{JsonString(item, "id"), JsonString(item, "title"), JsonString(item, "reason")});
    }
    return items;
}

std::vector<StagedDiff> ContractStore::StagedDiffs() const {
    std::vector<StagedDiff> items;
    for (const auto& item : impl_->files.at("platform-diffs.json").at("staged")) {
        items.push_back(StagedDiff{JsonString(item, "id"), JsonString(item, "category"), JsonString(item, "owner"), JsonString(item, "dueAt"), JsonString(item, "parityPlan")});
    }
    return items;
}

std::unordered_map<std::string, std::vector<std::string>> ContractStore::Transitions() const {
    std::unordered_map<std::string, std::vector<std::string>> map;
    for (auto it = impl_->files.at("ui-state.json").at("transitions").begin(); it != impl_->files.at("ui-state.json").at("transitions").end(); ++it) {
        map[it.key()] = JsonStrings(json{{"v", it.value()}}, "v");
    }
    return map;
}

std::unordered_map<std::string, std::vector<std::string>> ContractStore::EscMap() const {
    std::unordered_map<std::string, std::vector<std::string>> map;
    for (auto it = impl_->files.at("ui-state.json").at("esc").begin(); it != impl_->files.at("ui-state.json").at("esc").end(); ++it) {
        map[it.key()] = JsonStrings(json{{"v", it.value()}}, "v");
    }
    return map;
}

std::string ContractStore::FixtureText(const std::string& name) const {
    return ReadAll(fs::path(root_) / "fixtures" / name);
}

StateMachine::StateMachine(const ContractStore& store, std::function<void(const std::string&)> log)
    : transitions_(store.Transitions()), esc_(store.EscMap()), log_(std::move(log)) {}

StateMachine::StateMachine(std::unordered_map<std::string, std::vector<std::string>> transitions,
                           std::unordered_map<std::string, std::vector<std::string>> esc,
                           std::string state)
    : transitions_(std::move(transitions)), esc_(std::move(esc)), state_(std::move(state)) {}

bool StateMachine::Reach(const std::string& next) {
    if (state_ == next) return true;
    const auto path = ShortestPath(next);
    if (!path) {
        if (log_) log_("未能进入 " + next + "，当前 " + state_);
        return false;
    }
    for (const std::string& step : *path) Transition(step);
    return state_ == next;
}

Result StateMachine::Transition(const std::string& next) {
    const auto found = transitions_.find(state_);
    const bool allowed = found != transitions_.end() && std::find(found->second.begin(), found->second.end(), next) != found->second.end();
    if (!allowed) {
        if (log_) log_("拒绝状态转移 " + state_ + " -> " + next);
        return Result::Fail("INVALID_ARG", "不能从 " + state_ + " 进入 " + next);
    }
    const std::string from = state_;
    state_ = next;
    if (log_) log_("状态转移 " + from + " -> " + next);
    return Result::Success(state_);
}

Result StateMachine::Escape() {
    const auto found = esc_.find(state_);
    if (found == esc_.end() || found->second.empty()) {
        if (log_) log_("Esc 在 " + state_ + "，保持隐藏");
        return Result::Success(state_);
    }
    if (log_) log_("Esc 回退 " + state_ + " -> " + found->second[0]);
    return Transition(found->second[0]);
}

std::vector<std::string> StateMachine::EscapeSequence() const {
    StateMachine copy(transitions_, esc_, state_);
    std::vector<std::string> seen;
    while (copy.state_ != "HIDDEN" && seen.size() < 8) {
        if (!copy.Escape().ok) break;
        seen.push_back(copy.state_);
    }
    return seen;
}

std::optional<std::vector<std::string>> StateMachine::ShortestPath(const std::string& next) const {
    std::queue<std::vector<std::string>> queue;
    std::unordered_map<std::string, bool> seen;
    seen[state_] = true;
    queue.push({state_});
    while (!queue.empty()) {
        auto path = queue.front();
        queue.pop();
        const auto nexts = transitions_.find(path.back());
        if (nexts == transitions_.end()) continue;
        for (const std::string& candidate : nexts->second) {
            if (seen[candidate]) continue;
            seen[candidate] = true;
            auto longer = path;
            longer.push_back(candidate);
            if (candidate == next) {
                longer.erase(longer.begin());
                return longer;
            }
            queue.push(std::move(longer));
        }
    }
    return std::nullopt;
}

namespace {

struct Parser {
    const std::string& text;
    size_t index = 0;
    bool failed = false;
    bool dividedByZero = false;
    explicit Parser(const std::string& input) : text(input) {}
    bool End() const { return index >= text.size(); }
    void Skip() {
        while (!End() && std::isspace(static_cast<unsigned char>(text[index]))) ++index;
    }
    double Expression() {
        double value = Term();
        while (true) {
            Skip();
            if (End() || (text[index] != '+' && text[index] != '-')) return value;
            const char op = text[index++];
            const double right = Term();
            value = op == '+' ? value + right : value - right;
        }
    }
    double Term() {
        double value = Factor();
        while (true) {
            Skip();
            if (End() || (text[index] != '*' && text[index] != '/')) return value;
            const char op = text[index++];
            const double right = Factor();
            if (op == '/' && right == 0) {
                dividedByZero = true;
                return 0;
            }
            value = op == '*' ? value * right : value / right;
        }
    }
    double Factor() {
        Skip();
        if (End()) {
            failed = true;
            return 0;
        }
        if (text[index] == '(') {
            ++index;
            const double value = Expression();
            Skip();
            if (End() || text[index] != ')') {
                failed = true;
                return 0;
            }
            ++index;
            return value;
        }
        return Number();
    }
    double Number() {
        Skip();
        const size_t start = index;
        if (!End() && (text[index] == '+' || text[index] == '-')) ++index;
        bool digits = false;
        while (!End() && text[index] >= '0' && text[index] <= '9') {
            digits = true;
            ++index;
        }
        if (!End() && text[index] == '.') {
            ++index;
            while (!End() && text[index] >= '0' && text[index] <= '9') {
                digits = true;
                ++index;
            }
        }
        if (!digits) {
            failed = true;
            return 0;
        }
        try {
            return std::stod(text.substr(start, index - start));
        } catch (...) {
            failed = true;
            return 0;
        }
    }
};

const std::unordered_map<std::string, std::pair<std::string, double>>& UnitTable() {
    static const std::unordered_map<std::string, std::pair<std::string, double>> table = {
        {"m", {"len", 1}}, {"km", {"len", 1000}}, {"mi", {"len", 1609.344}}, {"cm", {"len", 0.01}},
        {"kg", {"wt", 1}}, {"g", {"wt", 0.001}}, {"lb", {"wt", 0.45359237}},
        {"b", {"data", 1}}, {"kb", {"data", 1024}}, {"mb", {"data", 1048576}},
        {"cny", {"money", 1}}, {"usd", {"money", 7.12}}};
    return table;
}

bool ParseUnitQuery(const std::string& input, double& amount, std::string& from, std::string& to) {
    size_t i = 0;
    while (i < input.size() && std::isspace(static_cast<unsigned char>(input[i]))) ++i;
    const size_t number = i;
    while (i < input.size() && ((input[i] >= '0' && input[i] <= '9') || input[i] == '.')) ++i;
    if (i == number) return false;
    amount = std::stod(input.substr(number, i - number));
    while (i < input.size() && std::isspace(static_cast<unsigned char>(input[i]))) ++i;
    const size_t unit = i;
    while (i < input.size() && std::isalpha(static_cast<unsigned char>(input[i]))) ++i;
    if (i == unit) return false;
    from = LowerAscii(input.substr(unit, i - unit));
    while (i < input.size() && std::isspace(static_cast<unsigned char>(input[i]))) ++i;
    if (input.compare(i, 2, "to") != 0 && input.compare(i, 2, "TO") != 0 && input.compare(i, 2, "To") != 0) {
        // " to " 大小写不敏感，只认独立的 to。
    }
    if (i + 2 > input.size() || !EqualsInsensitive(input.substr(i, 2), "to")) return false;
    i += 2;
    if (i >= input.size() || !std::isspace(static_cast<unsigned char>(input[i]))) return false;
    while (i < input.size() && std::isspace(static_cast<unsigned char>(input[i]))) ++i;
    const size_t dest = i;
    while (i < input.size() && std::isalpha(static_cast<unsigned char>(input[i]))) ++i;
    if (i == dest) return false;
    to = LowerAscii(input.substr(dest, i - dest));
    while (i < input.size() && std::isspace(static_cast<unsigned char>(input[i]))) ++i;
    return i == input.size();
}

std::string FormatTemp(double amount, const std::string& from, const std::string& to) {
    double celsius = amount;
    if (from == "f") celsius = (amount - 32) * 5 / 9;
    else if (from == "k") celsius = amount - 273.15;
    double output = celsius;
    if (to == "f") output = celsius * 9 / 5 + 32;
    else if (to == "k") output = celsius + 273.15;
    return FormatNumber(output) + " " + to;
}

}  // namespace

std::string FormatNumber(double value) {
    const double rounded = std::round(value);
    if (std::fabs(value - rounded) < 1e-9 && rounded >= -2147483648.0 && rounded <= 2147483647.0) {
        return std::to_string(static_cast<int>(rounded));
    }
    std::ostringstream stream;
    stream.setf(std::ios::fixed);
    stream.precision(12);
    std::string text = stream.str();
    while (!text.empty() && text.back() == '0') text.pop_back();
    if (!text.empty() && text.back() == '.') text.pop_back();
    return text;
}

Result EvaluateExpression(const std::string& input) {
    size_t begin = 0;
    size_t end = input.size();
    while (begin < end && std::isspace(static_cast<unsigned char>(input[begin]))) ++begin;
    while (end > begin && std::isspace(static_cast<unsigned char>(input[end - 1]))) --end;
    if (begin == end) return Result::Fail("INVALID_ARG", "算式为空");
    Parser parser(input.substr(begin, end - begin));
    const double value = parser.Expression();
    parser.Skip();
    if (parser.dividedByZero) return Result::Fail("INVALID_ARG", "除数不能为 0");
    if (parser.failed || !parser.End()) return Result::Fail("INVALID_ARG", "算式无法解析");
    return Result::Success(FormatNumber(value));
}

std::optional<std::string> ConvertUnit(const std::string& input) {
    double amount = 0;
    std::string from;
    std::string to;
    if (!ParseUnitQuery(input, amount, from, to)) return std::nullopt;
    if ((from == "c" || from == "f" || from == "k") && (to == "c" || to == "f" || to == "k")) {
        return FormatTemp(amount, from, to);
    }
    const auto& table = UnitTable();
    const auto left = table.find(from);
    const auto right = table.find(to);
    if (left == table.end() || right == table.end() || left->second.first != right->second.first || right->second.second == 0) {
        return std::nullopt;
    }
    const double value = amount * left->second.second / right->second.second;
    const Result formatted = EvaluateExpression(FormatNumber(value));
    if (!formatted.ok) return std::nullopt;
    return formatted.data + " " + to;
}

std::optional<Rgb> ParseColor(const std::string& input) {
    std::string text = input;
    size_t begin = 0;
    size_t end = text.size();
    while (begin < end && std::isspace(static_cast<unsigned char>(text[begin]))) ++begin;
    while (end > begin && std::isspace(static_cast<unsigned char>(text[end - 1]))) --end;
    text = text.substr(begin, end - begin);
    if (!text.empty() && text[0] == '#') text.erase(text.begin());
    if (text.size() == 3) {
        std::string expanded;
        for (char ch : text) {
            expanded.push_back(ch);
            expanded.push_back(ch);
        }
        text = expanded;
    }
    if (text.size() != 6) return std::nullopt;
    for (char ch : text) {
        const bool hex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
        if (!hex) return std::nullopt;
    }
    const int value = std::stoi(text, nullptr, 16);
    return Rgb{(value >> 16) & 255, (value >> 8) & 255, value & 255};
}

std::optional<std::string> DescribeColor(const std::string& input) {
    const auto rgb = ParseColor(input);
    if (!rgb) return std::nullopt;
    const double rf = rgb->r / 255.0;
    const double gf = rgb->g / 255.0;
    const double bf = rgb->b / 255.0;
    const double max = std::max(rf, std::max(gf, bf));
    const double min = std::min(rf, std::min(gf, bf));
    const double l = (max + min) / 2;
    double h = 0;
    double s = 0;
    if (max != min) {
        const double d = max - min;
        s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        if (max == rf) h = (gf - bf) / d + (gf < bf ? 6 : 0);
        else if (max == gf) h = (bf - rf) / d + 2;
        else h = (rf - gf) / d + 4;
        h /= 6;
    }
    const int hh = static_cast<int>(std::round(h * 360));
    const int ss = static_cast<int>(std::round(s * 100));
    const int ll = static_cast<int>(std::round(l * 100));
    char hex[8];
    std::snprintf(hex, sizeof(hex), "#%02X%02X%02X", rgb->r, rgb->g, rgb->b);
    return std::string(hex) + "  rgb(" + std::to_string(rgb->r) + "," + std::to_string(rgb->g) + "," + std::to_string(rgb->b) + ")  hsl(" +
           std::to_string(hh) + ", " + std::to_string(ss) + "%, " + std::to_string(ll) + "%)";
}

std::optional<std::string> DescribeTimestamp(const std::string& input) {
    std::string text = input;
    size_t begin = 0;
    size_t end = text.size();
    while (begin < end && std::isspace(static_cast<unsigned char>(text[begin]))) ++begin;
    while (end > begin && std::isspace(static_cast<unsigned char>(text[end - 1]))) --end;
    text = text.substr(begin, end - begin);
    const bool digits = !text.empty() && std::all_of(text.begin(), text.end(), [](char ch) { return ch >= '0' && ch <= '9'; });
    if (digits && (text.size() == 10 || text.size() == 13)) {
        long long raw = std::stoll(text);
        if (text.size() == 13) raw = (raw + 500) / 1000;
        FILETIME file{};
        ULARGE_INTEGER stamp;
        stamp.QuadPart = static_cast<uint64_t>(raw) * 10000000ULL + 116444736000000000ULL;
        file.dwLowDateTime = stamp.LowPart;
        file.dwHighDateTime = stamp.HighPart;
        FILETIME local{};
        FileTimeToLocalFileTime(&file, &local);
        SYSTEMTIME system{};
        FileTimeToSystemTime(&local, &system);
        char buffer[32];
        std::snprintf(buffer, sizeof(buffer), "%04u-%02u-%02u %02u:%02u:%02u", system.wYear, system.wMonth, system.wDay, system.wHour, system.wMinute, system.wSecond);
        return std::string(buffer);
    }
    int year = 0, month = 0, day = 0, hour = 0, minute = 0, second = 0;
    if (std::sscanf(text.c_str(), "%d-%d-%d %d:%d:%d", &year, &month, &day, &hour, &minute, &second) == 6) {
        SYSTEMTIME local{};
        local.wYear = static_cast<WORD>(year);
        local.wMonth = static_cast<WORD>(month);
        local.wDay = static_cast<WORD>(day);
        local.wHour = static_cast<WORD>(hour);
        local.wMinute = static_cast<WORD>(minute);
        local.wSecond = static_cast<WORD>(second);
        FILETIME localFile{};
        FILETIME utc{};
        if (!SystemTimeToFileTime(&local, &localFile) || !LocalFileTimeToFileTime(&localFile, &utc)) return std::nullopt;
        ULARGE_INTEGER stamp;
        stamp.LowPart = utc.dwLowDateTime;
        stamp.HighPart = utc.dwHighDateTime;
        const long long unix = static_cast<long long>((stamp.QuadPart - 116444736000000000ULL) / 10000000ULL);
        return std::to_string(unix);
    }
    return std::nullopt;
}

std::pair<std::string, bool> TranslateText(const std::string& input) {
    static const std::unordered_map<std::string, std::string> dictionary = {
        {"hello world", "你好，世界"}, {"hello", "你好"}, {"world", "世界"}, {"thank you", "谢谢"},
        {"thanks", "谢谢"}, {"good morning", "早上好"}, {"deskkit", "DeskKit"}, {"search", "搜索"},
        {"file", "文件"}, {"copy", "复制"}, {"color", "颜色"}, {"time", "时间"}};
    std::string key = input;
    size_t begin = 0;
    size_t end = key.size();
    while (begin < end && std::isspace(static_cast<unsigned char>(key[begin]))) ++begin;
    while (end > begin && std::isspace(static_cast<unsigned char>(key[end - 1]))) --end;
    key = LowerAscii(key.substr(begin, end - begin));
    const auto found = dictionary.find(key);
    if (found != dictionary.end()) return {found->second, true};
    return {"未收录：" + input, true};
}

std::vector<std::string> MatchKeywords(const std::vector<IndexRow>& rows, const std::string& query) {
    std::string needle = query;
    size_t begin = 0;
    size_t end = needle.size();
    while (begin < end && std::isspace(static_cast<unsigned char>(needle[begin]))) ++begin;
    while (end > begin && std::isspace(static_cast<unsigned char>(needle[end - 1]))) --end;
    needle = needle.substr(begin, end - begin);
    std::vector<std::string> hits;
    if (needle.empty()) return hits;
    for (const IndexRow& row : rows) {
        if (row.kind != "app" && row.kind != "file" && row.kind != "url" && row.kind != "tool") continue;
        bool hit = ContainsInsensitive(row.title, needle);
        if (!hit) {
            for (const std::string& word : row.keywords) {
                if (ContainsInsensitive(word, needle)) {
                    hit = true;
                    break;
                }
            }
        }
        if (hit) hits.push_back(row.id);
    }
    return hits;
}

std::optional<std::string> ResolveTrigger(const ContractStore& store, const std::string& word) {
    const auto found = store.Trigger(word);
    if (!found) return std::nullopt;
    return found->toolId;
}

Result DeleteGroup(std::vector<GroupRecord>& groups, std::vector<ItemRecord>& items, const std::string& groupId, const std::string& ungroupedId) {
    if (groupId == ungroupedId) return Result::Fail("INVALID_ARG", "不能删除未分组");
    const auto index = std::find_if(groups.begin(), groups.end(), [&](const GroupRecord& group) { return group.id == groupId; });
    if (index == groups.end()) return Result::Fail("INVALID_ARG", "分组不存在");
    std::vector<std::string> removing = index->itemIds;
    groups.erase(index);
    for (GroupRecord& group : groups) {
        group.itemIds.erase(std::remove_if(group.itemIds.begin(), group.itemIds.end(), [&](const std::string& id) {
                                return std::find(removing.begin(), removing.end(), id) != removing.end();
                            }),
                            group.itemIds.end());
    }
    items.erase(std::remove_if(items.begin(), items.end(), [&](const ItemRecord& item) {
                    return std::find(removing.begin(), removing.end(), item.id) != removing.end();
                }),
                items.end());
    std::string joined;
    for (const std::string& id : removing) {
        if (!joined.empty()) joined += ",";
        joined += id;
    }
    return Result::Success(joined);
}

std::string KindOfPath(const std::string& path) {
    const auto dot = path.find_last_of('.');
    std::string ext = dot == std::string::npos ? "" : LowerAscii(path.substr(dot));
    if (ext == ".exe" || ext == ".lnk" || ext == ".app") return "app";
    return "file";
}

StarterBuilt ComposeStarter(const std::vector<StarterGroupSpec>& groups, const std::vector<StarterFound>& found, const StarterGroupSpec& ungrouped) {
    std::unordered_map<std::string, std::vector<StarterFound>> byGroup;
    for (const StarterFound& item : found) {
        if (item.id.find_first_not_of(' ') == std::string::npos || item.path.find_first_not_of(' ') == std::string::npos) continue;
        byGroup[item.groupId].push_back(item);
    }
    StarterBuilt built;
    for (const StarterGroupSpec& group : groups) {
        if (group.id == ungrouped.id) continue;
        const auto list = byGroup.find(group.id);
        if (list == byGroup.end() || list->second.empty()) continue;
        GroupRecord record{group.id, group.name, group.color, {}};
        for (const StarterFound& item : list->second) {
            record.itemIds.push_back(item.id);
            built.items.push_back(StarterShortcut{item.id, item.title, item.path, KindOfPath(item.path)});
        }
        built.groups.push_back(std::move(record));
    }
    built.groups.push_back(GroupRecord{ungrouped.id, ungrouped.name, ungrouped.color, {}});
    return built;
}

}  // namespace dk
