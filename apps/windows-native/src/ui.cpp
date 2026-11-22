#include "deskkit.hpp"

#include "imgui.h"
#include "imgui_stdlib.h"

#include <windows.h>
#include <shlobj.h>
#include <commdlg.h>

#include <algorithm>
#include <cctype>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <filesystem>

namespace dk {

float g_scale = 1.f;
float Px(double dip) { return static_cast<float>(dip * g_scale); }

ImVec4 Hex(const std::string& text) {
    unsigned value = 0;
    const char* cursor = text.c_str();
    if (cursor[0] == '#') ++cursor;
    std::sscanf(cursor, "%x", &value);
    return ImVec4(((value >> 16) & 255) / 255.f, ((value >> 8) & 255) / 255.f, (value & 255) / 255.f, 1);
}

void ApplyTheme(const Shell& shell) {
    const bool dark = shell.session->settings.theme == "dark" || (shell.session->settings.theme == "system" && IsSystemDark());
    ImGui::StyleColorsLight();
    ImGuiStyle& style = ImGui::GetStyle();
    style.Alpha = 1.f;
    style.WindowPadding = ImVec2(0, 0);
    style.WindowBorderSize = 0;
    style.WindowRounding = Px(14);
    style.FrameRounding = Px(8);
    style.ChildRounding = Px(10);
    style.Colors[ImGuiCol_WindowBg] = ImVec4(0, 0, 0, 0);
    style.Colors[ImGuiCol_ChildBg] = ImVec4(0, 0, 0, 0);
    style.Colors[ImGuiCol_Text] = Hex(shell.session->store.Color("text.primary", dark));
    // 浅色主题把光标画成黑色。窗口是深色的，黑光标会看不见，看起来像没有聚焦。
    style.Colors[ImGuiCol_InputTextCursor] = style.Colors[ImGuiCol_Text];
    style.InputTextCursorSize = std::max(1.f, Px(2));
    style.Colors[ImGuiCol_TextDisabled] = Hex(shell.session->store.Color("text.tertiary", dark));
    style.Colors[ImGuiCol_Border] = Hex(shell.session->store.Color("border.default", dark));
    style.Colors[ImGuiCol_FrameBg] = Hex(shell.session->store.Color("bg.input", dark));
    style.Colors[ImGuiCol_Button] = Hex(shell.session->store.Color("bg.subtle", dark));
    style.Colors[ImGuiCol_ButtonHovered] = Hex(shell.session->store.Color("bg.hover", dark));
    style.Colors[ImGuiCol_ButtonActive] = Hex(shell.session->store.Color("accent.soft", dark));
    style.Colors[ImGuiCol_Header] = Hex(shell.session->store.Color("bg.selected", dark));
    style.Colors[ImGuiCol_HeaderHovered] = Hex(shell.session->store.Color("bg.hover", dark));
    style.Colors[ImGuiCol_HeaderActive] = Hex(shell.session->store.Color("accent.soft", dark));
    style.Colors[ImGuiCol_PopupBg] = Hex(shell.session->store.Color("bg.float", dark));
    style.Colors[ImGuiCol_PopupBg].w = 0.98f;
    // 列表行自己占满契约高度，不再额外垫一截。按钮和输入框仍用 8/4 的内边距。
    style.ItemSpacing = ImVec2(Px(8), Px(4));
    style.ItemInnerSpacing = ImVec2(Px(6), Px(4));
    style.FramePadding = ImVec2(Px(8), Px(4));
    style.ScrollbarSize = Px(8);
    style.ScrollbarRounding = Px(4);
    style.PopupRounding = Px(8);
    style.PopupBorderSize = 1.f;
}

bool LoadUiFont(float scale) {
    g_scale = scale < 1.f ? 1.f : scale;
    ImGuiIO& io = ImGui::GetIO();
    // msyh.ttc 第 0 面是微软雅黑，第 1 面是微软雅黑 UI，和原来 WPF 的 FontFamily 一致。
    const char* fonts[] = {"C:\\Windows\\Fonts\\msyh.ttc", "C:\\Windows\\Fonts\\msyh.ttf", "C:\\Windows\\Fonts\\segoeui.ttf", "C:\\Windows\\Fonts\\simhei.ttf"};
    const ImWchar* ranges = io.Fonts->GetGlyphRangesChineseSimplifiedCommon();
    for (const char* font : fonts) {
        if (GetFileAttributesA(font) == INVALID_FILE_ATTRIBUTES) continue;
        auto add = [&](float size, int face) -> ImFont* {
            ImFontConfig config;
            config.FontNo = face;
            config.PixelSnapH = false;
            return io.Fonts->AddFontFromFileTTF(font, size * g_scale, &config, ranges);
        };
        // 微软雅黑（第 0 面）比雅黑 UI 更醒目。字号比契约大一档，避免界面字看起来偏小。
        int face = 0;
        ImFont* body = add(16.f, face);
        if (!body) {
            face = 1;
            body = add(16.f, face);
        }
        if (!body) continue;
        g_smallFont = add(14.f, face);
        g_inputFont = add(20.f, face);
        if (!g_inputFont) g_inputFont = body;
        static const ImWchar icons[] = {0xE700, 0xF8FF, 0};
        if (GetFileAttributesA("C:\\Windows\\Fonts\\segmdl2.ttf") != INVALID_FILE_ATTRIBUTES) {
            g_iconFont = io.Fonts->AddFontFromFileTTF("C:\\Windows\\Fonts\\segmdl2.ttf", 16.f * g_scale, nullptr, icons);
        }
        return true;
    }
    io.Fonts->AddFontDefault();
    return false;
}

void PresentHost(void* raw, Shell& shell) {
    HWND hwnd = static_cast<HWND>(raw);
    const float dpi = GetDpiForWindow(hwnd) / 96.f;
    if (dpi > 0.5f) g_scale = dpi;
    SyncPicker(shell);
    const bool visible = shell.showLauncher || shell.showGroups || shell.showTools || shell.showSettings || shell.showGuide || shell.pickingColor;
    static int placedX = 0x7fffffff;
    static int placedY = 0;
    static int placedW = 0;
    static int placedH = 0;
    if (!visible) {
        if (IsWindowVisible(hwnd)) ShowWindow(hwnd, SW_HIDE);
        placedX = 0x7fffffff;
        return;
    }
    MonitorBox box = shell.session->settings.multiScreen == "main" ? PrimaryMonitor() : MonitorAtCursor();
    int width = 800;
    int height = 480;
    if (shell.pickingColor) {
        width = box.w;
        height = box.h;
    } else if (shell.showGuide) {
        width = static_cast<int>(Px(shell.session->store.Layout({"windows", "guide", "width"})));
        height = static_cast<int>(Px(shell.session->store.Layout({"windows", "guide", "height"})));
    } else if (shell.showSettings) {
        width = static_cast<int>(Px(shell.session->store.Layout({"windows", "settings", "width"})));
        height = static_cast<int>(Px(shell.session->store.Layout({"windows", "settings", "height"})));
    } else if (shell.showTools) {
        const bool page = shell.toolMode == "page";
        width = static_cast<int>(Px(shell.session->store.Layout({"tools", page ? "pageWidth" : "gridWidth"})));
        height = static_cast<int>(Px(shell.session->store.Layout({"tools", page ? "pageHeight" : "gridHeight"})));
    } else if (shell.showGroups) {
        const auto& saved = shell.session->settings;
        if (saved.panelPlacedByUser && saved.panelWidth >= 760 && saved.panelHeight >= 480) {
            width = static_cast<int>(std::lround(saved.panelWidth * g_scale));
            height = static_cast<int>(std::lround(saved.panelHeight * g_scale));
        } else {
            width = static_cast<int>(Px(shell.session->store.Layout({"windows", "panel", "width"})));
            height = static_cast<int>(Px(shell.session->store.Layout({"windows", "panel", "height"})));
        }
    } else {
        width = static_cast<int>(Px(shell.session->store.Layout({"launcher", "width"})));
        height = static_cast<int>(Px(shell.session->store.Layout({"launcher", "defaultHeight"})));
    }
    const int inset = static_cast<int>(Px(shell.session->store.Layout({"launcher", "edgeInset"})));
    const int shadow = shell.pickingColor ? 0 : static_cast<int>(ShadowPad(shell));
    const bool groups = shell.showGroups && !shell.showLauncher && !shell.showTools && !shell.showSettings && !shell.showGuide && !shell.pickingColor;
    int anchorX = box.x;
    int anchorY = box.y;
    const int edge = groups ? 0 : inset;
    const auto clampToWork = [&](int& px, int& py) {
        POINT point{px + width / 2, py + height / 2};
        MONITORINFO info{};
        info.cbSize = sizeof(info);
        if (!GetMonitorInfoW(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST), &info)) return;
        const int left = info.rcWork.left + edge;
        const int top = info.rcWork.top + edge;
        const int right = std::max(left, static_cast<int>(info.rcWork.right) - width - edge);
        const int bottom = std::max(top, static_cast<int>(info.rcWork.bottom) - height - edge);
        px = std::min(std::max(px, left), right);
        py = std::min(std::max(py, top), bottom);
    };
    if (groups && shell.session->settings.panelPlacedByUser) {
        anchorX = static_cast<int>(std::lround(shell.session->settings.panelX * g_scale));
        anchorY = static_cast<int>(std::lround(shell.session->settings.panelY * g_scale));
        clampToWork(anchorX, anchorY);
    } else if (placedX != 0x7fffffff && placedW == width + shadow * 2 && placedH == height + shadow * 2) {
        anchorX = placedX + shadow;
        anchorY = placedY + shadow;
    } else if (!shell.pickingColor && groups) {
        POINT cursor;
        GetCursorPos(&cursor);
        anchorX = cursor.x - std::min(180, width / 3);
        anchorY = cursor.y - 36;
        clampToWork(anchorX, anchorY);
    } else if (!shell.pickingColor) {
        anchorX = box.x + std::max(inset, (box.w - width) / 2);
        if (shell.showLauncher && !shell.showGroups && !shell.showTools && !shell.showSettings && !shell.showGuide) {
            anchorY = box.y + static_cast<int>(box.h * shell.session->store.Layout({"launcher", "topRatio"}));
        } else {
            anchorY = box.y + std::max(inset, (box.h - height) / 2);
        }
    }
    const int x = anchorX - shadow;
    const int y = anchorY - shadow;
    const int w = width + shadow * 2;
    const int h = height + shadow * 2;
    // 位置和大小没变就不要再 SetWindowPos。每帧都改置顶，合成器会把窗口抖一下。
    if (!IsWindowVisible(hwnd) || x != placedX || y != placedY || w != placedW || h != placedH) {
        SetWindowPos(hwnd, HWND_TOPMOST, x, y, w, h, SWP_SHOWWINDOW | SWP_NOACTIVATE | SWP_NOCOPYBITS);
        placedX = x;
        placedY = y;
        placedW = w;
        placedH = h;
    }
    if (!shell.wantFocus) return;
    shell.wantFocus = false;
    if (GetForegroundWindow() == hwnd) return;
    const HWND foreground = GetForegroundWindow();
    const DWORD theirThread = GetWindowThreadProcessId(foreground, nullptr);
    const DWORD ourThread = GetCurrentThreadId();
    const bool attach = theirThread != 0 && theirThread != ourThread;
    if (attach) AttachThreadInput(theirThread, ourThread, TRUE);
    SetForegroundWindow(hwnd);
    SetFocus(hwnd);
    if (attach) AttachThreadInput(theirThread, ourThread, FALSE);
}

