// 把原来 WPF 的浮层、分组卡片、工具宫格和设置卡片画出来。
// 色值和尺寸仍然只读契约。图标用系统文件图标和 Segoe MDL2，对应原来的 Glyph。

#include "deskkit.hpp"

#define IMGUI_DEFINE_MATH_OPERATORS
#include "imgui.h"
#include "imgui_internal.h"
#include "imgui_stdlib.h"

#include <windows.h>
#include <shellapi.h>
#include <commdlg.h>
#include <d3d11.h>
#include <wincodec.h>
#include <shlobj.h>

#include <algorithm>
#include <cctype>
#include <chrono>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <exception>
#include <filesystem>
#include <string>
#include <unordered_map>
#include <vector>

ImFont* g_inputFont = nullptr;
ImFont* g_iconFont = nullptr;
ImFont* g_smallFont = nullptr;

namespace dk {
namespace {

ID3D11Device* g_device = nullptr;
std::unordered_map<std::string, ImTextureID> g_icons;
std::string g_backgroundPath;
ImTextureID g_background = 0;
Shell* g_pick = nullptr;
HHOOK g_pickMouse = nullptr;
HHOOK g_pickKey = nullptr;

std::wstring Wide(const std::string& text) {
    if (text.empty()) return {};
    const int count = MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0);
    std::wstring wide(count, 0);
    MultiByteToWideChar(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), wide.data(), count);
    return wide;
}

std::string Utf8(const std::wstring& text) {
    if (text.empty()) return {};
    const int count = WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    std::string utf8(count, 0);
    WideCharToMultiByte(CP_UTF8, 0, text.data(), static_cast<int>(text.size()), utf8.data(), count, nullptr, nullptr);
    return utf8;
}

ImVec4 Hex(const std::string& text, float alpha = 1) {
    unsigned value = 0;
    const char* cursor = text.c_str();
    if (cursor[0] == '#') ++cursor;
    std::sscanf(cursor, "%x", &value);
    return ImVec4(((value >> 16) & 255) / 255.f, ((value >> 8) & 255) / 255.f, (value & 255) / 255.f, alpha);
}

bool Dark(const Shell& shell) {
    return shell.session->settings.theme == "dark" || (shell.session->settings.theme == "system" && IsSystemDark());
}

ImVec4 Paint(const Shell& shell, const char* name, float alpha = 1) {
    std::string key = name;
    if (key == "accent" && !shell.session->settings.accentToken.empty() && shell.session->settings.accentToken != "accent") key = shell.session->settings.accentToken;
    ImVec4 color = Hex(shell.session->store.Color(key, Dark(shell)), alpha);
    color.w *= alpha;
    return color;
}

ImU32 PaintU(const Shell& shell, const char* name, float alpha = 1) { return ImGui::ColorConvertFloat4ToU32(Paint(shell, name, alpha)); }

std::string FirstGlyph(const std::string& text) {
    if (text.empty()) return "?";
    const auto lead = static_cast<unsigned char>(text[0]);
    size_t count = 1;
    if (lead >= 0xF0) count = 4;
    else if (lead >= 0xE0) count = 3;
    else if (lead >= 0xC0) count = 2;
    return text.substr(0, std::min(count, text.size()));
}

const char* ToolGlyph(const std::string& id) {
    if (id == "clipboard") return "\uE8C8";
    if (id == "picker") return "\uE790";
    if (id == "translate") return "\uE8C1";
    if (id == "calc") return "\uE8EF";
    if (id == "timestamp") return "\uE823";
    if (id == "unit") return "\uE8AB";
    if (id == "color" || id == "edit") return "\uE70F";
    if (id == "back") return "\uE72B";
    if (id == "gear") return "\uE713";
    if (id == "close") return "\uE711";
    if (id == "plus") return "\uE710";
    if (id == "search") return "\uE721";
    if (id == "check") return "\uE73E";
    if (id == "down") return "\uE70D";
    if (id == "right") return "\uE76C";
    if (id == "link") return "\uE71B";
    if (id == "doc") return "\uE8A5";
    return "\uECAA";
}

const char* KindLabel(const std::string& kind) {
    if (kind == "tool") return "内置工具";
    if (kind == "app") return "应用";
    if (kind == "file") return "文件";
    if (kind == "url") return "网址";
    return "";
}

ImTextureID UploadBgra(const uint8_t* pixels, int width, int height) {
    if (!g_device || !pixels || width <= 0 || height <= 0) return 0;
    D3D11_TEXTURE2D_DESC desc{};
    desc.Width = static_cast<UINT>(width);
    desc.Height = static_cast<UINT>(height);
    desc.MipLevels = 1;
    desc.ArraySize = 1;
    desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
    desc.SampleDesc.Count = 1;
    desc.Usage = D3D11_USAGE_DEFAULT;
    desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
    D3D11_SUBRESOURCE_DATA data{};
    data.pSysMem = pixels;
    data.SysMemPitch = static_cast<UINT>(width * 4);
    ID3D11Texture2D* texture = nullptr;
    if (FAILED(g_device->CreateTexture2D(&desc, &data, &texture))) return 0;
    ID3D11ShaderResourceView* view = nullptr;
    g_device->CreateShaderResourceView(texture, nullptr, &view);
    texture->Release();
    return static_cast<ImTextureID>(reinterpret_cast<uintptr_t>(view));
}

ImTextureID TextureFromIcon(HICON icon, int size) {
    if (!icon) return 0;
    BITMAPINFO info{};
    info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    info.bmiHeader.biWidth = size;
    info.bmiHeader.biHeight = -size;
    info.bmiHeader.biPlanes = 1;
    info.bmiHeader.biBitCount = 32;
    info.bmiHeader.biCompression = BI_RGB;
    void* bits = nullptr;
    HDC screen = GetDC(nullptr);
    HDC memory = CreateCompatibleDC(screen);
    HBITMAP dib = CreateDIBSection(screen, &info, DIB_RGB_COLORS, &bits, nullptr, 0);
    const HGDIOBJ previous = SelectObject(memory, dib);
    DrawIconEx(memory, 0, 0, icon, size, size, 0, nullptr, DI_NORMAL);
    // 很多程序图标画进 32 位位图后颜色有了，透明通道却是 0，按纹理去画就会整张消失。
    auto* pixels = static_cast<uint32_t*>(bits);
    bool anyAlpha = false;
    const int count = size * size;
    for (int i = 0; i < count; ++i) {
        if ((pixels[i] >> 24) != 0) {
            anyAlpha = true;
            break;
        }
    }
    if (!anyAlpha) {
        for (int i = 0; i < count; ++i) {
            if ((pixels[i] & 0x00FFFFFFu) != 0) pixels[i] |= 0xFF000000u;
        }
    }
    ImTextureID texture = UploadBgra(static_cast<uint8_t*>(bits), size, size);
    SelectObject(memory, previous);
    DeleteObject(dib);
    DeleteDC(memory);
    ReleaseDC(nullptr, screen);
    return texture;
}

ImTextureID FileIcon(const std::string& path, int size) {
    if (!g_device || path.empty()) return 0;
    const std::string key = path + "#" + std::to_string(size);
    if (const auto found = g_icons.find(key); found != g_icons.end()) return found->second;
    const std::wstring wide = Wide(path);
    HICON icon = nullptr;
    std::string ext;
    const auto dot = path.find_last_of('.');
    if (dot != std::string::npos) {
        ext = path.substr(dot);
        for (char& ch : ext) ch = static_cast<char>(std::tolower(static_cast<unsigned char>(ch)));
    }
    // 应用和快捷方式用目标程序自己的图标。系统图像列表经常只给一个看不清的通用图标。
    if (ext == ".exe" || ext == ".lnk" || ext == ".ico") {
        HICON small = nullptr;
        if (ExtractIconExW(wide.c_str(), 0, &icon, &small, 1) > 0 && !icon) icon = small;
        else if (small && small != icon) DestroyIcon(small);
    }
    SHFILEINFOW info{};
    if (!icon) {
        const DWORD attributes = GetFileAttributesW(wide.c_str());
        const bool directory = attributes != INVALID_FILE_ATTRIBUTES && (attributes & FILE_ATTRIBUTE_DIRECTORY);
        DWORD flags = SHGFI_ICON | SHGFI_LARGEICON;
        if (!SHGetFileInfoW(wide.c_str(), directory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL, &info, sizeof(info), flags) || !info.hIcon) {
            flags |= SHGFI_USEFILEATTRIBUTES;
            SHGetFileInfoW(directory ? L"folder" : L"file.bin", directory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL, &info, sizeof(info), flags);
        }
        icon = info.hIcon;
    }
    const ImTextureID texture = TextureFromIcon(icon, size);
    if (icon) DestroyIcon(icon);
    g_icons[key] = texture;
    return texture;
}

ImTextureID BackgroundImage(const Shell& shell) {
    const std::string& path = shell.session->settings.backgroundImagePath;
    if (path.empty() || !g_device) return 0;
    if (path == g_backgroundPath) return g_background;
    g_backgroundPath = path;
    g_background = 0;
    IWICImagingFactory* factory = nullptr;
    if (FAILED(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&factory)))) return 0;
    IWICBitmapDecoder* decoder = nullptr;
    IWICBitmapFrameDecode* frame = nullptr;
    IWICFormatConverter* converter = nullptr;
    if (SUCCEEDED(factory->CreateDecoderFromFilename(Wide(path).c_str(), nullptr, GENERIC_READ, WICDecodeMetadataCacheOnLoad, &decoder)) && SUCCEEDED(decoder->GetFrame(0, &frame)) && SUCCEEDED(factory->CreateFormatConverter(&converter)) && SUCCEEDED(converter->Initialize(frame, GUID_WICPixelFormat32bppBGRA, WICBitmapDitherTypeNone, nullptr, 0, WICBitmapPaletteTypeCustom))) {
        UINT width = 0;
        UINT height = 0;
        converter->GetSize(&width, &height);
        if (width > 0 && height > 0 && width < 8000 && height < 8000) {
            std::vector<uint8_t> pixels(static_cast<size_t>(width) * height * 4);
            if (SUCCEEDED(converter->CopyPixels(nullptr, width * 4, static_cast<UINT>(pixels.size()), pixels.data()))) g_background = UploadBgra(pixels.data(), static_cast<int>(width), static_cast<int>(height));
        }
    }
    if (converter) converter->Release();
    if (frame) frame->Release();
    if (decoder) decoder->Release();
    factory->Release();
    return g_background;
}

void DrawGlyph(const char* glyph, ImU32 color, ImVec2 center, float size) {
    if (!g_iconFont) return;
    ImGui::PushFont(g_iconFont);
    const ImVec2 text = ImGui::CalcTextSize(glyph);
    ImGui::GetWindowDrawList()->AddText(ImVec2(center.x - text.x * 0.5f, center.y - text.y * 0.5f), color, glyph);
    ImGui::PopFont();
    (void)size;
}

bool ChevronButton(const Shell& shell, const char* id, bool folded) {
    ImGui::PushID(id);
    const float size = Px(20);
    const ImVec2 origin = ImGui::GetCursorScreenPos();
    const bool clicked = ImGui::InvisibleButton("##chevron", ImVec2(size, size));
    ImDrawList* draw = ImGui::GetWindowDrawList();
    const ImVec2 center(origin.x + size * 0.5f, origin.y + size * 0.5f);
    if (ImGui::IsItemHovered()) draw->AddCircleFilled(center, size * 0.5f, PaintU(shell, "bg.hover"));
    const float arm = Px(4.5f);
    const float thick = std::max(1.6f, Px(1.6f));
    const ImU32 color = PaintU(shell, ImGui::IsItemHovered() ? "text.primary" : "text.tertiary");
    if (folded) {
        draw->AddLine(ImVec2(center.x - arm * 0.35f, center.y - arm), ImVec2(center.x + arm * 0.55f, center.y), color, thick);
        draw->AddLine(ImVec2(center.x + arm * 0.55f, center.y), ImVec2(center.x - arm * 0.35f, center.y + arm), color, thick);
    } else {
        draw->AddLine(ImVec2(center.x - arm, center.y - arm * 0.25f), ImVec2(center.x, center.y + arm * 0.55f), color, thick);
        draw->AddLine(ImVec2(center.x, center.y + arm * 0.55f), ImVec2(center.x + arm, center.y - arm * 0.25f), color, thick);
    }
    ImGui::PopID();
    return clicked;
}

bool QuietIcon(const Shell& shell, const char* id, const char* glyph, const char* tip, bool active = false) {
    ImGui::PushID(id);
    const float size = Px(22);
    const ImVec2 origin = ImGui::GetCursorScreenPos();
    const bool clicked = ImGui::InvisibleButton("##quiet", ImVec2(size, size));
    const bool hover = ImGui::IsItemHovered();
    if (tip && hover) ImGui::SetTooltip("%s", tip);
    ImDrawList* draw = ImGui::GetWindowDrawList();
    if (hover) draw->AddCircleFilled(ImVec2(origin.x + size * 0.5f, origin.y + size * 0.5f), size * 0.5f, PaintU(shell, "bg.hover"));
    const float glyphSize = Px(13);
    const ImU32 color = PaintU(shell, active ? "accent" : (hover ? "text.secondary" : "text.tertiary"));
    if (g_iconFont) {
        const ImVec2 text = g_iconFont->CalcTextSizeA(glyphSize, FLT_MAX, 0.f, glyph);
        draw->AddText(g_iconFont, glyphSize, ImVec2(origin.x + (size - text.x) * 0.5f, origin.y + (size - text.y) * 0.5f), color, glyph);
    }
    ImGui::PopID();
    return clicked;
}

bool IconButton(const Shell& shell, const char* id, const char* glyph) {
    ImGui::PushID(id);
    const float size = Px(32);
    const ImVec2 origin = ImGui::GetCursorScreenPos();
    const bool clicked = ImGui::InvisibleButton("##icon", ImVec2(size, size));
    if (ImGui::IsItemHovered()) ImGui::GetWindowDrawList()->AddRectFilled(origin, ImVec2(origin.x + size, origin.y + size), PaintU(shell, "bg.hover"), Px(6));
    DrawGlyph(glyph, PaintU(shell, "text.secondary"), ImVec2(origin.x + size * 0.5f, origin.y + size * 0.5f), 16);
    ImGui::PopID();
    return clicked;
}

bool SoftButton(const Shell& shell, const char* id, const char* label, bool primary = false, ImVec2 size = ImVec2(0, 0)) {
    ImGui::PushID(id);
    ImGui::PushStyleColor(ImGuiCol_Button, Paint(shell, primary ? "accent" : "bg.subtle"));
    ImGui::PushStyleColor(ImGuiCol_ButtonHovered, Paint(shell, primary ? "accent.hover" : "bg.hover"));
    ImGui::PushStyleColor(ImGuiCol_ButtonActive, Paint(shell, "accent"));
    ImGui::PushStyleColor(ImGuiCol_Text, primary ? ImVec4(1, 1, 1, 1) : Paint(shell, "text.secondary"));
    ImGui::PushStyleColor(ImGuiCol_Border, Paint(shell, primary ? "accent" : "border.default"));
    ImGui::PushStyleVar(ImGuiStyleVar_FrameBorderSize, primary ? 0 : 1);
    ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, Px(8));
    ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(Px(12), Px(6)));
    const bool clicked = ImGui::Button(label, size);
    ImGui::PopStyleVar(3);
    ImGui::PopStyleColor(5);
    ImGui::PopID();
    return clicked;
}

