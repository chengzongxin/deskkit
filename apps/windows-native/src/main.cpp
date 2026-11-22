// 窗口和绘制走 DirectX 11。界面用 Dear ImGui，和 Maye Nano 同一类做法：
// 自己的进程里直接画，不打开 WebView，也不带 .NET 运行时。

#include "deskkit.hpp"

#include "imgui.h"
#include "imgui_impl_dx11.h"
#include "imgui_impl_win32.h"

#include <d3d11.h>
#include <dcomp.h>
#include <dwmapi.h>
#include <dxgi1_2.h>
#include <oleidl.h>
#include <shellapi.h>
#include <tchar.h>

#include <algorithm>
#include <cstdio>
#include <mutex>
#include <thread>
#include <vector>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")

extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);

namespace {

ID3D11Device* g_device = nullptr;
ID3D11DeviceContext* g_context = nullptr;
IDXGISwapChain* g_swap = nullptr;
ID3D11RenderTargetView* g_target = nullptr;
IDCompositionDevice* g_dcomp = nullptr;
IDCompositionTarget* g_dcompTarget = nullptr;
IDCompositionVisual* g_dcompVisual = nullptr;
HWND g_hwnd = nullptr;
dk::Shell* g_shell = nullptr;
std::string GroupUnderPointer(const dk::Shell& shell);
UINT g_tray = WM_APP + 1;
void RaiseHost() {
    // 先放到最终位置再显示。如果先在旧坐标露出来再挪过去，看起来就是抖一下。
    if (!g_hwnd || !g_shell) return;
    dk::PresentHost(g_hwnd, *g_shell);
}

bool CreateRenderTarget() {
    ID3D11Texture2D* back = nullptr;
    if (FAILED(g_swap->GetBuffer(0, IID_PPV_ARGS(&back)))) return false;
    const HRESULT created = g_device->CreateRenderTargetView(back, nullptr, &g_target);
    back->Release();
    return SUCCEEDED(created);
}

bool AttachComposition(HWND hwnd, IDXGIDevice* dxgiDevice, IDXGISwapChain1* swap) {
    // 这台机器不允许窗口交换链带透明通道，改由桌面合成器把画面贴到窗口上。
    IDCompositionDevice* device = nullptr;
    if (FAILED(DCompositionCreateDevice(dxgiDevice, IID_PPV_ARGS(&device)))) return false;
    IDCompositionTarget* target = nullptr;
    IDCompositionVisual* visual = nullptr;
    const bool ok = SUCCEEDED(device->CreateTargetForHwnd(hwnd, TRUE, &target)) && SUCCEEDED(device->CreateVisual(&visual)) && SUCCEEDED(visual->SetContent(swap)) && SUCCEEDED(target->SetRoot(visual)) && SUCCEEDED(device->Commit());
    if (!ok) {
        if (visual) visual->Release();
        if (target) target->Release();
        device->Release();
        return false;
    }
    g_dcomp = device;
    g_dcompTarget = target;
    g_dcompVisual = visual;
    return true;
}

bool CreateDevice(HWND hwnd) {
    // 先试透明合成，圆角外面才能看见桌面。失败就退回不透明，避免起不来。
    RECT rect{};
    GetClientRect(hwnd, &rect);
    const UINT width = std::max(1, static_cast<int>(rect.right - rect.left));
    const UINT height = std::max(1, static_cast<int>(rect.bottom - rect.top));
    D3D_FEATURE_LEVEL level;
    const D3D_FEATURE_LEVEL levels[] = {D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_0};
    if (FAILED(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, levels, 2, D3D11_SDK_VERSION, &g_device, &level, &g_context))) return false;
    IDXGIDevice* dxgiDevice = nullptr;
    IDXGIAdapter* adapter = nullptr;
    IDXGIFactory2* factory = nullptr;
    if (FAILED(g_device->QueryInterface(IID_PPV_ARGS(&dxgiDevice))) || FAILED(dxgiDevice->GetAdapter(&adapter)) || FAILED(adapter->GetParent(IID_PPV_ARGS(&factory)))) {
        if (adapter) adapter->Release();
        if (dxgiDevice) dxgiDevice->Release();
        return false;
    }
    SetWindowLongW(hwnd, GWL_EXSTYLE, GetWindowLongW(hwnd, GWL_EXSTYLE) | 0x00200000);
    const MARGINS margins{-1, -1, -1, -1};
    DwmExtendFrameIntoClientArea(hwnd, &margins);
    DXGI_SWAP_CHAIN_DESC1 desc{};
    desc.Width = width;
    desc.Height = height;
    desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    desc.BufferCount = 2;
    desc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL;
    desc.AlphaMode = DXGI_ALPHA_MODE_PREMULTIPLIED;
    desc.Scaling = DXGI_SCALING_STRETCH;
    IDXGISwapChain1* swap = nullptr;
    const HRESULT created = factory->CreateSwapChainForComposition(g_device, &desc, nullptr, &swap);
    if (SUCCEEDED(created) && AttachComposition(hwnd, dxgiDevice, swap)) {
        g_swap = swap;
        dk::LogInfo("透明合成已创建");
    } else if (swap) {
        swap->Release();
    }
    factory->Release();
    adapter->Release();
    dxgiDevice->Release();
    if (!g_swap) {
        char text[96];
        std::snprintf(text, sizeof(text), "透明合成失败 %08lX，改用不透明窗口", static_cast<unsigned long>(created));
        dk::LogWarn(text);
        if (g_context) g_context->Release();
        if (g_device) g_device->Release();
        g_context = nullptr;
        g_device = nullptr;
        SetWindowLongW(hwnd, GWL_EXSTYLE, GetWindowLongW(hwnd, GWL_EXSTYLE) & ~0x00200000);
        DXGI_SWAP_CHAIN_DESC legacy{};
        legacy.BufferCount = 2;
        legacy.BufferDesc.Width = width;
        legacy.BufferDesc.Height = height;
        legacy.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
        legacy.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
        legacy.OutputWindow = hwnd;
        legacy.SampleDesc.Count = 1;
        legacy.Windowed = TRUE;
        legacy.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;
        if (FAILED(D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, levels, 2, D3D11_SDK_VERSION, &legacy, &g_swap, &g_device, &level, &g_context))) return false;
    }
    return CreateRenderTarget();
}