void TickBlur(Shell& shell) {
    if (shell.suppressBlur || shell.pickingColor || !shell.session->settings.blurHide || (!shell.showLauncher && !shell.showGroups)) {
        shell.blurSince = 0;
        if (!shell.showLauncher && !shell.showGroups) shell.hadFocus = false;
        return;
    }
    DWORD process = 0;
    GetWindowThreadProcessId(GetForegroundWindow(), &process);
    if (process == GetCurrentProcessId()) {
        shell.hadFocus = true;
        shell.blurSince = 0;
        return;
    }
    // 刚呼出的几百毫秒里，Alt 松开会让焦点抖一下。这段时间不能收起，否则看起来像呼出失败。
    if (GetTickCount64() < shell.blurGraceUntil) {
        shell.blurSince = 0;
        return;
    }
    // 和原来的 WPF 一样：只有曾经真正激活过，再点到外面，才开始 150ms 倒计时。
    // 刚呼出时如果焦点还没抢到，不能立刻藏掉，否则窗口会闪一下就消失。
    if (!shell.hadFocus || IsLeftButtonDown()) {
        shell.blurSince = 0;
        return;
    }
    const uint64_t now = GetTickCount64();
    if (shell.blurSince == 0) shell.blurSince = now;
    const auto wait = static_cast<uint64_t>(shell.session->store.Layout({"motionMs", "blurHide"}));
    if (now - shell.blurSince >= (wait == 0 ? 150 : wait)) {
        if (shell.showLauncher) {
            LogInfo("失去焦点，收起浮层");
            shell.session->Hide();
        }
        shell.showGroups = false;
        shell.blurSince = 0;
    }
}