bool Chip(const Shell& shell, const char* id, const char* label, bool selected) {
    ImGui::PushID(id);
    ImGui::PushStyleColor(ImGuiCol_Button, Paint(shell, selected ? "accent.soft" : "bg.subtle"));
    ImGui::PushStyleColor(ImGuiCol_ButtonHovered, Paint(shell, "bg.hover"));
    ImGui::PushStyleColor(ImGuiCol_Text, Paint(shell, selected ? "accent.hover" : "text.secondary"));
    ImGui::PushStyleColor(ImGuiCol_Border, Paint(shell, selected ? "accent" : "border.default"));
    ImGui::PushStyleVar(ImGuiStyleVar_FrameBorderSize, 1);
    ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, Px(8));
    ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(Px(10), Px(4)));
    const bool clicked = ImGui::Button(label);
    ImGui::PopStyleVar(3);
    ImGui::PopStyleColor(4);
    ImGui::PopID();
    return clicked;
}

bool Toggle(const Shell& shell, const char* id, bool* value) {
    ImGui::PushID(id);
    const float width = Px(40);
    const float height = Px(22);
    const ImVec2 origin = ImGui::GetCursorScreenPos();
    const bool clicked = ImGui::InvisibleButton("##switch", ImVec2(width, height));
    if (clicked) *value = !*value;
    ImDrawList* draw = ImGui::GetWindowDrawList();
    draw->AddRectFilled(origin, ImVec2(origin.x + width, origin.y + height), PaintU(shell, *value ? "accent" : "border.strong"), height * 0.5f);
    const float knob = Px(16);
    const float x = *value ? origin.x + width - knob - Px(3) : origin.x + Px(3);
    draw->AddCircleFilled(ImVec2(x + knob * 0.5f, origin.y + height * 0.5f), knob * 0.5f, IM_COL32(255, 255, 255, 255));
    ImGui::PopID();
    return clicked;
}

void Keycap(const Shell& shell, const std::string& text) {
    const ImVec2 size = ImGui::CalcTextSize(text.c_str()) + ImVec2(Px(12), Px(4));
    const ImVec2 origin = ImGui::GetCursorScreenPos();
    ImDrawList* draw = ImGui::GetWindowDrawList();
    draw->AddRectFilled(origin, origin + size, PaintU(shell, "bg.input"), Px(5));
    draw->AddRect(origin, origin + size, PaintU(shell, "border.default"), Px(5));
    draw->AddText(origin + ImVec2(Px(6), Px(2)), PaintU(shell, "text.secondary"), text.c_str());
    ImGui::Dummy(size);
}

void BeginCard(const Shell& shell, const char* id, const char* title, float glass = 1.f, float inset = 16.f, float pad = 16.f) {
    ImVec4 card = Paint(shell, "bg.float");
    card.w = glass;
    ImGui::PushStyleColor(ImGuiCol_ChildBg, card);
    ImGui::PushStyleVar(ImGuiStyleVar_ChildRounding, Px(10));
    ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(Px(pad), Px(pad)));
    ImGui::SetCursorPosX(Px(inset));
    ImGui::BeginChild(id, ImVec2(std::max(0.f, ImGui::GetContentRegionAvail().x - Px(inset)), 0), ImGuiChildFlags_Borders | ImGuiChildFlags_AutoResizeY, ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoScrollWithMouse);
    if (title && title[0]) {
        ImGui::Text("%s", title);
        ImGui::Dummy(ImVec2(0, Px(4)));
    }
}

void EndRow();

void EndCard() {
    EndRow();
    ImGui::EndChild();
    ImGui::PopStyleVar(2);
    ImGui::PopStyleColor();
    ImGui::Dummy(ImVec2(0, Px(4)));
}

bool g_rowOpen = false;

void EndRow() {
    if (!g_rowOpen) return;
    ImGui::EndGroup();
    g_rowOpen = false;
}

void BeginRow(const char* title, const char* detail) {
    // 卡片高度跟着内容长。表格嵌在这种卡片里会在裁切时提前结束，再调用就会弹出 ImGui 报错。
    // 左右两列改成两组控件：左边说明，右边开关和按钮。
    EndRow();
    ImGui::Spacing();
    const float top = ImGui::GetCursorPosY();
    const float full = ImGui::GetContentRegionAvail().x;
    const float controlW = std::min(Px(280), std::max(Px(120), full * 0.46f));
    ImGui::BeginGroup();
    ImGui::PushTextWrapPos(ImGui::GetCursorPos().x + full - controlW - Px(16));
    ImGui::TextUnformatted(title);
    if (detail && detail[0]) ImGui::TextDisabled("%s", detail);
    ImGui::PopTextWrapPos();
    ImGui::EndGroup();
    ImGui::SameLine();
    ImGui::SetCursorPos(ImVec2(ImGui::GetCursorPosX() + ImGui::GetContentRegionAvail().x - controlW, top));
    ImGui::BeginGroup();
    g_rowOpen = true;
}

bool Hidden(const Settings& settings, const std::string& id) {
    return std::find(settings.hiddenTools.begin(), settings.hiddenTools.end(), id) != settings.hiddenTools.end();
}

std::string HotkeyText(const Shell& shell, const std::string& id) {
    const auto found = shell.session->settings.hotkeys.find(id);
    if (found != shell.session->settings.hotkeys.end() && !found->second.text.empty()) return found->second.text;
    return shell.session->store.FormatChord("windows", id);
}

const char* VkName(int vk) {
    static char buffer[16];
    if (vk == VK_SPACE) return "Space";
    if (vk >= 'A' && vk <= 'Z') {
        buffer[0] = static_cast<char>(vk);
        buffer[1] = 0;
        return buffer;
    }
    if (vk >= '0' && vk <= '9') {
        buffer[0] = static_cast<char>(vk);
        buffer[1] = 0;
        return buffer;
    }
    if (vk == VK_OEM_COMMA) return ",";
    std::snprintf(buffer, sizeof(buffer), "VK%d", vk);
    return buffer;
}

int ImGuiKeyToVk(ImGuiKey key) {
    if (key >= ImGuiKey_A && key <= ImGuiKey_Z) return 'A' + (key - ImGuiKey_A);
    if (key >= ImGuiKey_0 && key <= ImGuiKey_9) return '0' + (key - ImGuiKey_0);
    if (key == ImGuiKey_Space) return VK_SPACE;
    if (key == ImGuiKey_Enter || key == ImGuiKey_KeypadEnter) return VK_RETURN;
    if (key == ImGuiKey_Tab) return VK_TAB;
    if (key == ImGuiKey_Comma) return VK_OEM_COMMA;
    if (key >= ImGuiKey_F1 && key <= ImGuiKey_F12) return VK_F1 + (key - ImGuiKey_F1);
    return 0;
}

void DrawIcon(const Shell& shell, const Hit& hit, ImVec2 center, float size) {
    ImDrawList* draw = ImGui::GetWindowDrawList();
    if (!hit.path.empty() && hit.kind != "tool") {
        if (const ImTextureID texture = FileIcon(hit.path, 32)) {
            draw->AddImage(texture, ImVec2(center.x - size * 0.5f, center.y - size * 0.5f), ImVec2(center.x + size * 0.5f, center.y + size * 0.5f));
            return;
        }
    }
    const char* glyph = hit.kind == "tool" ? ToolGlyph(hit.toolId) : hit.kind == "url" ? ToolGlyph("link") : hit.kind == "file" ? ToolGlyph("doc") : ToolGlyph("app");
    if (g_iconFont) DrawGlyph(glyph, PaintU(shell, "accent"), center, 18);
    else {
        const std::string mark = FirstGlyph(hit.title);
        const ImVec2 text = ImGui::CalcTextSize(mark.c_str());
        draw->AddText(ImVec2(center.x - text.x * 0.5f, center.y - text.y * 0.5f), PaintU(shell, "accent"), mark.c_str());
    }
}

bool ResultRow(Shell& shell, const Hit& hit, bool selected, float width, float height, int index, bool showEnter) {
    ImGui::PushID(hit.id.c_str());
    const ImVec2 origin = ImGui::GetCursorScreenPos();
    const float rowW = width - Px(16);
    const bool clicked = ImGui::Selectable("##row", selected, 0, ImVec2(rowW, height));
    ImDrawList* draw = ImGui::GetWindowDrawList();
    if (selected) {
        draw->AddRectFilled(origin, ImVec2(origin.x + rowW, origin.y + height), PaintU(shell, "bg.selected"), Px(8));
        draw->AddRectFilled(ImVec2(origin.x, origin.y + Px(10)), ImVec2(origin.x + Px(3), origin.y + height - Px(10)), PaintU(shell, "accent"), Px(2));
    }
    // 和原来的结果行一样：左边 8，图标列 40，标题 14，副标题 12。
    DrawIcon(shell, hit, ImVec2(origin.x + Px(28), origin.y + height * 0.5f), Px(32));
    const std::string secondary = hit.subtitle.empty() ? KindLabel(hit.kind) : hit.subtitle;
    const float textX = origin.x + Px(48);
    const float titleSize = ImGui::GetFontSize();
    float titleY = origin.y + (height - titleSize) * 0.5f;
    if (g_smallFont && height >= Px(44)) {
        const float subSize = g_smallFont->LegacySize;
        const float block = titleSize + subSize + Px(1);
        titleY = origin.y + (height - block) * 0.5f;
        draw->AddText(g_smallFont, subSize, ImVec2(textX, titleY + titleSize + Px(1)), PaintU(shell, "text.tertiary"), secondary.c_str());
    }
    draw->AddText(ImVec2(textX, titleY), PaintU(shell, "text.primary"), hit.title.c_str());
    std::string badge = index >= 1 && index <= 9 ? shell.session->store.KeyLabel("windows", "alt") + "+" + std::to_string(index) : std::to_string(index);
    if (g_smallFont) ImGui::PushFont(g_smallFont);
    const ImVec2 badgeSize = ImGui::CalcTextSize(badge.c_str()) + ImVec2(Px(10), Px(2));
    const ImVec2 badgePos(origin.x + rowW - badgeSize.x - Px(8), origin.y + (height - badgeSize.y) * 0.5f);
    draw->AddRectFilled(badgePos, badgePos + badgeSize, PaintU(shell, "bg.input"), Px(5));
    draw->AddRect(badgePos, badgePos + badgeSize, PaintU(shell, "border.default"), Px(5));
    draw->AddText(badgePos + ImVec2(Px(5), Px(1)), PaintU(shell, "text.secondary"), badge.c_str());
    if (showEnter) {
        const std::string enter = shell.session->store.KeyLabel("windows", "enter");
        const ImVec2 enterSize = ImGui::CalcTextSize(enter.c_str());
        draw->AddText(ImVec2(badgePos.x - enterSize.x - Px(8), origin.y + (height - enterSize.y) * 0.5f), PaintU(shell, "text.tertiary"), enter.c_str());
    }
    if (g_smallFont) ImGui::PopFont();
    if (ImGui::BeginPopupContextItem("row-menu")) {
        if (!hit.path.empty() && ImGui::MenuItem("打开文件夹")) RevealTarget(hit.path);
        if (ImGui::MenuItem("复制路径")) CopyText(hit.path.empty() ? hit.title : hit.path);
        ImGui::EndPopup();
    }
    ImGui::PopID();
    return clicked;
}

void Section(const Shell& shell, const char* title, const std::string& extra) {
    if (g_smallFont) ImGui::PushFont(g_smallFont);
    ImGui::Dummy(ImVec2(0, Px(8)));
    ImGui::SetCursorPosX(Px(16));
    ImGui::TextDisabled("%s", title);
    if (!extra.empty()) {
        const float extraWidth = ImGui::CalcTextSize(extra.c_str()).x;
        ImGui::SameLine(ImGui::GetWindowWidth() - extraWidth - Px(16));
        ImGui::TextDisabled("%s", extra.c_str());
    }
    if (g_smallFont) ImGui::PopFont();
    // 标题和下面第一条结果之间留出空隙。上面的 Dummy 只撑开标题上方。
    ImGui::Dummy(ImVec2(0, Px(8)));
    (void)shell;
}

void BuildHome(Shell& shell) {
    Session& session = *shell.session;
    shell.home = session.RecentHits();
    std::vector<std::string> seen;
    for (const Hit& hit : shell.home) seen.push_back(hit.id);
    int candidates = 0;
    for (const Hit& hit : session.index) {
        if (hit.kind != "app" || std::find(seen.begin(), seen.end(), hit.id) != seen.end()) continue;
        shell.home.push_back(hit);
        if (++candidates >= 12) break;
    }
}

int ActiveCount(const Shell& shell) {
    if (!shell.session->triggerWord.empty() || !shell.session->query.empty()) return static_cast<int>(shell.session->hits.size());
    return static_cast<int>(shell.home.size());
}

void PublishTool(Shell& shell) {
    const std::string& text = shell.toolInput;
    if (shell.activeTool == "unit" || text.find(" to ") != std::string::npos) shell.toolOutput = ConvertUnit(text).value_or("—");
    else if (shell.activeTool == "calc") shell.toolOutput = EvaluateExpression(text).ok ? EvaluateExpression(text).data : "—";
    else if (shell.activeTool == "translate") shell.toolOutput = TranslateText(text).first;
    else if (shell.activeTool == "color") shell.toolOutput = DescribeColor(text).value_or("格式非法");
    else if (shell.activeTool == "timestamp") shell.toolOutput = DescribeTimestamp(text).value_or("格式非法");
}