void CleanupDevice() {
    if (g_dcompVisual) g_dcompVisual->Release();
    if (g_dcompTarget) g_dcompTarget->Release();
    if (g_dcomp) g_dcomp->Release();
    if (g_target) g_target->Release();
    if (g_swap) g_swap->Release();
    if (g_context) g_context->Release();
    if (g_device) g_device->Release();
    g_dcompVisual = nullptr;
    g_dcompTarget = nullptr;
    g_dcomp = nullptr;
    g_target = nullptr;
    g_swap = nullptr;
    g_context = nullptr;
    g_device = nullptr;
}

HICON LoadAppIcon(HINSTANCE instance, int size) {
    HICON icon = static_cast<HICON>(LoadImageW(instance, MAKEINTRESOURCEW(1), IMAGE_ICON, size, size, LR_DEFAULTCOLOR));
    if (!icon) icon = LoadIconW(nullptr, IDI_APPLICATION);
    return icon;
}

void AddTray() {
    NOTIFYICONDATAW data{};
    data.cbSize = sizeof(data);
    data.hWnd = g_hwnd;
    data.uID = 1;
    data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    data.uCallbackMessage = g_tray;
    data.hIcon = LoadAppIcon(GetModuleHandleW(nullptr), 32);
    lstrcpynW(data.szTip, L"DeskKit", 64);
    Shell_NotifyIconW(NIM_ADD, &data);
}