void DrawInterface(Shell& shell) {
    ApplyTheme(shell);
    ImGui::GetStyle().Alpha = 1.f;
    const float pad = shell.pickingColor ? 0.f : ShadowPad(shell);
    const ImGuiViewport* viewport = ImGui::GetMainViewport();
    ImGui::SetNextWindowPos(viewport->WorkPos);
    ImGui::SetNextWindowSize(viewport->WorkSize);
    ImGui::Begin("DeskKitSurface", nullptr, ImGuiWindowFlags_NoDecoration | ImGuiWindowFlags_NoMove | ImGuiWindowFlags_NoBringToFrontOnFocus | ImGuiWindowFlags_NoBackground | ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoScrollWithMouse);
    if (pad > 0.f) {
        DrawPanelShadow(shell, pad);
        ImGui::SetCursorPos(ImVec2(pad, pad));
        ImGui::BeginChild("panel", ImVec2(ImGui::GetWindowWidth() - pad * 2.f, ImGui::GetWindowHeight() - pad * 2.f), ImGuiChildFlags_None, ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoBackground);
    }
    if (!shell.pickingColor) PaintChrome(shell);
    if (shell.pickingColor) DrawPicker(shell);
    else if (shell.showGuide) DrawGuide(shell);
    else if (shell.showSettings) DrawSettings(shell);
    else if (shell.showTools) DrawTools(shell);
    else if (shell.showGroups) DrawGroups(shell);
    else if (shell.showLauncher) DrawLauncher(shell);
    if (!shell.pickingColor) FinishChrome(shell);
    if (pad > 0.f) ImGui::EndChild();
    ImGui::End();
    TickBlur(shell);
}