LRESULT CALLBACK PickMouse(int code, WPARAM wParam, LPARAM lParam) {
    if (code == HC_ACTION && g_pick && g_pick->pickingColor && (wParam == WM_LBUTTONDOWN || wParam == WM_RBUTTONDOWN)) {
        if (wParam == WM_LBUTTONDOWN) {
            const auto* info = reinterpret_cast<MSLLHOOKSTRUCT*>(lParam);
            const std::string hex = ScreenPixelHex(info->pt.x, info->pt.y);
            if (!hex.empty()) {
                g_pick->pickedHex = hex;
                g_pick->toolOutput = hex;
                g_pick->toolInput = hex;
                CopyText(hex);
                LogInfo("已取色 " + hex);
            }
        }
        g_pick->pickingColor = false;
        return 1;
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

LRESULT CALLBACK PickKey(int code, WPARAM wParam, LPARAM lParam) {
    if (code == HC_ACTION && g_pick && g_pick->pickingColor && wParam == WM_KEYDOWN) {
        const auto* info = reinterpret_cast<KBDLLHOOKSTRUCT*>(lParam);
        if (info->vkCode == VK_ESCAPE) {
            g_pick->pickingColor = false;
            return 1;
        }
    }
    return CallNextHookEx(nullptr, code, wParam, lParam);
}

bool Folded(const Shell& shell, const std::string& id) {
    return std::find(shell.foldedGroups.begin(), shell.foldedGroups.end(), id) != shell.foldedGroups.end();
}

void ToggleFold(Shell& shell, const std::string& id) {
    const auto found = std::find(shell.foldedGroups.begin(), shell.foldedGroups.end(), id);
    if (found == shell.foldedGroups.end()) shell.foldedGroups.push_back(id);
    else shell.foldedGroups.erase(found);
}

std::string DropGroup(const Shell& shell) {
    if (!shell.hoverGroup.empty() && shell.hoverGroup != "all") return shell.hoverGroup;
    if (!shell.selectedGroup.empty() && shell.selectedGroup != "all") return shell.selectedGroup;
    return "ungrouped";
}

std::string LowerAscii(std::string text) {
    for (char& ch : text) {
        if (ch >= 'A' && ch <= 'Z') ch = static_cast<char>(ch - 'A' + 'a');
    }
    return text;
}

bool ContainsFold(const std::string& haystack, const std::string& needle) {
    if (needle.empty()) return true;
    return LowerAscii(haystack).find(LowerAscii(needle)) != std::string::npos;
}

bool ShortcutMatches(const Shortcut& item, const std::string& query) {
    return ContainsFold(item.title, query) || ContainsFold(item.path, query);
}

ImVec2 RotatePoint(ImVec2 point, ImVec2 center, float radians) {
    const float c = std::cos(radians);
    const float s = std::sin(radians);
    const float x = point.x - center.x;
    const float y = point.y - center.y;
    return ImVec2(center.x + x * c - y * s, center.y + x * s + y * c);
}

// 改版前是左右各晃 0.86 度，来回 0.14 秒，一共 6 下。相邻图标错开一点点。
float EditWobble(const Shell& shell, int index) {
    if (!shell.groupsEditing || shell.editWobbleStart < 0.0) return 0.f;
    const float elapsed = static_cast<float>(ImGui::GetTime() - shell.editWobbleStart) - static_cast<float>(index % 2) * 0.05f;
    if (elapsed < 0.f || elapsed >= 1.68f) return 0.f;
    const float wave = std::sin(elapsed / 0.28f * 6.2831853f - 1.5707963f);
    return wave * 0.86f * 3.14159265f / 180.f;
}

std::string FitText(const std::string& text, float maxWidth) {
    if (text.empty() || ImGui::CalcTextSize(text.c_str()).x <= maxWidth) return text;
    const std::string ellipsis = "…";
    std::string cut = text;
    while (!cut.empty()) {
        while (!cut.empty() && (static_cast<unsigned char>(cut.back()) & 0xC0) == 0x80) cut.pop_back();
        if (!cut.empty()) cut.pop_back();
        if (ImGui::CalcTextSize((cut + ellipsis).c_str()).x <= maxWidth) return cut + ellipsis;
    }
    return ellipsis;
}

struct TopmostPause {
    HWND hwnd = nullptr;
    explicit TopmostPause(HWND window) : hwnd(window) {
        if (hwnd) SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
    ~TopmostPause() {
        if (!hwnd) return;
        SetForegroundWindow(hwnd);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }
    TopmostPause(const TopmostPause&) = delete;
    TopmostPause& operator=(const TopmostPause&) = delete;
};

void FinishOwnedDialog(Shell& shell) {
    shell.blurSince = 0;
    shell.blurGraceUntil = GetTickCount64() + 400;
    shell.suppressBlur = false;
}

void AddFiles(Shell& shell) {
    shell.suppressBlur = true;
    const HWND owner = static_cast<HWND>(shell.hostWindow);
    TopmostPause pause(owner);
    wchar_t buffer[32768] = {};
    OPENFILENAMEW dialog{};
    dialog.lStructSize = sizeof(dialog);
    dialog.hwndOwner = owner;
    dialog.lpstrFile = buffer;
    dialog.nMaxFile = 32768;
    dialog.lpstrTitle = L"选择应用、文件或文件夹";
    dialog.lpstrFilter = L"全部\0*.*\0";
    dialog.Flags = OFN_EXPLORER | OFN_ALLOWMULTISELECT | OFN_FILEMUSTEXIST | OFN_PATHMUSTEXIST;
    if (GetOpenFileNameW(&dialog)) {
        const wchar_t* cursor = buffer;
        const std::wstring first = cursor;
        cursor += first.size() + 1;
        if (*cursor == 0) shell.session->AddShortcut(Utf8(first), DropGroup(shell));
        else {
            while (*cursor) {
                const std::wstring name = cursor;
                shell.session->AddShortcut(Utf8(first + L"\\" + name), DropGroup(shell));
                cursor += name.size() + 1;
            }
        }
    }
    FinishOwnedDialog(shell);
}

void AddGroup(Shell& shell) {
    const std::vector<std::string> colors = shell.session->store.GroupColors();
    Group group;
    // 用毫秒做编号。只精确到秒的话，连点两次会得到同一个编号，界面就会报编号冲突。
    group.id = "g" + std::to_string(GetTickCount64());
    group.name = "新分组";
    group.color = colors.size() > 3 ? colors[3] : "#9B6CF6";
    for (const Group& existing : shell.session->library.groups) {
        if (existing.id == group.id) group.id += "b";
    }
    shell.session->library.groups.push_back(group);
    shell.session->Save();
    shell.selectedGroup = group.id;
    shell.renamingGroup = group.id;
    shell.renameBuffer = group.name;
    shell.renameFocus = true;
    shell.confirmDeleteId.clear();
    LogInfo("新建分组 " + group.id);
}

void DrawDeleteConfirm(Shell& shell) {
    if (shell.confirmDeleteId.empty()) return;
    Session& session = *shell.session;
    const auto group = std::find_if(session.library.groups.begin(), session.library.groups.end(), [&](const Group& item) { return item.id == shell.confirmDeleteId; });
    if (group == session.library.groups.end()) {
        shell.confirmDeleteId.clear();
        return;
    }
    const std::string id = group->id;
    const std::string name = group->name;
    const int count = static_cast<int>(group->itemIds.size());
    shell.suppressBlur = true;
    const std::string body = session.store.Text("group.deleteConfirm", {{"name", name}, {"count", std::to_string(count)}});
    const ImVec2 window = ImGui::GetWindowSize();
    ImGui::SetCursorPos(ImVec2(0, 0));
    ImGui::PushStyleColor(ImGuiCol_ChildBg, ImVec4(0, 0, 0, 0.45f));
    ImGui::BeginChild("delete-veil", window, false);
    const float cardW = std::min(Px(420), window.x - Px(48));
    const float cardH = Px(168);
    ImGui::SetCursorPos(ImVec2((window.x - cardW) * 0.5f, (window.y - cardH) * 0.5f));
    ImGui::PushStyleColor(ImGuiCol_ChildBg, Paint(shell, "bg.float"));
    ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding, ImVec2(Px(20), Px(16)));
    ImGui::BeginChild("delete-card", ImVec2(cardW, cardH), ImGuiChildFlags_Borders);
    ImGui::TextWrapped("%s", body.c_str());
    ImGui::Dummy(ImVec2(0, Px(12)));
    if (SoftButton(shell, "confirm-delete", "删除分组", true)) {
        session.DeleteGroupById(id);
        if (shell.selectedGroup == id) shell.selectedGroup = "all";
        shell.renamingGroup.clear();
        shell.confirmDeleteId.clear();
    }
    ImGui::SameLine();
    if (SoftButton(shell, "cancel-delete", "取消")) shell.confirmDeleteId.clear();
    ImGui::EndChild();
    ImGui::PopStyleVar();
    ImGui::PopStyleColor();
    ImGui::EndChild();
    ImGui::PopStyleColor();
}

void ChooseBackground(Shell& shell) {
    shell.suppressBlur = true;
    const HWND owner = static_cast<HWND>(shell.hostWindow);
    TopmostPause pause(owner);
    wchar_t file[MAX_PATH] = {};
    OPENFILENAMEW dialog{};
    dialog.lStructSize = sizeof(dialog);
    dialog.hwndOwner = owner;
    dialog.lpstrFile = file;
    dialog.nMaxFile = MAX_PATH;
    dialog.lpstrFilter = L"图片\0*.png;*.jpg;*.jpeg;*.bmp\0";
    dialog.lpstrTitle = L"设为背景";
    dialog.Flags = OFN_FILEMUSTEXIST | OFN_EXPLORER;
    if (GetOpenFileNameW(&dialog)) {
        std::wstring source = file;
        std::wstring extension = L".png";
        const auto dot = source.find_last_of(L'.');
        if (dot != std::wstring::npos) extension = source.substr(dot);
        const std::wstring destination = Wide(DataRoot()) + L"\\background-" + std::to_wstring(GetTickCount64()) + extension;
        if (CopyFileW(source.c_str(), destination.c_str(), FALSE)) {
            shell.session->settings.backgroundImagePath = Utf8(destination);
            shell.session->Save();
            g_backgroundPath.clear();
        }
    }
    FinishOwnedDialog(shell);
}

void ExportData(Shell& shell) {
    shell.suppressBlur = true;
    const HWND owner = static_cast<HWND>(shell.hostWindow);
    TopmostPause pause(owner);
    wchar_t folder[MAX_PATH] = {};
    BROWSEINFOW browse{};
    browse.hwndOwner = owner;
    browse.pszDisplayName = folder;
    browse.lpszTitle = L"导出到这里";
    browse.ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE;
    LPITEMIDLIST id = SHBrowseForFolderW(&browse);
    if (id) {
        wchar_t path[MAX_PATH] = {};
        if (SHGetPathFromIDListW(id, path)) {
            const std::filesystem::path destination = std::filesystem::path(path) / L"DeskKit-export";
            std::error_code error;
            std::filesystem::create_directories(destination, error);
            for (const wchar_t* name : {L"settings.json", L"groups.json", L"clipboard.json", L"recents.json"}) {
                const std::filesystem::path source = std::filesystem::path(Wide(DataRoot())) / name;
                if (std::filesystem::exists(source)) std::filesystem::copy_file(source, destination / name, std::filesystem::copy_options::overwrite_existing, error);
            }
            OpenTarget(Utf8(destination.wstring()));
            LogInfo("已导出数据");
        }
        CoTaskMemFree(id);
    }
    FinishOwnedDialog(shell);
}

}  // namespace

void BindIconDevice(void* device) { g_device = static_cast<ID3D11Device*>(device); }

void SyncPicker(Shell& shell) {
    g_pick = &shell;
    if (shell.pickingColor) {
        if (!g_pickMouse) g_pickMouse = SetWindowsHookExW(WH_MOUSE_LL, PickMouse, GetModuleHandleW(nullptr), 0);
        if (!g_pickKey) g_pickKey = SetWindowsHookExW(WH_KEYBOARD_LL, PickKey, GetModuleHandleW(nullptr), 0);
    } else {
        if (g_pickMouse) UnhookWindowsHookEx(g_pickMouse);
        if (g_pickKey) UnhookWindowsHookEx(g_pickKey);
        g_pickMouse = nullptr;
        g_pickKey = nullptr;
    }
}

float ShadowPad(const Shell&) {
    // 面板四周不留阴影。留白会在桌面上显出一圈边。
    return 0.f;
}

void AddSoftShadow(ImDrawList* draw, ImVec2 min, ImVec2 max, float radius, float offsetY, float reach, ImVec4 ink, float opacity) {
    // 从外往里画。每一层只补上「比外面更深的那一点」，叠完才等于目标透明度，不会糊成一块灰框。
    const int steps = 7;
    reach = std::min(reach, Px(12));
    float previous = 0.f;
    for (int i = steps; i >= 1; --i) {
        const float t = static_cast<float>(i) / static_cast<float>(steps);
        const float grow = reach * t;
        const float target = opacity * std::exp(-4.2f * t * t);
        const float alpha = (target - previous) / std::max(0.05f, 1.f - previous);
        previous = target;
        ink.w = std::clamp(alpha, 0.f, 0.35f);
        draw->AddRectFilled(ImVec2(min.x - grow, min.y + offsetY * t), ImVec2(max.x + grow, max.y + offsetY + grow * 0.35f), ImGui::ColorConvertFloat4ToU32(ink), radius + grow);
    }
}

void DrawPanelShadow(const Shell& shell, float) {
    ImDrawList* draw = ImGui::GetWindowDrawList();
    const float pad = ShadowPad(shell);
    const ImVec2 origin = ImGui::GetWindowPos() + ImVec2(pad, pad);
    const ImVec2 end = ImGui::GetWindowPos() + ImGui::GetWindowSize() - ImVec2(pad, pad);
    const float radius = Px(shell.session->store.Layout({"radius", "float"}));
    const ImVec4 ink = Paint(shell, "shadow.ink");
    const float alpha1 = static_cast<float>(shell.session->store.Layout({"shadow", "alpha1"}));
    const float alpha2 = static_cast<float>(shell.session->store.Layout({"shadow", "alpha2"}));
    AddSoftShadow(draw, origin, end, radius, Px(6), Px(12), ink, alpha1);
    AddSoftShadow(draw, origin, end, radius, Px(2), Px(5), ink, alpha2);
}

void PaintChrome(const Shell& shell) {
    ImDrawList* draw = ImGui::GetWindowDrawList();
    const ImVec2 origin = ImGui::GetWindowPos();
    const ImVec2 end = origin + ImGui::GetWindowSize();
    const float radius = Px(shell.session->store.Layout({"radius", "float"}));
    const float alpha = static_cast<float>(std::clamp(shell.session->settings.backgroundOpacity, 0.15, 1.0));
    const char* fill = shell.showSettings || shell.showGuide || shell.showGroups ? "bg.canvas" : "bg.float";
    if (const ImTextureID image = BackgroundImage(shell)) {
        draw->AddImageRounded(image, origin, end, ImVec2(0, 0), ImVec2(1, 1), IM_COL32(255, 255, 255, static_cast<int>(alpha * 255)), radius);
        draw->AddRectFilled(origin, end, PaintU(shell, fill, 0.55f), radius);
    } else {
        draw->AddRectFilled(origin, end, PaintU(shell, fill, alpha), radius);
    }
}

void FinishChrome(const Shell& shell) {
    ImDrawList* draw = ImGui::GetWindowDrawList();
    const ImVec2 origin = ImGui::GetWindowPos();
    const ImVec2 end = origin + ImGui::GetWindowSize();
    draw->AddRect(origin, end, PaintU(shell, "border.default"), Px(shell.session->store.Layout({"radius", "float"})), 0, Px(1));
}

bool UserPressedEscape(Shell& shell) {
    const bool down = ImGui::IsKeyDown(ImGuiKey_Escape);
    // 窗口刚出现的一小段里，热键附带的 Esc 还在。这段不关窗口，并把卡住的按下清掉。
    if (GetTickCount64() < shell.ignoreEscapeUntil || !shell.escapeSeenUp) {
        if (down) ImGui::GetIO().AddKeyEvent(ImGuiKey_Escape, false);
        if (GetTickCount64() >= shell.ignoreEscapeUntil) shell.escapeSeenUp = true;
        return false;
    }
    return ImGui::IsKeyPressed(ImGuiKey_Escape, false);
}

void DrawLauncher(Shell& shell) {
    Session& session = *shell.session;
    const float width = ImGui::GetWindowWidth();
    const float inputH = Px(session.store.Layout({"launcher", "inputRowHeight"}));
    const float row = session.settings.density == "compact" ? Px(session.store.Layout({"launcher", "resultRowHeightCompact"})) : Px(session.store.Layout({"launcher", "resultRowHeight"}));
    if (session.query.empty() && session.triggerWord.empty()) BuildHome(shell);
    const int count = ActiveCount(shell);
    if (session.selection >= count) session.selection = std::max(0, count - 1);

    ImGui::BeginChild("input", ImVec2(0, inputH), false, ImGuiWindowFlags_NoScrollbar);
    {
        ImDrawList* draw = ImGui::GetWindowDrawList();
        const ImVec2 origin = ImGui::GetWindowPos();
        const ImVec2 mark(origin.x + Px(22), origin.y + inputH * 0.5f);
        const ImU32 icon = PaintU(shell, "text.tertiary");
        draw->AddCircle(mark, Px(7), icon, 16, 1.6f);
        draw->AddLine(ImVec2(mark.x + Px(5), mark.y + Px(5)), ImVec2(mark.x + Px(10), mark.y + Px(10)), icon, 1.6f);
        draw->AddLine(ImVec2(origin.x, origin.y + inputH - 1), ImVec2(origin.x + width, origin.y + inputH - 1), PaintU(shell, "border.default"));
    }
    float left = Px(44);
    if (!session.triggerWord.empty()) {
        const std::string word = session.triggerWord;
        const ImVec2 wordSize = ImGui::CalcTextSize(word.c_str());
        ImGui::SetCursorPos(ImVec2(left, (inputH - Px(28)) * 0.5f));
        const ImVec2 capsule = ImGui::GetCursorScreenPos();
        const ImVec2 capsuleSize(wordSize.x + Px(36), Px(28));
        ImDrawList* draw = ImGui::GetWindowDrawList();
        draw->AddRectFilled(capsule, capsule + capsuleSize, PaintU(shell, "accent.soft"), Px(14));
        draw->AddRect(capsule, capsule + capsuleSize, PaintU(shell, "accent"), Px(14));
        draw->AddText(capsule + ImVec2(Px(10), Px(6)), PaintU(shell, "accent.hover"), word.c_str());
        ImGui::SetCursorScreenPos(ImVec2(capsule.x + wordSize.x + Px(12), capsule.y));
        if (IconButton(shell, "drop-trigger", ToolGlyph("close"))) {
            session.triggerWord.clear();
            session.payload.clear();
            session.query.clear();
            session.Search();
        }
        left += capsuleSize.x + Px(8);
    }
    std::string shown = session.triggerWord.empty() ? session.query : session.payload;
    const std::string hint = session.triggerWord.empty() ? session.store.Text("launcher.placeholder") : (session.ResolveTriggerWord(session.triggerWord) ? session.ResolveTriggerWord(session.triggerWord)->placeholder : "");
    const float btn = Px(32);
    const float gap = Px(8);
    const float edge = Px(12);
    const bool showClear = !shown.empty();
    const int buttons = showClear ? 3 : 2;
    const float cluster = edge + buttons * btn + (buttons - 1) * gap;
    ImGui::SetCursorPos(ImVec2(left, (inputH - ImGui::GetFrameHeight()) * 0.5f));
    if (g_inputFont) ImGui::PushFont(g_inputFont);
    ImGui::SetNextItemWidth(std::max(Px(80), width - left - cluster - gap));
    ImGui::PushStyleColor(ImGuiCol_FrameBg, ImVec4(0, 0, 0, 0));
    ImGui::PushStyleVar(ImGuiStyleVar_FrameBorderSize, 0);
    // SetKeyboardFocusHere 只是“下一个控件”。输入框要真正进入编辑，光标才会闪，字才会打进去。
    if (shell.focusQuery > 0) {
        ImGui::GetIO().AddKeyEvent(ImGuiKey_Escape, false);
        ImGuiContext& context = *ImGui::GetCurrentContext();
        const ImGuiID queryId = ImGui::GetID("##query");
        context.NavActivateId = queryId;
        context.NavActivateDownId = queryId;
        context.NavActivatePressedId = queryId;
        context.NavActivateFlags = ImGuiActivateFlags_PreferInput | ImGuiActivateFlags_FromFocusApi | ImGuiActivateFlags_TryToPreserveState;
    }
    ImGui::InputTextWithHint("##query", hint.c_str(), &shown, ImGuiInputTextFlags_CallbackHistory, [](ImGuiInputTextCallbackData* data) {
        auto* current = static_cast<Shell*>(data->UserData);
        const int total = ActiveCount(*current);
        if (data->EventFlag == ImGuiInputTextFlags_CallbackHistory && total > 0) {
            if (data->EventKey == ImGuiKey_UpArrow) current->session->selection = std::max(0, current->session->selection - 1);
            if (data->EventKey == ImGuiKey_DownArrow) current->session->selection = std::min(total - 1, current->session->selection + 1);
        }
        return 0;
    }, &shell);
    ImGui::PopStyleVar();
    ImGui::PopStyleColor();
    if (g_inputFont) ImGui::PopFont();
    if (ImGui::IsItemActive()) shell.focusQuery = 0;
    else if (shell.focusQuery > 0) {
        --shell.focusQuery;
        // 窗口这一帧还没拿到系统焦点时，下一帧再抢一次，否则激活请求会被丢掉。
        shell.wantFocus = true;
    }
    float buttonX = width - edge - btn;
    const float buttonY = (inputH - btn) * 0.5f;
    ImGui::SetCursorPos(ImVec2(buttonX, buttonY));
    if (IconButton(shell, "close", ToolGlyph("close"))) session.Hide();
    buttonX -= btn + gap;
    ImGui::SetCursorPos(ImVec2(buttonX, buttonY));
    if (IconButton(shell, "settings", ToolGlyph("gear"))) shell.ShowSettings();
    if (showClear) {
        buttonX -= btn + gap;
        ImGui::SetCursorPos(ImVec2(buttonX, buttonY));
        if (IconButton(shell, "clear", ToolGlyph("close"))) {
            shown.clear();
            session.query.clear();
            session.triggerWord.clear();
            session.payload.clear();
            session.Search();
        }
    }
    ImGui::EndChild();

    if (shown != (session.triggerWord.empty() ? session.query : session.payload)) {
        session.query = session.triggerWord.empty() ? shown : session.triggerWord + " " + shown;
        session.Search();
    }
    if (ImGui::IsKeyPressed(ImGuiKey_Enter) || ImGui::IsKeyPressed(ImGuiKey_KeypadEnter)) {
        if (session.query.empty() && session.triggerWord.empty() && !shell.home.empty()) session.Execute(shell.home[std::clamp(session.selection, 0, static_cast<int>(shell.home.size()) - 1)]);
        else if (!session.triggerWord.empty() && !session.hits.empty()) session.Execute(session.hits[0]);
        else session.ExecuteSelection();
    }
    if (UserPressedEscape(shell)) {
        if (!session.triggerWord.empty()) {
            session.triggerWord.clear();
            session.payload.clear();
            session.query.clear();
            session.Search();
        } else if (!session.query.empty()) {
            session.query.clear();
            session.Search();
        } else session.Escape();
    }
    if (ImGui::IsKeyPressed(ImGuiKey_Tab)) shell.ShowTools();
    if (ImGui::IsKeyDown(ImGuiKey_LeftAlt) || ImGui::IsKeyDown(ImGuiKey_RightAlt)) {
        const std::vector<Hit>& source = session.query.empty() && session.triggerWord.empty() ? shell.home : session.hits;
        for (int digit = 1; digit <= 9; ++digit) {
            if (!ImGui::IsKeyPressed(static_cast<ImGuiKey>(ImGuiKey_0 + digit))) continue;
            if (digit <= static_cast<int>(source.size())) session.Execute(source[static_cast<size_t>(digit - 1)]);
        }
    }

    ImGui::BeginChild("body", ImVec2(0, std::max(0.f, ImGui::GetWindowHeight() - inputH)), false);
    ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(Px(8), 0));
    ImGui::Dummy(ImVec2(0, Px(4)));
    if (!session.triggerWord.empty()) {
        const std::optional<TriggerDef> resolved = session.ResolveTriggerWord(session.triggerWord);
        const Hit* hit = session.hits.empty() ? nullptr : &session.hits[0];
        ImGui::SetCursorPos(ImVec2(Px(16), Px(12)));
        ImGui::Text("触发词 %s · 工具独占视图", session.triggerWord.c_str());
        ImGui::SameLine();
        if (resolved) ImGui::TextColored(Paint(shell, "accent.hover"), "%s", resolved->label.c_str());
        ImGui::SetCursorPosX(Px(16));
        BeginCard(shell, "trigger", "");
        if (resolved && resolved->toolId == "color") {
            ImGui::TextDisabled("HEX ↔ RGB ↔ HSL · 输入 %s", session.payload.empty() ? "#RRGGBB" : session.payload.c_str());
            if (const auto rgb = ParseColor(session.payload.empty() ? session.store.Color("accent") : session.payload)) {
                const ImVec2 swatch = ImGui::GetCursorScreenPos();
                ImGui::Dummy(ImVec2(ImGui::GetContentRegionAvail().x, Px(56)));
                ImGui::GetWindowDrawList()->AddRectFilled(swatch, ImVec2(swatch.x + ImGui::GetContentRegionAvail().x, swatch.y + Px(56)), IM_COL32(rgb->r, rgb->g, rgb->b, 255), Px(8));
            }
        } else if (resolved && resolved->toolId == "clipboard") {
            ImGui::TextDisabled("剪贴板历史 · 点击复用");
            int shownClips = 0;
            for (const Clip& clip : session.clips) {
                if (!session.payload.empty() && clip.text.find(session.payload) == std::string::npos) continue;
                const std::string label = clip.text.size() > 48 ? clip.text.substr(0, 48) : clip.text;
                if (SoftButton(shell, label.c_str(), label.c_str())) {
                    CopyText(clip.text);
                    session.Hide();
                }
                if (++shownClips >= 5) break;
            }
            if (shownClips == 0) ImGui::TextDisabled("%s", session.store.Text("empty.clipboard").c_str());
        } else {
            const char* lead = "工具";
            if (resolved && resolved->toolId == "translate") lead = "原文";
            if (resolved && resolved->toolId == "calc") lead = "表达式 / 单位换算";
            if (resolved && resolved->toolId == "timestamp") lead = "时间戳 ↔ 日期双向";
            ImGui::TextDisabled("%s", lead);
            ImGui::PushFont(g_inputFont ? g_inputFont : ImGui::GetFont());
            ImGui::TextWrapped("%s", session.payload.empty() ? "（继续输入）" : session.payload.c_str());
            ImGui::PopFont();
            ImGui::TextDisabled("结果");
            ImGui::PushFont(g_inputFont ? g_inputFont : ImGui::GetFont());
            ImGui::TextColored(Paint(shell, "accent.hover"), "%s", hit ? hit->title.c_str() : "—");
            ImGui::PopFont();
            if (hit && !hit->subtitle.empty()) ImGui::TextDisabled("%s", hit->subtitle.c_str());
        }
        EndCard();
        ImGui::SetCursorPosX(Px(16));
        if (resolved && SoftButton(shell, "open-tool", ("打开「" + ToolTitle(resolved->toolId) + "」工具内页").c_str())) shell.ShowTool(resolved->toolId);
        ImGui::SameLine();
        if (SoftButton(shell, "copy-trigger", "Enter 复制结果") && hit && hit->copyText) CopyText(*hit->copyText);
        ImGui::SameLine();
        if (SoftButton(shell, "exit-trigger", "Esc 退出触发词胶囊")) {
            session.triggerWord.clear();
            session.payload.clear();
            session.query.clear();
            session.Search();
        }
    } else if (session.query.empty()) {
        const int recent = static_cast<int>(session.RecentHits().size());
        Section(shell, session.store.Text("launcher.recent").c_str(), recent > 0 ? std::to_string(recent) + " 条" : "打开应用后会出现在这里");
        int index = 0;
        for (const Hit& hit : shell.home) {
            if (index == recent && recent < static_cast<int>(shell.home.size())) Section(shell, "候选", std::to_string(shell.home.size() - recent) + " 条");
            ImGui::SetCursorPosX(Px(8));
            if (ResultRow(shell, hit, index == session.selection, width, row, index + 1, false)) {
                session.selection = index;
                session.Execute(hit);
            }
            ++index;
        }
    } else if (session.hits.empty()) {
        ImGui::Dummy(ImVec2(0, Px(24)));
        const std::string title = session.store.Text("empty.search", {{"query", session.query}});
        const float titleWidth = ImGui::CalcTextSize(title.c_str()).x;
        ImGui::SetCursorPosX(std::max(0.f, (width - titleWidth) * 0.5f));
        ImGui::TextDisabled("%s", title.c_str());
        ImGui::Dummy(ImVec2(0, Px(12)));
        const float buttons = Px(360);
        ImGui::SetCursorPosX(std::max(0.f, (width - buttons) * 0.5f));
        if (SoftButton(shell, "browse", session.store.Text("empty.searchBrowser").c_str(), true)) {
            std::string url = "https://www.bing.com/search?q=";
            for (unsigned char ch : session.query) {
                if (std::isalnum(ch) || ch == '-' || ch == '_' || ch == '.') url.push_back(static_cast<char>(ch));
                else {
                    char hex[8];
                    std::snprintf(hex, sizeof(hex), "%%%02X", ch);
                    url += hex;
                }
            }
            OpenTarget(url);
            session.Hide();
        }
        ImGui::SameLine();
        if (SoftButton(shell, "clear-empty", session.store.Text("empty.searchClear").c_str())) {
            session.query.clear();
            session.Search();
        }
        ImGui::SameLine();
        if (SoftButton(shell, "all-tools", session.store.Text("empty.searchTools").c_str())) shell.ShowTools();
    } else {
        const char* kinds[] = {"tool", "app", "file", "url"};
        const char* labels[] = {"内置工具", "应用", "文件", "网址"};
        int shown = 0;
        for (int kind = 0; kind < 4; ++kind) {
            int matches = 0;
            for (const Hit& hit : session.hits)
                if (hit.kind == kinds[kind]) ++matches;
            if (!matches) continue;
            Section(shell, labels[kind], std::to_string(matches) + " 条");
            for (int i = 0; i < static_cast<int>(session.hits.size()); ++i) {
                if (session.hits[i].kind != kinds[kind]) continue;
                ImGui::SetCursorPosX(Px(8));
                if (ResultRow(shell, session.hits[i], shown == session.selection, width, row, shown + 1, shown == session.selection)) {
                    session.selection = shown;
                    session.Execute(session.hits[i]);
                }
                ++shown;
            }
        }
    }
    ImGui::PopStyleVar();
    ImGui::EndChild();
}