void RemoveTray() {
    NOTIFYICONDATAW data{};
    data.cbSize = sizeof(data);
    data.hWnd = g_hwnd;
    data.uID = 1;
    Shell_NotifyIconW(NIM_DELETE, &data);
}

void ShowTrayMenu() {
    HMENU menu = CreatePopupMenu();
    AppendMenuW(menu, MF_STRING, 1, L"呼出");
    std::wstring panel = L"快捷启动  ";
    for (unsigned char ch : g_shell->PanelHotkeyText()) panel.push_back(ch);
    AppendMenuW(menu, MF_STRING, 2, panel.c_str());
    AppendMenuW(menu, MF_STRING, 3, L"常用工具");
    AppendMenuW(menu, MF_STRING, 4, L"设置");
    AppendMenuW(menu, MF_STRING, 5, L"重新注册呼出键");
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, 6, L"退出");
    POINT point;
    GetCursorPos(&point);
    SetForegroundWindow(g_hwnd);
    const int command = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON, point.x, point.y, 0, g_hwnd, nullptr);
    DestroyMenu(menu);
    if (!g_shell || !g_shell->session) return;
    if (command == 1) g_shell->session->Summon();
    if (command == 2) g_shell->ShowGroups();
    if (command == 3) g_shell->ShowTools();
    if (command == 4) g_shell->ShowSettings();
    if (command == 5 && g_shell->onHotkeyChanged) g_shell->onHotkeyChanged();
    if (command == 6 && MessageBoxW(nullptr, L"退出后热键和剪贴板记录都会停掉。", L"退出 DeskKit", MB_OKCANCEL | MB_ICONQUESTION) == IDOK) {
        dk::LogInfo("退出");
        g_shell->quitRequested = true;
    }
}

LRESULT GroupEdgeHit(HWND hwnd, LPARAM lParam) {
    const int x = static_cast<int>(static_cast<short>(LOWORD(lParam)));
    const int y = static_cast<int>(static_cast<short>(HIWORD(lParam)));
    RECT rect{};
    GetWindowRect(hwnd, &rect);
    const int edge = 7;
    const bool left = x >= rect.left && x < rect.left + edge;
    const bool right = x <= rect.right && x > rect.right - edge;
    const bool top = y >= rect.top && y < rect.top + edge;
    const bool bottom = y <= rect.bottom && y > rect.bottom - edge;
    if (top && left) return HTTOPLEFT;
    if (top && right) return HTTOPRIGHT;
    if (bottom && left) return HTBOTTOMLEFT;
    if (bottom && right) return HTBOTTOMRIGHT;
    if (left) return HTLEFT;
    if (right) return HTRIGHT;
    if (top) return HTTOP;
    if (bottom) return HTBOTTOM;
    return HTCLIENT;
}

void RememberGroupFrame(HWND hwnd) {
    if (!g_shell || !g_shell->session) return;
    RECT rect{};
    GetWindowRect(hwnd, &rect);
    float scale = GetDpiForWindow(hwnd) / 96.f;
    if (scale < 0.5f) scale = 1.f;
    auto& settings = g_shell->session->settings;
    settings.panelPlacedByUser = true;
    settings.panelX = rect.left / scale;
    settings.panelY = rect.top / scale;
    settings.panelWidth = (rect.right - rect.left) / scale;
    settings.panelHeight = (rect.bottom - rect.top) / scale;
    g_shell->session->Save();
    dk::LogInfo("记住快捷启动大小");
}