void FocusOnly(Shell& shell, bool launcher) {
    shell.showGuide = false;
    shell.showSettings = false;
    shell.showTools = false;
    shell.showGroups = false;
    shell.showLauncher = false;
    if (launcher) shell.showLauncher = true;
    // 热键刚把窗口拉起来时，系统可能补一次 Esc。只丢掉这一小段，避免用户自己的 Esc 被连着吃掉。
    shell.ignoreEscapeUntil = GetTickCount64() + 150;
    shell.escapeSeenUp = false;
    shell.blurGraceUntil = GetTickCount64() + 300;
    ++shell.generation;
    shell.hadFocus = false;
    shell.blurSince = 0;
    shell.wantFocus = launcher;
    shell.focusQuery = launcher ? 12 : 0;
    if (ImGui::GetCurrentContext()) ImGui::GetIO().AddKeyEvent(ImGuiKey_Escape, false);
}

void Shell::ShowLauncher() { FocusOnly(*this, true); }
void Shell::HideLauncher() { showLauncher = false; }

void Shell::ShowGroups() {
    if (session && session->machine.State() != "HIDDEN") session->Hide();
    FocusOnly(*this, false);
    showGroups = true;
    wantFocus = true;
    if (selectedGroup.empty()) selectedGroup = "all";
}

void Shell::ToggleGroups() {
    if (showGroups) {
        showGroups = false;
        ++generation;
        LogInfo("热键隐藏快捷启动");
        return;
    }
    LogInfo("热键唤出快捷启动");
    ShowGroups();
}

void Shell::ShowTools() {
    if (session && session->machine.State() != "HIDDEN") session->Hide();
    FocusOnly(*this, false);
    showTools = true;
    toolMode = "grid";
    wantFocus = true;
}

void Shell::ShowTool(const std::string& id) {
    if (session && session->machine.State() != "HIDDEN") session->Hide();
    FocusOnly(*this, false);
    showTools = true;
    toolMode = "page";
    activeTool = id;
    wantFocus = true;
    if (id == "calc") toolInput = "520+999-1314";
    else if (id == "unit") toolInput = "3km to mi";
    else if (id == "translate") toolInput = "hello world";
    else if (id == "color") toolInput = "#3B6CF6";
    else if (id == "timestamp") toolInput = "1730000000";
    else toolInput.clear();
    if (id == "unit") toolOutput = ConvertUnit(toolInput).value_or("—");
    else if (id == "calc") toolOutput = EvaluateExpression(toolInput).data;
    else if (id == "translate") toolOutput = TranslateText(toolInput).first;
    else if (id == "color") toolOutput = DescribeColor(toolInput).value_or("");
    else if (id == "timestamp") toolOutput = DescribeTimestamp(toolInput).value_or("");
    else toolOutput.clear();
}

void Shell::CloseTool() {
    if (toolMode == "page") {
        toolMode = "grid";
        ++generation;
    } else {
        showTools = false;
        ++generation;
    }
}

void Shell::ShowSettings(const std::string& tab) {
    if (session && session->machine.State() != "HIDDEN") session->Hide();
    settingsTab = tab;
    FocusOnly(*this, false);
    showSettings = true;
    wantFocus = true;
}

void Shell::ShowGuide() {
    guideStep = 1;
    FocusOnly(*this, false);
    showGuide = true;
    wantFocus = true;
}

void Shell::NoteHotkey(bool ready) {
    if (hotkeyReady == ready) return;
    hotkeyReady = ready;
    LogInfo(ready ? "呼出键已生效" : "呼出键未生效：组合键被占用");
}

std::string Shell::PanelHotkeyText() const {
    const auto found = session->settings.hotkeys.find("panel");
    if (found != session->settings.hotkeys.end() && !found->second.text.empty()) return found->second.text;
    return session->store.FormatChord("windows", "panel");
}

}  // namespace dk