void DragGroups(Shell& shell, bool onSurface) {
    static bool dragging = false;
    static int grabX = 0;
    static int grabY = 0;
    if (ImGui::IsPopupOpen(nullptr, ImGuiPopupFlags_AnyPopup)) {
        if (dragging && !ImGui::IsMouseDown(ImGuiMouseButton_Left)) dragging = false;
        return;
    }
    HWND hwnd = FindWindowW(L"DeskKitHost", nullptr);
    if (!hwnd) return;
    if (!dragging && onSurface && !ImGui::IsAnyItemHovered() && !ImGui::IsAnyItemActive() && ImGui::IsMouseClicked(ImGuiMouseButton_Left)) {
        POINT point;
        RECT rect;
        GetCursorPos(&point);
        GetWindowRect(hwnd, &rect);
        const int edge = static_cast<int>(Px(7));
        const bool onEdge = point.x <= rect.left + edge || point.x >= rect.right - edge || point.y <= rect.top + edge || point.y >= rect.bottom - edge;
        if (onEdge) return;
        grabX = point.x - rect.left;
        grabY = point.y - rect.top;
        dragging = true;
    }
    if (!dragging) return;
    if (ImGui::IsMouseDown(ImGuiMouseButton_Left)) {
        POINT point;
        GetCursorPos(&point);
        const float scale = Px(1.f);
        shell.session->settings.panelPlacedByUser = true;
        shell.session->settings.panelX = (point.x - grabX) / (scale > 0.f ? scale : 1.f);
        shell.session->settings.panelY = (point.y - grabY) / (scale > 0.f ? scale : 1.f);
        shell.suppressBlur = true;
        return;
    }
    dragging = false;
    shell.suppressBlur = false;
    shell.session->Save();
    LogInfo("记住快捷启动位置");
}