LRESULT CALLBACK WndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam) {
    if (g_shell && g_shell->showGroups && msg == WM_NCHITTEST) {
        const LRESULT hit = GroupEdgeHit(hwnd, lParam);
        if (hit != HTCLIENT) return hit;
    }
    if (g_shell && g_shell->showGroups && msg == WM_GETMINMAXINFO) {
        float scale = GetDpiForWindow(hwnd) / 96.f;
        if (scale < 0.5f) scale = 1.f;
        auto* info = reinterpret_cast<MINMAXINFO*>(lParam);
        info->ptMinTrackSize.x = static_cast<LONG>(760 * scale);
        info->ptMinTrackSize.y = static_cast<LONG>(480 * scale);
        return 0;
    }
    if (g_shell && g_shell->showGroups && msg == WM_EXITSIZEMOVE) RememberGroupFrame(hwnd);
    // 只丢掉按下，抬起要放行。否则 Esc 会一直停在“按着”，下一次窗口刚出来就被关掉。
    if (g_shell && wParam == VK_ESCAPE && GetTickCount64() < g_shell->ignoreEscapeUntil &&
        (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)) {
        return 0;
    }
    if (ImGui_ImplWin32_WndProcHandler(hwnd, msg, wParam, lParam)) return true;
    if (msg == g_tray) {
        if (lParam == WM_LBUTTONUP && g_shell && g_shell->session) g_shell->session->Summon();
        if (lParam == WM_RBUTTONUP) ShowTrayMenu();
        return 0;
    }
    if (msg == WM_SYSCOMMAND && (wParam & 0xFFF0) == SC_KEYMENU) return 0;
    if (msg == WM_DROPFILES && g_shell && g_shell->session && g_shell->showGroups) {
        HDROP drop = reinterpret_cast<HDROP>(wParam);
        const UINT count = DragQueryFileW(drop, 0xFFFFFFFF, nullptr, 0);
        POINT point{};
        if (DragQueryPoint(drop, &point)) {
            g_shell->dropX = static_cast<float>(point.x);
            g_shell->dropY = static_cast<float>(point.y);
        }
        const std::string group = GroupUnderPointer(*g_shell);
        for (UINT index = 0; index < count; ++index) {
            wchar_t path[MAX_PATH] = {};
            DragQueryFileW(drop, index, path, MAX_PATH);
            char utf8[MAX_PATH * 3] = {};
            WideCharToMultiByte(CP_UTF8, 0, path, -1, utf8, sizeof(utf8), nullptr, nullptr);
            g_shell->session->AddShortcut(utf8, group);
        }
        DragFinish(drop);
        return 0;
    }
    if (msg == WM_CLOSE) {
        if (g_shell) {
            g_shell->showLauncher = false;
            g_shell->showGroups = false;
            g_shell->showTools = false;
            g_shell->showSettings = false;
            g_shell->showGuide = false;
            g_shell->pickingColor = false;
            if (g_shell->session && g_shell->session->machine.State() != "HIDDEN") g_shell->session->Hide();
        }
        ShowWindow(hwnd, SW_HIDE);
        return 0;
    }
    if (msg == WM_SIZE && g_device && wParam != SIZE_MINIMIZED) {
        if (g_target) {
            g_target->Release();
            g_target = nullptr;
        }
        g_swap->ResizeBuffers(0, LOWORD(lParam), HIWORD(lParam), DXGI_FORMAT_UNKNOWN, 0);
        if (g_dcomp) g_dcomp->Commit();
        ID3D11Texture2D* back = nullptr;
        g_swap->GetBuffer(0, IID_PPV_ARGS(&back));
        g_device->CreateRenderTargetView(back, nullptr, &g_target);
        back->Release();
    }
    if (msg == WM_DESTROY) {
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wParam, lParam);
}

void ApplyHotkey(dk::Shell& shell) {
    const dk::Settings& settings = shell.session->settings;
    const bool summonRecorded = settings.hotkeys.contains("summon");
    const int summonVk = dk::ToVirtualKey(settings.summonKeyCode, summonRecorded);
    dk::HotkeyBinding panel{};
    const auto found = settings.hotkeys.find("panel");
    if (found != settings.hotkeys.end()) panel = found->second;
    const bool panelRecorded = found != settings.hotkeys.end();
    const int panelVk = dk::ToVirtualKey(found == settings.hotkeys.end() ? 2 : panel.keyCode, panelRecorded);
    const bool ok = dk::InstallHotkeys({
        dk::HotkeySlot{1, settings.summonAlt, settings.summonControl, settings.summonShift, settings.summonCommand, summonVk},
        dk::HotkeySlot{2, found == settings.hotkeys.end() ? true : panel.alt, panel.control, panel.shift, panel.command, panelVk},
    });
    shell.NoteHotkey(ok);
    dk::LogInfo(ok ? "呼出键已按设置重新注册" : "呼出键注册失败");
}

