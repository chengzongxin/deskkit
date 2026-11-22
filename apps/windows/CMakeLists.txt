cmake_minimum_required(VERSION 3.20)
project(DeskKit LANGUAGES CXX RC)

# Windows 端按 Maye Nano 的方式：C++20 + Dear ImGui + DirectX 11。不嵌浏览器。
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
add_compile_definitions(_WIN32_WINNT=0x0A00 WINVER=0x0A00)

if(MSVC)
  add_compile_options(/utf-8 /W3 /EHsc)
else()
  add_compile_options(-finput-charset=UTF-8 -fexec-charset=UTF-8 -Wall -Wextra)
endif()

set(IMGUI_DIR ${CMAKE_SOURCE_DIR}/third_party/imgui)
set(IMGUI_SRC
  ${IMGUI_DIR}/imgui.cpp
  ${IMGUI_DIR}/imgui_draw.cpp
  ${IMGUI_DIR}/imgui_tables.cpp
  ${IMGUI_DIR}/imgui_widgets.cpp
  ${IMGUI_DIR}/backends/imgui_impl_win32.cpp
  ${IMGUI_DIR}/backends/imgui_impl_dx11.cpp
  ${IMGUI_DIR}/misc/cpp/imgui_stdlib.cpp
)

add_library(deskkit_core STATIC
  src/core.cpp
  src/platform.cpp
  src/session.cpp
)
target_include_directories(deskkit_core PUBLIC src ${CMAKE_SOURCE_DIR}/third_party)
target_compile_definitions(deskkit_core PUBLIC UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
target_link_libraries(deskkit_core PUBLIC bcrypt ole32 oleaut32 uuid shell32 advapi32 user32 gdi32)

add_executable(deskkit WIN32
  src/main.cpp
  src/ui.cpp
  src/chrome.cpp
  assets/deskkit.rc
  ${IMGUI_SRC}
)
set_source_files_properties(assets/deskkit.rc PROPERTIES
  INCLUDE_DIRECTORIES "${CMAKE_CURRENT_SOURCE_DIR}/assets")
target_include_directories(deskkit PRIVATE
  src
  ${CMAKE_SOURCE_DIR}/third_party
  ${IMGUI_DIR}
  ${IMGUI_DIR}/backends
  ${IMGUI_DIR}/misc/cpp
)
target_compile_definitions(deskkit PRIVATE UNICODE _UNICODE NOMINMAX WIN32_LEAN_AND_MEAN)
target_link_libraries(deskkit PRIVATE deskkit_core d3d11 dxgi d3dcompiler dwmapi dcomp windowscodecs)
if(MINGW)
  # -static 会把 libgcc、libstdc++、libwinpthread 打进 exe。
  # 只写 -static-libgcc 时，别的电脑没有 MinGW 的 libwinpthread-1.dll，启动就会失败。
  target_link_options(deskkit PRIVATE -static -mwindows)
endif()

add_executable(deskkit_tests src/tests.cpp)
target_link_libraries(deskkit_tests PRIVATE deskkit_core)
if(MINGW)
  target_link_options(deskkit_tests PRIVATE -static)
endif()

# 拼音表和契约不打进 exe，运行时从仓库或程序旁边找。
add_custom_command(TARGET deskkit POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_directory
          ${CMAKE_SOURCE_DIR}/assets $<TARGET_FILE_DIR:deskkit>/assets
)