void DrawGroups(Shell& shell) {
    Session& session = *shell.session;
    // 菜单里的删除和移动先记下来，等这一帧的列表画完再改。边画边改会把正在遍历的数组弄坏，右键之后程序就起不来。
    std::string pendingDeleteItem;
    std::string pendingDeleteGroup;
    std::string pendingMoveItem;
    std::string pendingMoveFrom;
    std::string pendingMoveTo;
    bool sawMenu = false;
    const float headerH = Px(session.store.Layout({"windows", "headerHeight"}));
    const float navW = Px(session.store.Layout({"windows", "navWidth"}));
    ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(Px(8), 0));
    ImGui::BeginChild("header", ImVec2(0, headerH), false, ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoScrollWithMouse);
    ImDrawList* header = ImGui::GetWindowDrawList();
    const ImVec2 headerOrigin = ImGui::GetWindowPos();
    header->AddRectFilled(headerOrigin, headerOrigin + ImVec2(ImGui::GetWindowWidth(), headerH), PaintU(shell, "bg.float", 0.85f), Px(shell.session->store.Layout({"radius", "float"})), ImDrawFlags_RoundCornersTop);
    header->AddLine(ImVec2(headerOrigin.x, headerOrigin.y + headerH - 1), ImVec2(headerOrigin.x + ImGui::GetWindowWidth(), headerOrigin.y + headerH - 1), PaintU(shell, "border.default"));
    std::string heading = "全部";
    std::string headingColor;
    if (shell.selectedGroup != "all") {
        for (const Group& group : session.library.groups)
            if (group.id == shell.selectedGroup) {
                heading = group.name;
                headingColor = group.color;
            }
    }
    const float edgeL = Px(16);
    const float edgeR = Px(12);
    const float gap = Px(2);
    const float icon = Px(22);
    const float searchW = Px(132);
    const bool showSearch = shell.groupSearchOpen || !shell.groupFilter.empty();
    float cursor = ImGui::GetWindowWidth() - edgeR;
    const auto place = [&](float itemWidth, float itemHeight) {
        cursor -= itemWidth;
        ImGui::SetCursorPos(ImVec2(cursor, (headerH - itemHeight) * 0.5f));
        cursor -= gap;
    };
    place(icon, icon);
    if (QuietIcon(shell, "close-groups", ToolGlyph("close"), "关闭")) shell.showGroups = false;
    place(icon, icon);
    if (QuietIcon(shell, "gear-groups", ToolGlyph("gear"), "设置")) shell.ShowSettings();
    place(icon, icon);
    if (QuietIcon(shell, "search-groups", ToolGlyph("search"), "搜索", showSearch)) {
        if (shell.groupFilter.empty() && shell.groupSearchOpen) shell.groupSearchOpen = false;
        else {
            shell.groupSearchOpen = true;
            shell.groupSearchFocus = true;
        }
    }
    if (showSearch) {
        place(searchW, icon);
        ImGui::SetNextItemWidth(searchW);
        if (shell.groupFilter.capacity() < 256) shell.groupFilter.reserve(256);
        ImGui::PushStyleVar(ImGuiStyleVar_FramePadding, ImVec2(Px(8), Px(2)));
        ImGui::PushStyleVar(ImGuiStyleVar_FrameRounding, Px(8));
        ImGui::PushStyleColor(ImGuiCol_FrameBg, Paint(shell, "bg.subtle"));
        ImGui::PushStyleColor(ImGuiCol_Border, Paint(shell, "border.default"));
        const bool wantFocus = shell.groupSearchFocus;
        if (wantFocus) ImGui::SetKeyboardFocusHere();
        ImGui::InputTextWithHint("##filter", "搜索", &shell.groupFilter);
        if (ImGui::IsItemActive()) shell.groupSearchFocus = false;
        else if (!wantFocus && shell.groupFilter.empty()) shell.groupSearchOpen = false;
        ImGui::PopStyleColor(2);
        ImGui::PopStyleVar(2);
    }
    place(icon, icon);
    if (QuietIcon(shell, "edit", shell.groupsEditing ? ToolGlyph("check") : ToolGlyph("edit"), shell.groupsEditing ? "完成" : "编辑", shell.groupsEditing)) {
        shell.groupsEditing = !shell.groupsEditing;
        if (shell.groupsEditing) shell.editWobbleStart = ImGui::GetTime();
    }
    place(icon, icon);
    if (QuietIcon(shell, "add-file", ToolGlyph("plus"), "添加")) AddFiles(shell);
    ImGui::SetCursorPos(ImVec2(edgeL, (headerH - ImGui::GetFontSize()) * 0.5f));
    ImGui::TextDisabled("DeskKit");
    ImGui::SameLine();
    if (!headingColor.empty()) {
        const ImVec2 bar = ImGui::GetCursorScreenPos();
        ImGui::Dummy(ImVec2(Px(12), ImGui::GetFontSize()));
        header->AddRectFilled(ImVec2(bar.x, bar.y + Px(2)), ImVec2(bar.x + Px(3), bar.y + ImGui::GetFontSize() - Px(2)), ImGui::ColorConvertFloat4ToU32(Hex(headingColor)), Px(2));
        ImGui::SameLine();
    }
    ImGui::Text("%s", heading.c_str());
    DragGroups(shell, ImGui::IsWindowHovered() && !ImGui::IsAnyItemActive());
    ImGui::EndChild();

    ImGui::BeginChild("nav", ImVec2(navW, 0), false, ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoScrollWithMouse);
    ImGui::GetWindowDrawList()->AddRectFilled(ImGui::GetWindowPos(), ImGui::GetWindowPos() + ImGui::GetWindowSize(), PaintU(shell, "bg.float", 0.75f));
    const float newGroupH = Px(34);
    ImGui::BeginChild("nav-scroll", ImVec2(0, -(newGroupH + Px(16))), false, ImGuiWindowFlags_NoScrollWithMouse);
    ImGui::Dummy(ImVec2(0, Px(8)));
    bool bringNav = false;
    if (shell.renamingGroup.empty() && !ImGui::IsPopupOpen(nullptr, ImGuiPopupFlags_AnyPopup) && ImGui::IsWindowHovered() && ImGui::GetIO().MouseWheel != 0.f) {
        std::vector<std::string> ids;
        ids.emplace_back("all");
        for (const Group& group : session.library.groups)
            if (group.id != "fav") ids.push_back(group.id);
        int index = 0;
        for (int i = 0; i < static_cast<int>(ids.size()); ++i)
            if (ids[static_cast<size_t>(i)] == shell.selectedGroup) index = i;
        const float wheel = ImGui::GetIO().MouseWheel;
        const int step = wheel > 0.f ? -1 : 1;
        const int times = std::max(1, static_cast<int>(std::fabs(wheel)));
        for (int n = 0; n < times; ++n) index = std::clamp(index + step, 0, static_cast<int>(ids.size()) - 1);
        if (!ids.empty() && shell.selectedGroup != ids[static_cast<size_t>(index)]) {
            shell.selectedGroup = ids[static_cast<size_t>(index)];
            bringNav = true;
        }
    }
    auto navRow = [&](const char* id, const std::string& name, const std::string& color) {
        ImGui::PushID(id);
        ImGui::SetCursorPosX(Px(8));
        const bool on = shell.selectedGroup == id;
        const ImVec2 origin = ImGui::GetCursorScreenPos();
        const float height = Px(34);
        const float width = navW - Px(16);
        if (ImGui::Selectable("##nav", on, 0, ImVec2(width, height))) shell.selectedGroup = id;
        if (color.empty() == false && ImGui::BeginDragDropSource()) {
            ImGui::SetDragDropPayload("dk-group", id, std::strlen(id) + 1);
            ImGui::TextUnformatted(name.c_str());
            ImGui::EndDragDropSource();
        }
        if (ImGui::BeginDragDropTarget()) {
            if (const ImGuiPayload* payload = ImGui::AcceptDragDropPayload("dk-item")) {
                const std::string data(static_cast<const char*>(payload->Data));
                const auto split = data.find('\n');
                if (split != std::string::npos && std::string(id) != "all") {
                    pendingMoveItem = data.substr(0, split);
                    pendingMoveFrom = data.substr(split + 1);
                    pendingMoveTo = id;
                }
            }
            if (const ImGuiPayload* payload = ImGui::AcceptDragDropPayload("dk-group")) {
                if (std::string(id) != "all") session.ReorderGroup(static_cast<const char*>(payload->Data), id, true);
            }
            ImGui::EndDragDropTarget();
        }
        ImDrawList* draw = ImGui::GetWindowDrawList();
        if (on) {
            draw->AddRectFilled(origin, ImVec2(origin.x + width, origin.y + height), PaintU(shell, "accent.soft"), Px(8));
            if (bringNav) ImGui::SetScrollHereY(0.5f);
        }
        float textX = origin.x + Px(10);
        if (!color.empty()) {
            draw->AddCircleFilled(ImVec2(origin.x + Px(16), origin.y + height * 0.5f), Px(3), ImGui::ColorConvertFloat4ToU32(Hex(color)));
            textX += Px(14);
        }
        const float labelW = std::max(Px(48.f), origin.x + width - Px(8) - textX);
        if (shell.renamingGroup == id) {
            ImGui::SetCursorScreenPos(ImVec2(textX, origin.y + Px(3)));
            ImGui::SetNextItemWidth(labelW);
            if (shell.renameFocus) {
                ImGui::SetKeyboardFocusHere();
                shell.renameFocus = false;
            }
            if (ImGui::InputText("##rename", &shell.renameBuffer, ImGuiInputTextFlags_EnterReturnsTrue | ImGuiInputTextFlags_AutoSelectAll)) {
                session.RenameGroup(id, shell.renameBuffer);
                shell.renamingGroup.clear();
            }
            if (UserPressedEscape(shell)) shell.renamingGroup.clear();
        } else if (g_smallFont) {
            ImGui::PushFont(g_smallFont);
            const std::string shown = FitText(name, labelW);
            ImGui::PopFont();
            const float textY = origin.y + (height - g_smallFont->LegacySize) * 0.5f;
            draw->AddText(g_smallFont, g_smallFont->LegacySize, ImVec2(textX, textY), PaintU(shell, on ? "accent.hover" : "text.secondary"), shown.c_str());
        } else {
            const std::string shown = FitText(name, labelW);
            draw->AddText(ImVec2(textX, origin.y + Px(8)), PaintU(shell, on ? "accent.hover" : "text.secondary"), shown.c_str());
        }
        if (std::string(id) != "all" && ImGui::BeginPopupContextItem("nav-menu")) {
            sawMenu = true;
            shell.suppressBlur = true;
            if (ImGui::MenuItem(session.store.Text("group.rename").c_str())) {
                shell.renamingGroup = id;
                shell.renameBuffer = name;
            }
            if (ImGui::MenuItem(session.store.Text("group.delete").c_str())) pendingDeleteGroup = id;
            ImGui::EndPopup();
        }
        ImGui::PopID();
    };
    navRow("all", "全部", "");
    for (const Group& group : session.library.groups) {
        if (group.id == "fav") continue;
        navRow(group.id.c_str(), group.name, group.color);
    }
    DragGroups(shell, ImGui::IsWindowHovered() && !ImGui::IsAnyItemHovered());
    ImGui::EndChild();
    ImGui::SetCursorPos(ImVec2(Px(8), ImGui::GetCursorPosY() + Px(8)));
    if (SoftButton(shell, "new-group", "新建分组", false, ImVec2(navW - Px(16), newGroupH))) AddGroup(shell);
    const ImVec2 navOrigin = ImGui::GetWindowPos();
    ImGui::GetWindowDrawList()->AddLine(ImVec2(navOrigin.x + navW - 1, navOrigin.y), ImVec2(navOrigin.x + navW - 1, navOrigin.y + ImGui::GetWindowHeight()), PaintU(shell, "border.default"));
    DragGroups(shell, ImGui::IsWindowHovered() && !ImGui::IsAnyItemHovered());
    ImGui::EndChild();
    ImGui::SameLine();

    ImGui::BeginChild("grid", ImVec2(0, 0), false);
    const float cell = Px(session.store.Layout({"cell", "shortcut"}));
    shell.hoverGroup.clear();
    shell.groupHits.clear();
    bool focusEmpty = false;
    bool anyCard = false;
    if (shell.selectedGroup != "all" && !shell.groupsEditing) {
        for (const Group& group : session.library.groups) {
            if (group.id == shell.selectedGroup && group.itemIds.empty()) focusEmpty = true;
        }
    }
    if (focusEmpty) {
        const float width = ImGui::GetWindowWidth();
        ImGui::SetCursorPosY(std::max(Px(48.f), (ImGui::GetWindowHeight() - Px(96)) * 0.5f));
        const std::string arrow = "↓";
        ImGui::SetCursorPosX((width - ImGui::CalcTextSize(arrow.c_str()).x) * 0.5f);
        ImGui::TextDisabled("%s", arrow.c_str());
        const std::string drop = session.store.Text("empty.panelDrop");
        ImGui::SetCursorPosX((width - ImGui::CalcTextSize(drop.c_str()).x) * 0.5f);
        ImGui::TextDisabled("%s", drop.c_str());
        ImGui::Dummy(ImVec2(0, Px(8)));
        const std::string hint = session.store.Text("empty.panel");
        ImGui::SetCursorPosX((width - ImGui::CalcTextSize(hint.c_str()).x) * 0.5f);
        ImGui::TextDisabled("%s", hint.c_str());
    } else for (const Group& group : session.library.groups) {
        if (group.id == "fav") continue;
        if (shell.selectedGroup != "all" && group.id != shell.selectedGroup) continue;
        if (!shell.groupFilter.empty()) {
            bool matched = false;
            for (const std::string& itemId : group.itemIds) {
                for (const Shortcut& item : session.library.items) {
                    if (item.id == itemId && ShortcutMatches(item, shell.groupFilter)) matched = true;
                }
                if (matched) break;
            }
            if (!matched) continue;
        }
        anyCard = true;
        const bool plain = shell.selectedGroup != "all";
        const bool folded = !plain && Folded(shell, group.id);
        ImGui::PushID(group.id.c_str());
        if (!plain) {
            if (ImGui::GetCursorPosY() < Px(4)) ImGui::Dummy(ImVec2(0, Px(8)));
            BeginCard(shell, "card", "", 0.86f, 8.f, 8.f);
            const float titleTop = ImGui::GetCursorPosY();
            const ImVec2 bar = ImGui::GetCursorScreenPos();
            ImGui::GetWindowDrawList()->AddRectFilled(bar, ImVec2(bar.x + Px(3), bar.y + ImGui::GetFontSize()), ImGui::ColorConvertFloat4ToU32(Hex(group.color)), Px(2));
            ImGui::SetCursorPosX(ImGui::GetCursorPosX() + Px(8));
            ImGui::Text("%s", group.name.c_str());
            const float chevron = Px(20);
            const float titleBand = std::max(ImGui::GetTextLineHeight(), chevron);
            ImGui::SetCursorPos(ImVec2(ImGui::GetWindowWidth() - Px(8) - chevron, titleTop + (titleBand - chevron) * 0.5f));
            if (ChevronButton(shell, "fold", folded)) ToggleFold(shell, group.id);
            if (!folded) ImGui::SetCursorPos(ImVec2(ImGui::GetStyle().WindowPadding.x, titleTop + titleBand + Px(4)));
        } else {
            ImGui::SetCursorPos(ImVec2(Px(12), Px(12)));
        }
        if (!folded) {
            if (group.itemIds.empty() && !shell.groupsEditing) {
                ImGui::TextDisabled("%s", session.store.Text("empty.panelStack").c_str());
            }
            int shown = 0;
            const float inner = ImGui::GetWindowContentRegionMax().x - ImGui::GetWindowContentRegionMin().x;
            const float baseGap = Px(8);
            const int columns = std::max(1, static_cast<int>((inner + baseGap) / (cell + baseGap)));
            float gap = baseGap;
            if (columns > 1) {
                const float extra = inner - (columns * cell + (columns - 1) * baseGap);
                if (extra > 1.f) gap = baseGap + extra / static_cast<float>(columns - 1);
            }
            ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing, ImVec2(gap, Px(6)));
            for (const std::string& itemId : group.itemIds) {
                const Shortcut* shortcut = nullptr;
                for (const Shortcut& item : session.library.items)
                    if (item.id == itemId) shortcut = &item;
                if (!shortcut || !ShortcutMatches(*shortcut, shell.groupFilter)) continue;
                const std::string shortcutId = shortcut->id;
                const std::string shortcutPath = shortcut->path;
                const std::string shortcutKind = shortcut->kind;
                const std::string shortcutTitle = shortcut->title;
                const std::string groupId = group.id;
                if (shown % columns != 0) ImGui::SameLine(0, gap);
                ImGui::PushID(shortcutId.c_str());
                ImGui::BeginChild("cell", ImVec2(cell, cell), ImGuiChildFlags_None, ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoScrollWithMouse);
                const ImVec2 origin = ImGui::GetCursorScreenPos();
                const float badge = Px(18);
                const float wobble = EditWobble(shell, shown);
                const ImVec2 pivot(origin.x + cell * 0.5f, origin.y + cell * 0.5f);
                ImVec2 badgePos(origin.x + Px(2), origin.y + Px(2));
                if (shell.groupsEditing && wobble != 0.f) badgePos = RotatePoint(badgePos + ImVec2(badge, badge) * 0.5f, pivot, wobble) - ImVec2(badge, badge) * 0.5f;
                const ImVec2 badgeEnd = badgePos + ImVec2(badge, badge);
                const ImVec2 hitMin = badgePos - ImVec2(Px(4), Px(4));
                const ImVec2 hitMax = badgeEnd + ImVec2(Px(4), Px(4));
                const auto insideRect = [](ImVec2 point, ImVec2 min, ImVec2 max) {
                    return point.x >= min.x && point.x < max.x && point.y >= min.y && point.y < max.y;
                };
                const ImVec2 pressed = ImGui::GetIO().MouseClickedPos[ImGuiMouseButton_Left];
                const bool pressOnBadge = shell.groupsEditing && insideRect(pressed, hitMin, hitMax);
                const bool open = ImGui::Selectable("##cell", false, ImGuiSelectableFlags_AllowOverlap, ImVec2(cell, cell));
                if (!pressOnBadge && ImGui::BeginDragDropSource()) {
                    const std::string payload = shortcutId + "\n" + groupId;
                    ImGui::SetDragDropPayload("dk-item", payload.c_str(), payload.size() + 1);
                    ImGui::TextUnformatted(shortcutTitle.c_str());
                    ImGui::EndDragDropSource();
                }
                // 菜单必须挂在这个格子自己的编号上，而且要在 EndChild 之前打开。所有格子共用一个菜单名时，右键会让界面报错。
                if (ImGui::BeginPopupContextItem("menu")) {
                    sawMenu = true;
                    shell.suppressBlur = true;
                    if (ImGui::BeginMenu(session.store.Text("shortcut.move").c_str())) {
                        bool any = false;
                        for (const Group& target : session.library.groups) {
                            if (target.id == groupId || target.id == "fav") continue;
                            any = true;
                            if (ImGui::MenuItem(target.name.c_str())) {
                                pendingMoveItem = shortcutId;
                                pendingMoveFrom = groupId;
                                pendingMoveTo = target.id;
                            }
                        }
                        if (!any) ImGui::TextDisabled("%s", session.store.Text("shortcut.moveEmpty").c_str());
                        ImGui::EndMenu();
                    }
                    if (shortcutKind != "url" && ImGui::MenuItem(session.store.Text("shortcut.reveal").c_str())) RevealTarget(shortcutPath);
                    if (ImGui::MenuItem(session.store.Text("shortcut.delete").c_str())) pendingDeleteItem = shortcutId;
                    ImGui::EndPopup();
                }
                ImDrawList* draw = ImGui::GetWindowDrawList();
                if (ImGui::IsItemHovered() && !pressOnBadge) draw->AddRectFilled(origin, ImVec2(origin.x + cell, origin.y + cell), PaintU(shell, "bg.hover"), Px(8));
                const ImVec2 iconMin(origin.x + cell * 0.5f - Px(16), origin.y + Px(8));
                const ImVec2 iconMax(origin.x + cell * 0.5f + Px(16), origin.y + Px(40));
                if (const ImTextureID texture = FileIcon(shortcutPath, 32)) {
                    if (wobble == 0.f) draw->AddImage(texture, iconMin, iconMax);
                    else draw->AddImageQuad(texture, RotatePoint(iconMin, pivot, wobble), RotatePoint(ImVec2(iconMax.x, iconMin.y), pivot, wobble), RotatePoint(iconMax, pivot, wobble), RotatePoint(ImVec2(iconMin.x, iconMax.y), pivot, wobble));
                } else {
                    const ImVec2 glyph = wobble == 0.f ? ImVec2(origin.x + cell * 0.5f, origin.y + Px(24)) : RotatePoint(ImVec2(origin.x + cell * 0.5f, origin.y + Px(24)), pivot, wobble);
                    DrawGlyph(shortcutKind == "url" ? ToolGlyph("link") : ToolGlyph("app"), PaintU(shell, "accent"), glyph, 18);
                }
                if (g_smallFont) ImGui::PushFont(g_smallFont);
                const std::string label = FitText(shortcutTitle, cell - Px(8));
                const ImVec2 labelSize = ImGui::CalcTextSize(label.c_str());
                ImVec2 labelPos(origin.x + (cell - labelSize.x) * 0.5f, origin.y + cell - labelSize.y - Px(6));
                if (wobble != 0.f) labelPos = RotatePoint(labelPos + labelSize * 0.5f, pivot, wobble) - labelSize * 0.5f;
                draw->AddText(labelPos, PaintU(shell, "text.secondary"), label.c_str());
                if (g_smallFont) ImGui::PopFont();
                if (shell.groupsEditing) {
                    draw->AddRectFilled(badgePos, badgeEnd, PaintU(shell, "bg.float"), Px(4));
                    draw->AddRect(badgePos, badgeEnd, PaintU(shell, "border.strong"), Px(4), 0, 1.f);
                    const ImVec2 mid = badgePos + ImVec2(badge * 0.5f, badge * 0.5f);
                    draw->AddLine(ImVec2(mid.x - Px(4), mid.y), ImVec2(mid.x + Px(4), mid.y), PaintU(shell, "text.secondary"), std::max(1.f, Px(1.6f)));
                    const ImVec2 mouse = ImGui::GetIO().MousePos;
                    if (pressOnBadge && insideRect(mouse, hitMin, hitMax) && ImGui::IsMouseReleased(ImGuiMouseButton_Left) && ImGui::GetDragDropPayload() == nullptr) pendingDeleteItem = shortcutId;
                }
                ImGui::EndChild();
                ImGui::PopID();
                if (open && !shell.groupsEditing && !ImGui::IsMouseReleased(ImGuiMouseButton_Right) && ImGui::GetDragDropPayload() == nullptr) {
                    OpenTarget(shortcutPath);
                    session.TouchRecentFromPath(shortcutPath, shortcutKind, shortcutTitle);
                    shell.showGroups = false;
                }
                ++shown;
            }
            ImGui::PopStyleVar();
            if (shell.groupsEditing) {
                if (shown % columns != 0) ImGui::SameLine(0, gap);
                ImGui::BeginChild("add-cell", ImVec2(cell, cell), ImGuiChildFlags_None, ImGuiWindowFlags_NoScrollbar | ImGuiWindowFlags_NoScrollWithMouse);
                const ImVec2 origin = ImGui::GetCursorScreenPos();
                if (ImGui::InvisibleButton("##add-cell", ImVec2(cell, cell))) AddFiles(shell);
                ImDrawList* add = ImGui::GetWindowDrawList();
                add->AddRect(origin + ImVec2(Px(8), Px(8)), origin + ImVec2(cell - Px(8), cell - Px(8)), PaintU(shell, "border.strong"), Px(8));
                const char* plus = "添加";
                const ImVec2 plusSize = ImGui::CalcTextSize(plus);
                add->AddText(origin + ImVec2((cell - plusSize.x) * 0.5f, (cell - plusSize.y) * 0.5f), PaintU(shell, "text.tertiary"), plus);
                ImGui::EndChild();
            }
        }
        const ImVec2 cardMin = ImGui::GetWindowPos();
        const ImVec2 cardMax = cardMin + ImGui::GetWindowSize();
        shell.groupHits.push_back(GroupHit{group.id, cardMin.x, cardMin.y, cardMax.x - cardMin.x, cardMax.y - cardMin.y});
        const ImVec2 point = shell.externalDrop ? ImVec2(shell.dropX, shell.dropY) : ImGui::GetMousePos();
        const bool inside = point.x >= cardMin.x && point.x < cardMax.x && point.y >= cardMin.y && point.y < cardMax.y;
        if (inside) {
            shell.hoverGroup = group.id;
            if (!plain) {
                if (const ImGuiPayload* payload = ImGui::GetDragDropPayload(); payload && payload->IsDataType("dk-item")) {
                    ImGui::GetWindowDrawList()->AddRect(cardMin, cardMax, PaintU(shell, "accent"), Px(10), 0, 2.f);
                    if (ImGui::IsMouseReleased(ImGuiMouseButton_Left)) {
                        const std::string data(static_cast<const char*>(payload->Data));
                        const auto split = data.find('\n');
                        if (split != std::string::npos && data.substr(split + 1) != group.id) {
                            pendingMoveItem = data.substr(0, split);
                            pendingMoveFrom = data.substr(split + 1);
                            pendingMoveTo = group.id;
                        }
                    }
                }
            }
        }
        if (!plain) {
            EndCard();
            ImGui::Dummy(ImVec2(0, Px(8)));
        }
        ImGui::PopID();
    }
    if (!focusEmpty && !anyCard && !shell.groupFilter.empty()) {
        const char* empty = "没有找到相关快捷项";
        const float width = ImGui::GetWindowWidth();
        ImGui::SetCursorPosY(Px(48));
        ImGui::SetCursorPosX((width - ImGui::CalcTextSize(empty).x) * 0.5f);
        ImGui::TextDisabled("%s", empty);
    }
    ImGui::EndChild();

    if (shell.externalDrop) {
        std::string name = "未分组";
        const std::string id = DropGroup(shell);
        for (const Group& group : session.library.groups)
            if (group.id == id) name = group.name;
        const std::string veil = "松手添加到「" + name + "」";
        ImDrawList* draw = ImGui::GetWindowDrawList();
        const ImVec2 origin = ImGui::GetWindowPos();
        const ImVec2 end = origin + ImGui::GetWindowSize();
        draw->AddRectFilled(origin, end, PaintU(shell, "bg.float", 0.9f), Px(shell.session->store.Layout({"radius", "float"})));
        const ImVec2 veilSize = ImGui::CalcTextSize(veil.c_str());
        draw->AddText(origin + (end - origin - veilSize) * 0.5f, PaintU(shell, "text.primary"), veil.c_str());
    }
    if (!pendingDeleteGroup.empty()) shell.confirmDeleteId = pendingDeleteGroup;
    if (UserPressedEscape(shell)) {
        if (!shell.confirmDeleteId.empty()) shell.confirmDeleteId.clear();
        else if (shell.renamingGroup.empty()) shell.showGroups = false;
    }
    if (!shell.externalDrop && shell.confirmDeleteId.empty()) shell.suppressBlur = sawMenu;
    try {
        if (!pendingMoveItem.empty()) session.MoveShortcut(pendingMoveItem, pendingMoveFrom, pendingMoveTo);
        else if (!pendingDeleteItem.empty()) session.RemoveShortcut(pendingDeleteItem);
    } catch (const std::exception& ex) {
        LogWarn(std::string("快捷启动操作失败 ") + ex.what());
    }
    ImGui::PopStyleVar();
    // 确认框放在列表画完之后。这样删除不会发生在正在遍历的分组数组里，也不会弹出系统对话框把界面弄乱。
    DrawDeleteConfirm(shell);
}