std::wstring FirstLine(std::wstring text) {
    const auto cut = text.find_first_of(L"\r\n");
    if (cut != std::wstring::npos) text.resize(cut);
    size_t index = 0;
    while (index < text.size() && iswspace(text[index])) ++index;
    return text.substr(index);
}

bool IsHttp(const std::wstring& text) {
    return _wcsnicmp(text.c_str(), L"http://", 7) == 0 || _wcsnicmp(text.c_str(), L"https://", 8) == 0;
}

std::wstring ReadWide(IDataObject* data, CLIPFORMAT format) {
    FORMATETC request{format, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
    STGMEDIUM medium{};
    if (FAILED(data->GetData(&request, &medium))) return {};
    std::wstring text;
    if (const auto* chars = static_cast<const wchar_t*>(GlobalLock(medium.hGlobal))) {
        text = chars;
        GlobalUnlock(medium.hGlobal);
    }
    ReleaseStgMedium(&medium);
    return text;
}

std::wstring ReadUrl(IDataObject* data) {
    const CLIPFORMAT wide = static_cast<CLIPFORMAT>(RegisterClipboardFormatW(L"UniformResourceLocatorW"));
    const CLIPFORMAT ansi = static_cast<CLIPFORMAT>(RegisterClipboardFormatW(L"UniformResourceLocator"));
    std::wstring text = FirstLine(ReadWide(data, wide));
    if (!IsHttp(text)) text = FirstLine(ReadWide(data, CF_UNICODETEXT));
    if (!IsHttp(text)) {
        FORMATETC request{ansi, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
        STGMEDIUM medium{};
        if (SUCCEEDED(data->GetData(&request, &medium))) {
            if (const auto* chars = static_cast<const char*>(GlobalLock(medium.hGlobal))) {
                wchar_t converted[2048] = {};
                MultiByteToWideChar(CP_UTF8, 0, chars, -1, converted, 2048);
                text = FirstLine(converted);
                GlobalUnlock(medium.hGlobal);
            }
            ReleaseStgMedium(&medium);
        }
    }
    return IsHttp(text) ? text : std::wstring{};
}

bool HasFiles(IDataObject* data) {
    FORMATETC request{CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
    return data->QueryGetData(&request) == S_OK;
}

std::string GroupUnderPointer(const dk::Shell& shell) {
    for (const dk::GroupHit& hit : shell.groupHits) {
        if (shell.dropX >= hit.x && shell.dropX < hit.x + hit.w && shell.dropY >= hit.y && shell.dropY < hit.y + hit.h) return hit.id;
    }
    if (!shell.selectedGroup.empty() && shell.selectedGroup != "all") return shell.selectedGroup;
    return "ungrouped";
}

struct PanelDrop final : IDropTarget {
    LONG refs = 1;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override {
        if (!object) return E_POINTER;
        if (iid == IID_IUnknown || iid == IID_IDropTarget) {
            *object = static_cast<IDropTarget*>(this);
            AddRef();
            return S_OK;
        }
        *object = nullptr;
        return E_NOINTERFACE;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return static_cast<ULONG>(InterlockedIncrement(&refs)); }
    ULONG STDMETHODCALLTYPE Release() override { return static_cast<ULONG>(InterlockedDecrement(&refs)); }
    void NoteDropPoint(POINTL screen) {
        if (!g_hwnd || !g_shell) return;
        POINT point{screen.x, screen.y};
        ScreenToClient(g_hwnd, &point);
        g_shell->dropX = static_cast<float>(point.x);
        g_shell->dropY = static_cast<float>(point.y);
    }
    HRESULT STDMETHODCALLTYPE DragEnter(IDataObject* data, DWORD, POINTL screen, DWORD* effect) override {
        const CLIPFORMAT urlWide = static_cast<CLIPFORMAT>(RegisterClipboardFormatW(L"UniformResourceLocatorW"));
        const CLIPFORMAT urlAnsi = static_cast<CLIPFORMAT>(RegisterClipboardFormatW(L"UniformResourceLocator"));
        FORMATETC probe{urlWide, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
        const bool url = data && (data->QueryGetData(&probe) == S_OK || (probe.cfFormat = urlAnsi, data->QueryGetData(&probe) == S_OK) || !ReadUrl(data).empty());
        const bool ok = g_shell && g_shell->showGroups && data && (HasFiles(data) || url);
        if (effect) *effect = ok ? DROPEFFECT_COPY : DROPEFFECT_NONE;
        if (g_shell) {
            g_shell->externalDrop = ok;
            if (ok) {
                g_shell->suppressBlur = true;
                NoteDropPoint(screen);
            }
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE DragOver(DWORD, POINTL screen, DWORD* effect) override {
        if (g_shell && g_shell->externalDrop) NoteDropPoint(screen);
        const bool ok = g_shell && g_shell->showGroups && g_shell->externalDrop;
        if (effect) *effect = ok ? DROPEFFECT_COPY : DROPEFFECT_NONE;
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE DragLeave() override {
        if (g_shell) {
            g_shell->externalDrop = false;
            g_shell->suppressBlur = false;
        }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Drop(IDataObject* data, DWORD, POINTL screen, DWORD* effect) override {
        if (effect) *effect = DROPEFFECT_COPY;
        if (g_shell) NoteDropPoint(screen);
        if (g_shell && g_shell->session && g_shell->showGroups && data) {
            const std::string group = GroupUnderPointer(*g_shell);
            int added = 0;
            int total = 0;
            FORMATETC request{CF_HDROP, nullptr, DVASPECT_CONTENT, -1, TYMED_HGLOBAL};
            STGMEDIUM medium{};
            if (SUCCEEDED(data->GetData(&request, &medium))) {
                const auto drop = static_cast<HDROP>(medium.hGlobal);
                total = static_cast<int>(DragQueryFileW(drop, 0xFFFFFFFF, nullptr, 0));
                for (int index = 0; index < total; ++index) {
                    wchar_t path[MAX_PATH] = {};
                    DragQueryFileW(drop, index, path, MAX_PATH);
                    char utf8[MAX_PATH * 3] = {};
                    WideCharToMultiByte(CP_UTF8, 0, path, -1, utf8, sizeof(utf8), nullptr, nullptr);
                    if (g_shell->session->AddShortcut(utf8, group)) ++added;
                }
                ReleaseStgMedium(&medium);
            } else if (const std::wstring url = ReadUrl(data); !url.empty()) {
                total = 1;
                char utf8[4096] = {};
                WideCharToMultiByte(CP_UTF8, 0, url.c_str(), -1, utf8, sizeof(utf8), nullptr, nullptr);
                if (g_shell->session->AddUrl(utf8, group)) ++added;
            }
            std::string name = group;
            for (const dk::Group& item : g_shell->session->library.groups)
                if (item.id == group) name = item.name;
            dk::LogInfo("拖入完成，放入「" + name + "」" + std::to_string(added) + " 个，共 " + std::to_string(total) + " 个");
        }
        if (g_shell) {
            g_shell->externalDrop = false;
            g_shell->suppressBlur = false;
        }
        return S_OK;
    }
};

PanelDrop g_drop;
bool g_dropRegistered = false;

}  // namespace

int WINAPI WinMain(HINSTANCE instance, HINSTANCE, LPSTR, int) {
    const wchar_t* command = GetCommandLineW();
    OleInitialize(nullptr);
    const auto root = dk::ContractStore::Locate();
    if (!root) {
        MessageBoxW(nullptr, L"找不到 contracts 目录，DeskKit 无法启动。", L"DeskKit", MB_OK);
        return 1;
    }
    std::string pinyin = *root + "\\..\\apps\\windows-native\\assets\\pinyin.txt";
    if (GetFileAttributesW(L"assets\\pinyin.txt") != INVALID_FILE_ATTRIBUTES) pinyin = "assets\\pinyin.txt";
    wchar_t module[MAX_PATH];
    GetModuleFileNameW(nullptr, module, MAX_PATH);
    std::wstring beside = module;
    beside = beside.substr(0, beside.find_last_of(L'\\')) + L"\\assets\\pinyin.txt";
    if (GetFileAttributesW(beside.c_str()) != INVALID_FILE_ATTRIBUTES) {
        char utf8[MAX_PATH * 3];
        WideCharToMultiByte(CP_UTF8, 0, beside.c_str(), -1, utf8, sizeof(utf8), nullptr, nullptr);
        pinyin = utf8;
    }
    dk::LoadPinyinTable(pinyin);

    dk::Session session((dk::ContractStore(*root)));
    session.SetIndex(dk::ToolHits());
    dk::Shell shell;
    shell.session = &session;
    g_shell = &shell;
    session.onShow = [&] { shell.ShowLauncher(); };
    session.onHide = [&] { shell.HideLauncher(); };
    session.onOpenTool = [&](const std::string& id) {
        if (id == "grid") shell.ShowTools();
        else shell.ShowTool(id);
    };
    shell.onHotkeyChanged = [&] { ApplyHotkey(shell); };
    dk::SetHotkeyHandler([&](int id) {
        dk::ReleaseAlt();
        if (id == 1) {
            session.Summon();
            // 热键消息还在处理时，系统才允许我们把窗口拉到最前。拖到下一帧再抢，焦点会丢，浮层马上被收起。
            if (shell.showLauncher) RaiseHost();
        }
        if (id == 2) {
            shell.ToggleGroups();
            if (shell.showGroups) RaiseHost();
        }
    });
    ApplyHotkey(shell);

    std::mutex indexMu;
    std::vector<dk::IndexedEntry> indexed;
    bool indexReady = false;
    std::thread indexer([&] {
        CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
        try {
            auto rows = dk::CollectIndex();
            std::lock_guard lock(indexMu);
            indexed = std::move(rows);
            indexReady = true;
        } catch (const std::exception& ex) {
            dk::LogWarn(std::string("索引失败 ") + ex.what());
        }
        CoUninitialize();
    });

    WNDCLASSEXW klass{};
    klass.cbSize = sizeof(klass);
    klass.style = CS_CLASSDC;
    klass.lpfnWndProc = WndProc;
    klass.hInstance = instance;
    klass.lpszClassName = L"DeskKitHost";
    klass.hCursor = LoadCursor(nullptr, IDC_ARROW);
    klass.hIcon = LoadAppIcon(instance, 32);
    klass.hIconSm = LoadAppIcon(instance, 16);
    RegisterClassExW(&klass);
    // 这扇窗自己就是浮层：无标题栏、不进任务栏、置顶。不要再套一层 ImGui 标题栏。
    g_hwnd = CreateWindowExW(WS_EX_TOOLWINDOW | WS_EX_TOPMOST, klass.lpszClassName, L"DeskKit", WS_POPUP, 0, 0, 800, 480, nullptr, nullptr, instance, nullptr);
    shell.hostWindow = g_hwnd;
    SendMessageW(g_hwnd, WM_SETICON, ICON_BIG, reinterpret_cast<LPARAM>(klass.hIcon));
    SendMessageW(g_hwnd, WM_SETICON, ICON_SMALL, reinterpret_cast<LPARAM>(klass.hIconSm));
    if (!CreateDevice(g_hwnd)) return 1;
    dk::BindIconDevice(g_device);
    if (SUCCEEDED(RegisterDragDrop(g_hwnd, &g_drop))) g_dropRegistered = true;
    else DragAcceptFiles(g_hwnd, TRUE);
    const int corner = 2;
    DwmSetWindowAttribute(g_hwnd, 33, &corner, sizeof(corner));
    AddTray();

    IMGUI_CHECKVERSION();
    ImGui::CreateContext();
    ImGuiIO& io = ImGui::GetIO();
    io.IniFilename = nullptr;
    io.ConfigFlags &= ~ImGuiConfigFlags_ViewportsEnable;
    const float dpiScale = GetDpiForWindow(g_hwnd) / 96.f;
    dk::LoadUiFont(dpiScale < 1.f ? 1.f : dpiScale);
    ImGui_ImplWin32_Init(g_hwnd);
    ImGui_ImplDX11_Init(g_device, g_context);

    uint32_t clipboardSeq = dk::ClipboardSequence();
    uint64_t clipboardTick = GetTickCount64();
    const std::wstring args = command ? command : L"";
    const bool show = args.find(L"--show") != std::wstring::npos;
    if (!session.settings.guideDone || show) shell.ShowGuide();
    if (show) session.Summon();
    dk::LogInfo("DeskKit 已启动");

    bool done = false;
    while (!done && !shell.quitRequested) {
        MSG message;
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
            if (message.message == WM_QUIT) done = true;
        }
        if (done) break;
        if (indexReady) {
            std::vector<dk::Hit> hits = dk::ToolHits();
            {
                std::lock_guard lock(indexMu);
                for (const dk::IndexedEntry& entry : indexed) {
                    dk::Hit hit;
                    hit.id = entry.id;
                    hit.kind = entry.kind;
                    hit.title = entry.title;
                    hit.subtitle = entry.subtitle;
                    hit.path = entry.path;
                    hit.keywords = entry.keywords;
                    hit.score = entry.score;
                    hits.push_back(std::move(hit));
                }
                indexReady = false;
            }
            session.SetIndex(std::move(hits));
        }
        const uint64_t now = GetTickCount64();
        if (now - clipboardTick >= 800) {
            clipboardTick = now;
            const uint32_t next = dk::ClipboardSequence();
            if (next != clipboardSeq) {
                clipboardSeq = next;
                const dk::ClipboardSnapshot snap = dk::ReadClipboard();
                session.NoteClipboard(snap.text, snap.files);
            }
        }
        dk::PresentHost(g_hwnd, shell);
        const bool visible = shell.showLauncher || shell.showGroups || shell.showTools || shell.showSettings || shell.showGuide || shell.pickingColor;
        if (!visible) {
            Sleep(10);
            continue;
        }
        ImGui_ImplDX11_NewFrame();
        ImGui_ImplWin32_NewFrame();
        ImGui::NewFrame();
        dk::DrawInterface(shell);
        ImGui::Render();
        const float clear[4] = {0, 0, 0, 0};
        g_context->OMSetRenderTargets(1, &g_target, nullptr);
        g_context->ClearRenderTargetView(g_target, clear);
        ImGui_ImplDX11_RenderDrawData(ImGui::GetDrawData());
        g_swap->Present(1, 0);
    }

    indexer.join();
    dk::RemoveHotkeys();
    RemoveTray();
    ImGui_ImplDX11_Shutdown();
    ImGui_ImplWin32_Shutdown();
    ImGui::DestroyContext();
    CleanupDevice();
    if (g_dropRegistered) RevokeDragDrop(g_hwnd);
    DestroyWindow(g_hwnd);
    OleUninitialize();
    dk::LogInfo("退出");
    return 0;
}