void DrawToolPage(Shell& shell) {
    Session& session = *shell.session;
    ImGui::BeginChild("page", ImVec2(0, -Px(44)), false);
    const bool split = shell.activeTool == "calc" || shell.activeTool == "unit" || shell.activeTool == "translate";
    if (split) ImGui::BeginChild("left", ImVec2(ImGui::GetContentRegionAvail().x * 0.5f - Px(8), 0), false);
    if (shell.activeTool == "calc" || shell.activeTool == "unit") {
        ImGui::SetCursorPos(ImVec2(Px(16), Px(16)));
        ImGui::TextDisabled("输入表达式 / 单位换算");
        ImGui::SetCursorPosX(Px(16));
        if (g_inputFont) ImGui::PushFont(g_inputFont);
        ImGui::SetNextItemWidth(-Px(16));
        if (ImGui::InputText("##expr", &shell.toolInput)) PublishTool(shell);
        if (g_inputFont) ImGui::PopFont();
        ImGui::SetCursorPosX(Px(16));
        for (const char* demo : {"123*4", "520+999-1314", "(1+2)*3", "3km to mi"}) {
            if (Chip(shell, demo, demo, shell.toolInput == demo)) {
                shell.toolInput = demo;
                PublishTool(shell);
            }
            ImGui::SameLine();
        }
        ImGui::NewLine();
        ImGui::SetCursorPosX(Px(16));
        ImGui::TextDisabled("支持四则运算与长度 / 重量 / 温度 / 离线汇率换算。");
    } else if (shell.activeTool == "translate") {
        ImGui::SetCursorPos(ImVec2(Px(16), Px(16)));
        ImGui::TextDisabled("输入（中英互译）");
        ImGui::SetCursorPosX(Px(16));
        if (ImGui::InputTextMultiline("##src", &shell.toolInput, ImVec2(-Px(16), Px(120)))) PublishTool(shell);
        ImGui::SetCursorPosX(Px(16));
        ImGui::TextDisabled("%s", session.store.Text("translate.offline").c_str());
    }
    if (split) {
        ImGui::EndChild();
        ImGui::SameLine();
        ImGui::BeginChild("right", ImVec2(0, 0), false);
        ImGui::SetCursorPos(ImVec2(Px(12), Px(16)));
        ImGui::TextDisabled(shell.activeTool == "translate" ? "译文" : "结果");
        ImGui::SetCursorPosX(Px(12));
        if (g_inputFont) ImGui::PushFont(g_inputFont);
        ImGui::PushTextWrapPos(ImGui::GetCursorPosX() + ImGui::GetContentRegionAvail().x - Px(12));
        if (shell.activeTool == "translate") ImGui::TextColored(Paint(shell, "accent.hover"), "%s", shell.toolOutput.c_str());
        else ImGui::TextWrapped("%s", shell.toolOutput.c_str());
        ImGui::PopTextWrapPos();
        if (g_inputFont) ImGui::PopFont();
        ImGui::EndChild();
    } else if (shell.activeTool == "color") {
        ImGui::TextDisabled("颜色");
        if (ImGui::InputText("##color", &shell.toolInput)) PublishTool(shell);
        if (const auto rgb = ParseColor(shell.toolInput)) {
            const ImVec2 swatch = ImGui::GetCursorScreenPos();
            ImGui::Dummy(ImVec2(ImGui::GetContentRegionAvail().x, Px(80)));
            ImGui::GetWindowDrawList()->AddRectFilled(swatch, ImVec2(swatch.x + ImGui::GetContentRegionAvail().x, swatch.y + Px(80)), IM_COL32(rgb->r, rgb->g, rgb->b, 255), Px(8));
        }
        ImGui::TextWrapped("%s", shell.toolOutput.c_str());
    } else if (shell.activeTool == "timestamp") {
        ImGui::TextDisabled("时间戳 ↔ 日期");
        if (ImGui::InputText("##time", &shell.toolInput)) PublishTool(shell);
        ImGui::SameLine();
        if (SoftButton(shell, "now", "现在")) {
            shell.toolInput = std::to_string(std::chrono::duration_cast<std::chrono::seconds>(std::chrono::system_clock::now().time_since_epoch()).count());
            PublishTool(shell);
        }
        ImGui::TextWrapped("%s", shell.toolOutput.c_str());
        ImGui::TextDisabled("时区：系统当前时区");
    } else if (shell.activeTool == "clipboard") {
        if (session.clips.empty()) ImGui::TextDisabled("%s", session.store.Text("empty.clipboard").c_str());
        for (const Clip& clip : session.clips) {
            const std::string label = clip.text.size() > 64 ? clip.text.substr(0, 64) : clip.text;
            if (SoftButton(shell, clip.id.c_str(), label.c_str())) {
                CopyText(clip.text);
                shell.toolOutput = clip.text;
            }
        }
        if (SoftButton(shell, "clear-clips", "清空记录")) {
            session.clips.clear();
            session.Save();
        }
    } else if (shell.activeTool == "picker") {
        ImGui::TextWrapped("点一下屏幕上的颜色，结果会复制到剪贴板。按 Esc 或右键取消。");
        if (SoftButton(shell, "pick", "开始取色", true)) shell.pickingColor = true;
        if (!shell.pickedHex.empty()) ImGui::Text("%s", shell.pickedHex.c_str());
    } else {
        ImGui::Text("二维码");
        ImGui::TextWrapped("二维码生成放在后续版本。现在可以把文本留在剪贴板。");
    }
    ImGui::EndChild();
    ImGui::BeginChild("tool-foot", ImVec2(0, Px(44)), false, ImGuiWindowFlags_NoScrollbar);
    ImGui::GetWindowDrawList()->AddRectFilled(ImGui::GetWindowPos(), ImGui::GetWindowPos() + ImGui::GetWindowSize(), PaintU(shell, "bg.subtle"));
    ImGui::SetCursorPos(ImVec2(Px(16), Px(8)));
    if (SoftButton(shell, "copy-out", "Enter 复制结果", true) && !shell.toolOutput.empty()) CopyText(shell.toolOutput);
    ImGui::SameLine();
    ImGui::TextDisabled("Esc 返回宫格 · 本地计算，零网络请求");
    ImGui::EndChild();
    if (ImGui::IsKeyPressed(ImGuiKey_Enter) && !shell.toolOutput.empty() && !ImGui::IsAnyItemActive()) CopyText(shell.toolOutput);
}

void DrawTools(Shell& shell) {
    Session& session = *shell.session;
    const float headerH = shell.toolMode == "page" ? Px(48) : Px(44);
    ImGui::BeginChild("tools-head", ImVec2(0, headerH), false, ImGuiWindowFlags_NoScrollbar);
    ImGui::SetCursorPos(ImVec2(Px(8), (headerH - Px(28)) * 0.5f));
    if (shell.toolMode == "page") {
        if (IconButton(shell, "back", ToolGlyph("back"))) shell.CloseTool();
        ImGui::SameLine();
        ImGui::Text("%s", ToolTitle(shell.activeTool).c_str());
        ImGui::SameLine();
        ImGui::TextColored(Paint(shell, "accent.hover"), "内置工具 · 离线可用");
    } else {
        ImGui::SetCursorPosX(Px(16));
        ImGui::Text("常用工具");
        ImGui::SameLine();
        ImGui::TextDisabled("%s", session.clips.empty() ? "剪贴板：无新内容" : "剪贴板：1 条新");
        ImGui::SameLine(ImGui::GetWindowWidth() - Px(40));
        if (IconButton(shell, "close-tools", ToolGlyph("close"))) shell.CloseTool();
    }
    ImGui::EndChild();
    if (shell.toolMode == "page") {
        DrawToolPage(shell);
        if (UserPressedEscape(shell)) shell.CloseTool();
        return;
    }
    const char* order[] = {"clipboard", "picker", "translate", "calc", "timestamp", "unit", "color", "qr", "add"};
    const float cellW = (ImGui::GetWindowWidth() - Px(36)) / 3.f;
    const float cellH = (ImGui::GetContentRegionAvail().y - Px(44)) / 3.f;
    ImGui::SetCursorPosX(Px(12));
    for (int i = 0; i < 9; ++i) {
        const char* id = order[i];
        const bool add = std::string(id) == "add";
        if (i % 3 != 0) ImGui::SameLine();
        ImGui::PushID(id);
        const ImVec2 origin = ImGui::GetCursorScreenPos();
        const bool pressed = ImGui::Selectable("##tool", false, 0, ImVec2(cellW, cellH));
        ImDrawList* draw = ImGui::GetWindowDrawList();
        draw->AddRectFilled(origin, ImVec2(origin.x + cellW - Px(8), origin.y + cellH - Px(8)), PaintU(shell, add ? "bg.subtle" : "bg.float"), Px(10));
        draw->AddRect(origin, ImVec2(origin.x + cellW - Px(8), origin.y + cellH - Px(8)), PaintU(shell, add ? "border.strong" : "border.default"), Px(10));
        const ImVec2 center(origin.x + (cellW - Px(8)) * 0.5f, origin.y + cellH * 0.38f);
        DrawGlyph(add ? ToolGlyph("plus") : ToolGlyph(id), PaintU(shell, add ? "text.tertiary" : "accent"), center, 22);
        const char* label = add ? "排序 / 显示" : ToolTitle(id).c_str();
        const ImVec2 labelSize = ImGui::CalcTextSize(label);
        draw->AddText(ImVec2(center.x - labelSize.x * 0.5f, origin.y + cellH * 0.62f), PaintU(shell, "text.secondary"), label);
        const std::string number = std::to_string(i + 1);
        draw->AddText(ImVec2(origin.x + cellW - Px(28), origin.y + cellH - Px(28)), PaintU(shell, "text.tertiary"), number.c_str());
        ImGui::PopID();
        if (pressed || ImGui::IsKeyPressed(static_cast<ImGuiKey>(ImGuiKey_1 + i))) {
            if (add) {
                session.settings.hiddenTools = session.settings.hiddenTools.empty() ? std::vector<std::string>{"qr"} : std::vector<std::string>{};
                session.Save();
            } else if (!Hidden(session.settings, id)) shell.ShowTool(id);
        }
    }
    ImGui::SetCursorPosX(0);
    ImGui::BeginChild("tools-foot", ImVec2(0, Px(32)), false);
    ImGui::GetWindowDrawList()->AddRectFilled(ImGui::GetWindowPos(), ImGui::GetWindowPos() + ImGui::GetWindowSize(), PaintU(shell, "bg.subtle"));
    const std::string foot = std::string("数字键 1–9 直达 · ") + session.store.FormatChord("windows", "tools") + " 打开面板 · Esc 返回";
    const ImVec2 footSize = ImGui::CalcTextSize(foot.c_str());
    ImGui::SetCursorPos(ImVec2((ImGui::GetWindowWidth() - footSize.x) * 0.5f, Px(8)));
    ImGui::TextDisabled("%s", foot.c_str());
    ImGui::EndChild();
    if (UserPressedEscape(shell)) shell.CloseTool();
}

void DrawSettings(Shell& shell) {
    Session& session = *shell.session;
    const float headerH = Px(48);
    const float navW = Px(session.store.Layout({"windows", "settings", "navWidth"}));
    ImGui::BeginChild("settings-head", ImVec2(0, headerH), false, ImGuiWindowFlags_NoScrollbar);
    ImGui::SetCursorPos(ImVec2(Px(16), (headerH - ImGui::GetFontSize()) * 0.5f));
    ImGui::Text("设置");
    ImGui::SameLine(ImGui::GetWindowWidth() - Px(44));
    if (IconButton(shell, "close-settings", ToolGlyph("close"))) shell.showSettings = false;
    ImGui::EndChild();
    ImGui::BeginChild("settings-nav", ImVec2(navW, -Px(36)), false);
    ImGui::Dummy(ImVec2(0, Px(8)));
    for (const auto& [id, label] : {std::pair{"general", "常规"}, {"hotkeys", "快捷键"}, {"about", "关于与双端信息"}}) {
        ImGui::PushID(id);
        ImGui::SetCursorPosX(Px(8));
        const bool on = shell.settingsTab == id;
        const ImVec2 origin = ImGui::GetCursorScreenPos();
        const float rowW = navW - Px(16);
        const float rowH = Px(34);
        if (ImGui::Selectable("##tab", false, 0, ImVec2(rowW, rowH))) shell.settingsTab = id;
        ImDrawList* draw = ImGui::GetWindowDrawList();
        if (on) draw->AddRectFilled(origin, ImVec2(origin.x + rowW, origin.y + rowH), PaintU(shell, "accent.soft"), Px(8));
        const std::string shown = FitText(label, rowW - Px(20));
        const float textY = origin.y + (rowH - ImGui::GetFontSize()) * 0.5f;
        draw->AddText(ImVec2(origin.x + Px(10), textY), PaintU(shell, on ? "accent.hover" : "text.secondary"), shown.c_str());
        ImGui::PopID();
    }
    ImGui::EndChild();
    ImGui::SameLine();
    ImGui::BeginChild("settings-page", ImVec2(0, -Px(36)), false);
    if (shell.settingsTab == "general") {
        BeginCard(shell, "boot", "启动与呼出");
        BeginRow("开机自启", "登录后自动常驻，随时响应全局呼出键");
        if (Toggle(shell, "autostart", &session.settings.autoLaunch)) {
            SetAutoStart(session.settings.autoLaunch);
            session.Save();
        }
        BeginRow("呼出方式", "全局快捷键、托盘，或仅从托盘打开");
        if (Chip(shell, "mode-hotkey", "全局快捷键", session.settings.summonMode == "hotkey")) { session.settings.summonMode = "hotkey"; session.Save(); }
        ImGui::SameLine();
        if (Chip(shell, "mode-menu", "托盘", session.settings.summonMode == "menu")) { session.settings.summonMode = "menu"; session.Save(); }
        ImGui::SameLine();
        if (Chip(shell, "mode-edge", "仅托盘", session.settings.summonMode == "edge")) { session.settings.summonMode = "edge"; session.Save(); }
        BeginRow("失焦自动隐藏", "点击浮层外 150ms 后隐藏");
        if (Toggle(shell, "blur", &session.settings.blurHide)) session.Save();
        BeginRow("多屏行为", "浮层靠近屏幕边缘自动内收，不跨屏裁切");
        if (Chip(shell, "screen-cursor", "跟随鼠标", session.settings.multiScreen == "followCursor")) { session.settings.multiScreen = "followCursor"; session.Save(); }
        ImGui::SameLine();
        if (Chip(shell, "screen-last", "记住上次", session.settings.multiScreen == "last")) { session.settings.multiScreen = "last"; session.Save(); }
        ImGui::SameLine();
        if (Chip(shell, "screen-main", "主屏", session.settings.multiScreen == "main")) { session.settings.multiScreen = "main"; session.Save(); }
        EndCard();
        BeginCard(shell, "starter", "快捷启动");
        BeginRow("本机应用", "系统工具按分组放入，开始菜单里的其他已安装应用放进「应用」。已经有的不会重复添加");
        if (SoftButton(shell, "import", "放入")) {
            const int added = session.ImportStarterTools();
            MessageBoxW(nullptr, added == 0 ? L"能找到的都已经在快捷启动里。" : L"已放入新的快捷项。", L"快捷启动", MB_OK);
        }
        EndCard();
        BeginCard(shell, "look", "外观");
        BeginRow("主题", "浅色为默认。深色和跟随系统会立刻切换外观");
        if (Chip(shell, "theme-light", "浅色", session.settings.theme == "light")) { session.settings.theme = "light"; session.Save(); }
        ImGui::SameLine();
        if (Chip(shell, "theme-dark", "深色", session.settings.theme == "dark")) { session.settings.theme = "dark"; session.Save(); }
        ImGui::SameLine();
        if (Chip(shell, "theme-system", "跟随系统", session.settings.theme == "system")) { session.settings.theme = "system"; session.Save(); }
        BeginRow("强调色", "选中结果和主按钮使用这个颜色");
        for (const auto& [id, label] : {std::pair{"accent", "蓝"}, {"success", "绿"}, {"warning", "黄"}, {"danger", "红"}}) {
            if (Chip(shell, id, label, session.settings.accentToken == id)) { session.settings.accentToken = id; session.Save(); }
            ImGui::SameLine();
        }
        BeginRow("结果密度", "紧凑 40px，标准 48px");
        if (Chip(shell, "density-compact", "紧凑", session.settings.density == "compact")) { session.settings.density = "compact"; session.Save(); }
        ImGui::SameLine();
        if (Chip(shell, "density-standard", "标准", session.settings.density == "standard")) { session.settings.density = "standard"; session.Save(); }
        BeginRow("背景图片", "用于浮层、快捷启动、设置和工具页的底图");
        if (SoftButton(shell, "bg", session.settings.backgroundImagePath.empty() ? "选择图片" : "更换图片")) ChooseBackground(shell);
        ImGui::SameLine();
        if (SoftButton(shell, "bg-clear", "清除")) {
            session.settings.backgroundImagePath.clear();
            session.Save();
            g_backgroundPath.clear();
            g_background = 0;
        }
        BeginRow("背景透明度", "只影响底图或底色，文字和按钮保持清晰");
        float opacity = static_cast<float>(session.settings.backgroundOpacity * 100.0);
        ImGui::SetNextItemWidth(Px(160));
        if (ImGui::SliderFloat("##opacity", &opacity, 15.f, 100.f, "%.0f%%")) {
            session.settings.backgroundOpacity = opacity / 100.0;
            session.Save();
        }
        EndCard();
        BeginCard(shell, "data", "数据与隐私");
        BeginRow("本地优先", "分组、设置和剪贴板只存在本机");
        if (SoftButton(shell, "open-data", "打开数据目录")) OpenTarget(DataRoot());
        ImGui::SameLine();
        if (SoftButton(shell, "export", "导出数据")) ExportData(shell);
        BeginRow("可选云同步", "当前版本只记录开关，数据不会上传");
        if (Toggle(shell, "cloud", &session.settings.cloudSync)) session.Save();
        BeginRow("剪贴板保留时长", "超过期限的记录会在下次复制时清掉");
        if (Chip(shell, "keep-7", "7 天", session.settings.clipboardRetention == "7d")) { session.settings.clipboardRetention = "7d"; session.Save(); session.PruneClips(); }
        ImGui::SameLine();
        if (Chip(shell, "keep-30", "30 天", session.settings.clipboardRetention == "30d")) { session.settings.clipboardRetention = "30d"; session.Save(); session.PruneClips(); }
        ImGui::SameLine();
        if (Chip(shell, "keep-ever", "永久", session.settings.clipboardRetention == "forever")) { session.settings.clipboardRetention = "forever"; session.Save(); session.PruneClips(); }
        BeginRow("剪贴板记录", "暂停后不再写入，也可以马上清空");
        if (SoftButton(shell, "pause-clip", session.settings.clipboardPaused ? "恢复记录" : "暂停记录")) {
            session.settings.clipboardPaused = !session.settings.clipboardPaused;
            session.Save();
        }
        ImGui::SameLine();
        if (SoftButton(shell, "clear-clip", "清空")) {
            session.clips.clear();
            session.Save();
        }
        BeginRow("语言", "界面文案为简体中文");
        ImGui::Text("简体中文");
        BeginRow("主程序更新", "关于页的检查更新会读取这个开关");
        if (Toggle(shell, "update", &session.settings.autoUpdate)) session.Save();
        EndCard();
    } else if (shell.settingsTab == "hotkeys") {
        BeginCard(shell, "keys", "快捷键 · 录制后自动保存");
        for (const auto& [id, title, detail] : {std::tuple{"summon", "主呼出键", "全局呼出或隐藏搜索浮层"}, {"panel", "快捷启动", "全局唤出或隐藏快捷启动面板"}, {"tools", "常用工具面板", "打开 3×3 工具宫格"}, {"clipboard", "剪贴板历史", "直达剪贴板记录"}, {"settings", "设置页", "打开设置"}}) {
            BeginRow(title, detail);
            Keycap(shell, HotkeyText(shell, id));
            ImGui::SameLine();
            if (SoftButton(shell, (std::string("rec-") + id).c_str(), shell.recordingHotkey == id ? "请按键…" : "录制")) shell.recordingHotkey = id;
            ImGui::SameLine();
            if (SoftButton(shell, (std::string("reset-") + id).c_str(), "恢复默认")) {
                session.settings.hotkeys.erase(id);
                if (std::string(id) == "summon") {
                    session.settings.summonKeyCode = 49;
                    session.settings.summonAlt = true;
                    session.settings.summonCommand = false;
                    session.settings.summonControl = false;
                    session.settings.summonShift = false;
                }
                if ((std::string(id) == "summon" || std::string(id) == "panel") && shell.onHotkeyChanged) shell.onHotkeyChanged();
                session.Save();
            }
        }
        EndCard();
        if (!shell.recordingHotkey.empty()) {
            ImGui::SetCursorPosX(Px(16));
            ImGui::TextDisabled("按下带 Ctrl、Alt、Shift 或 Win 的组合。Esc 取消。");
            if (UserPressedEscape(shell)) shell.recordingHotkey.clear();
            const ImGuiIO& io = ImGui::GetIO();
            if (io.KeyCtrl || io.KeyAlt || io.KeyShift || io.KeySuper) {
                for (int key = ImGuiKey_NamedKey_BEGIN; key < ImGuiKey_NamedKey_END; ++key) {
                    const auto imguiKey = static_cast<ImGuiKey>(key);
                    if (!ImGui::IsKeyPressed(imguiKey)) continue;
                    const int vk = ImGuiKeyToVk(imguiKey);
                    if (!vk || imguiKey == ImGuiKey_LeftCtrl || imguiKey == ImGuiKey_RightCtrl || imguiKey == ImGuiKey_LeftAlt || imguiKey == ImGuiKey_RightAlt || imguiKey == ImGuiKey_LeftShift || imguiKey == ImGuiKey_RightShift || imguiKey == ImGuiKey_LeftSuper || imguiKey == ImGuiKey_RightSuper) continue;
                    HotkeyBinding binding;
                    binding.keyCode = vk;
                    binding.alt = io.KeyAlt;
                    binding.control = io.KeyCtrl;
                    binding.shift = io.KeyShift;
                    binding.command = io.KeySuper;
                    std::string text;
                    if (binding.control) text += session.store.KeyLabel("windows", "ctrl") + "+";
                    if (binding.alt) text += session.store.KeyLabel("windows", "alt") + "+";
                    if (binding.shift) text += session.store.KeyLabel("windows", "shift") + "+";
                    if (binding.command) text += session.store.KeyLabel("windows", "command") + "+";
                    text += VkName(vk);
                    binding.text = text;
                    bool clash = false;
                    for (const auto& [other, existing] : session.settings.hotkeys) {
                        if (other != shell.recordingHotkey && existing.text == text) clash = true;
                    }
                    if (clash) {
                        LogWarn("快捷键冲突，没有保存");
                        break;
                    }
                    const std::string recordId = shell.recordingHotkey;
                    session.settings.hotkeys[recordId] = binding;
                    if (recordId == "summon") {
                        session.settings.summonKeyCode = vk;
                        session.settings.summonAlt = binding.alt;
                        session.settings.summonControl = binding.control;
                        session.settings.summonShift = binding.shift;
                        session.settings.summonCommand = binding.command;
                    }
                    if ((recordId == "summon" || recordId == "panel") && shell.onHotkeyChanged) shell.onHotkeyChanged();
                    session.Save();
                    shell.recordingHotkey.clear();
                    break;
                }
            }
        }
        BeginCard(shell, "triggers", "触发词");
        for (const TriggerDef& item : session.store.Triggers()) {
            if (item.toolId == "ocr" || item.word.empty()) continue;
            std::string word = session.settings.triggerWords.contains(item.toolId) ? session.settings.triggerWords[item.toolId] : item.word;
            BeginRow(item.label.c_str(), ("输入「" + word + " + 空格」进入" + item.label + "。回车保存").c_str());
            ImGui::SetNextItemWidth(Px(120));
            if (ImGui::InputText(("##" + item.toolId).c_str(), &word, ImGuiInputTextFlags_EnterReturnsTrue)) {
                bool clash = false;
                for (const auto& [other, existing] : session.settings.triggerWords) {
                    if (other != item.toolId && existing == word) clash = true;
                }
                if (!clash && !word.empty()) {
                    session.settings.triggerWords[item.toolId] = word;
                    session.Save();
                } else LogWarn("触发词已被占用，没有保存");
            }
        }
        EndCard();
    } else {
        BeginCard(shell, "version", "");
        BeginRow("DeskKit 0.9.3", "Windows 版是 C++ / ImGui 便携包，解压即可运行。");
        ImGui::Text("当前版本");
        EndCard();
        std::string diffs;
        for (const StagedDiff& item : session.store.StagedDiffs()) {
            if (item.owner != "windows" && item.owner != "both") continue;
            diffs += item.id + " · " + item.dueAt + " · " + item.parityPlan + "\n";
        }
        BeginCard(shell, "parity", "双端功能对齐");
        BeginRow("阶段性差异", diffs.c_str());
        ImGui::Text("来自契约");
        EndCard();
        BeginCard(shell, "notes", "更新日志");
        BeginRow("0.9.3", "Windows 程序改为 C++ / ImGui。便携包带上拼音表和契约，解压即可运行。");
        ImGui::Text("当前版本");
        EndCard();
        BeginCard(shell, "permissions", "Windows 系统能力");
        for (const PermissionDef& item : session.store.Permissions("windows")) {
            const char* state = "可用";
            const char* color = "success";
            if (item.id == "autostart") {
                state = session.settings.autoLaunch ? "已开启" : "未开启";
                color = session.settings.autoLaunch ? "success" : "warning";
            }
            if (item.id == "fileIndex") state = session.index.size() > 8 ? "已索引" : "索引中";
            BeginRow(item.title.c_str(), item.reason.c_str());
            ImGui::TextColored(Paint(shell, color), "%s", state);
        }
        EndCard();
        BeginCard(shell, "check", "数据 / 检查");
        BeginRow("数据目录", "打开、导出或清空都作用在本机数据上");
        if (SoftButton(shell, "open-about", "打开")) OpenTarget(DataRoot());
        ImGui::SameLine();
        if (SoftButton(shell, "export-about", "导出")) ExportData(shell);
        BeginRow("检查更新", session.settings.autoUpdate ? "已开启启动检查。现在可以手动查一次" : "自动检查已关闭，仍可手动查看版本");
        if (SoftButton(shell, "check-update", "检查更新")) MessageBoxW(nullptr, session.settings.autoUpdate ? L"当前已是 DeskKit 0.9.0，没有新版本。" : L"自动检查已关闭。当前版本仍是 DeskKit 0.9.0。", L"检查更新", MB_OK);
        BeginRow("重新走引导", "三步引导可以再看一遍");
        if (SoftButton(shell, "reguide", "重新引导")) {
            session.settings.guideDone = false;
            session.Save();
            shell.ShowGuide();
        }
        BeginRow("实测内存", "记录当前占用，不作为发布门槛");
        ImGui::Text("%.0f MB", WorkingSetMb());
        EndCard();
    }
    ImGui::EndChild();
    ImGui::SetCursorPosX(Px(16));
    ImGui::TextDisabled("%s", session.store.Text("settings.footer").c_str());
    if (UserPressedEscape(shell) && shell.recordingHotkey.empty()) shell.showSettings = false;
}

void DrawGuide(Shell& shell) {
    Session& session = *shell.session;
    const char* titles[] = {"一个键，办完一件事", "设置呼出键", "权限与预置工具"};
    const std::string chord = session.store.FormatChord("windows", "summon");
    const std::string bodies[] = {
        "按一下呼出，找应用、找文件、翻译、计算、取色都在同一个框里。没有插件市场，内置工具开箱即用。",
        "默认呼出键是 " + chord + "。如果这个组合被别的软件占用，可以到设置里改成别的键。",
        "Windows 这边需要开机自启和文件索引。未准备好时会写明状态，不会假装已经可用。预置工具包默认全选。",
    };
    ImGui::BeginChild("guide-head", ImVec2(0, Px(48)), false);
    ImGui::SetCursorPos(ImVec2(Px(24), Px(14)));
    ImGui::Text("DeskKit");
    const float dots = Px(3 * 18);
    ImGui::SetCursorPos(ImVec2((ImGui::GetWindowWidth() - dots) * 0.5f, Px(19)));
    for (int step = 1; step <= 3; ++step) {
        ImGui::GetWindowDrawList()->AddCircleFilled(ImGui::GetCursorScreenPos() + ImVec2(Px(5), Px(5)), Px(5), PaintU(shell, step == shell.guideStep ? "accent" : "border.strong"));
        ImGui::Dummy(ImVec2(Px(18), Px(10)));
        ImGui::SameLine();
    }
    ImGui::EndChild();
    ImGui::BeginChild("guide-body", ImVec2(0, -Px(56)), false);
    ImGui::SetCursorPos(ImVec2(Px(48), Px(24)));
    ImGui::PushFont(g_inputFont ? g_inputFont : ImGui::GetFont());
    ImGui::Text("%s", titles[std::clamp(shell.guideStep, 1, 3) - 1]);
    ImGui::PopFont();
    ImGui::SetCursorPosX(Px(48));
    ImGui::PushTextWrapPos(ImGui::GetWindowWidth() - Px(48));
    ImGui::TextWrapped("%s", bodies[std::clamp(shell.guideStep, 1, 3) - 1].c_str());
    ImGui::PopTextWrapPos();
    ImGui::SetCursorPosX(Px(48));
    if (shell.guideStep == 1) {
        BeginCard(shell, "guide-use", "你会用到");
        BeginRow("主启动器", "搜索应用 / 文件 / 网址 / 内置工具");
        ImGui::NewLine();
        BeginRow("快捷启动面板", "分组图标网格");
        ImGui::NewLine();
        BeginRow("常用工具", "剪贴板、翻译、计算器、取色");
        EndCard();
    } else if (shell.guideStep == 2) {
        ImGui::PushFont(g_inputFont ? g_inputFont : ImGui::GetFont());
        ImGui::TextColored(Paint(shell, "accent"), "%s", chord.c_str());
        ImGui::PopFont();
        ImGui::TextDisabled("%s", shell.hotkeyReady ? "呼出键：已注册" : "呼出键：还没注册成功，可以改一个组合键");
        if (SoftButton(shell, "guide-hotkey", "打开快捷键设置", true)) shell.ShowSettings("hotkeys");
    } else {
        for (const PermissionDef& item : session.store.Permissions("windows")) {
            const char* state = item.id == "autostart" ? (session.settings.autoLaunch ? "已开启" : "可选") : session.index.size() > 8 ? "已索引" : "即将开始";
            BeginCard(shell, item.id.c_str(), "");
            BeginRow(item.title.c_str(), item.reason.c_str());
            ImGui::Text("%s", state);
            EndCard();
        }
    }
    ImGui::EndChild();
    ImGui::BeginChild("guide-foot", ImVec2(0, Px(56)), false);
    ImGui::SetCursorPos(ImVec2(Px(16), Px(12)));
    if (SoftButton(shell, "skip", "跳过")) {
        session.settings.guideDone = true;
        session.Save();
        shell.showGuide = false;
        session.Summon();
    }
    ImGui::SameLine(ImGui::GetWindowWidth() - Px(220));
    if (shell.guideStep > 1 && SoftButton(shell, "prev", "上一步")) --shell.guideStep;
    ImGui::SameLine();
    if (SoftButton(shell, "next", shell.guideStep == 3 ? "开始使用" : "下一步", true)) {
        if (shell.guideStep < 3) ++shell.guideStep;
        else {
            session.settings.guideDone = true;
            session.Save();
            shell.showGuide = false;
            session.Summon();
        }
    }
    ImGui::EndChild();
}

void DrawPicker(Shell& shell) {
    POINT point;
    GetCursorPos(&point);
    const std::string hex = ScreenPixelHex(point.x, point.y);
    const ImVec2 window = ImGui::GetWindowPos();
    const ImVec2 origin(static_cast<float>(point.x) + 16.f, static_cast<float>(point.y) + 16.f);
    ImGui::Dummy(ImVec2(1, 1));
    ImDrawList* draw = ImGui::GetWindowDrawList();
    const ImVec2 pad(Px(8), Px(4));
    const std::string label = hex.empty() ? "—" : hex;
    const ImVec2 text = ImGui::CalcTextSize(label.c_str());
    draw->AddRectFilled(origin, origin + text + pad * 2.f, IM_COL32(0, 0, 0, 220), Px(4));
    if (const auto rgb = ParseColor(hex)) draw->AddRectFilled(origin + ImVec2(Px(6), Px(6)), origin + ImVec2(Px(22), text.y + Px(2)), IM_COL32(rgb->r, rgb->g, rgb->b, 255), Px(2));
    draw->AddText(origin + ImVec2(hex.empty() ? Px(8) : Px(28), Px(4)), IM_COL32(255, 255, 255, 255), label.c_str());
    (void)window;
    (void)shell;
}

}  // namespace dk
