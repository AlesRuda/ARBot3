cmake_minimum_required(VERSION 3.15)
project(NativeLib LANGUAGES CXX)

if(WIN32)
    enable_language(ASM_MASM)
    set(SRC_FILES native_funcs.cpp asm_win_x64.asm)
else()
    # Zde říkáme, že pro Linux aktivujeme standardní ASM (GAS)
    enable_language(ASM)
    set(SRC_FILES native_funcs.cpp asm_linux_arm64.S)
endif()

add_library(NativeLib SHARED ${SRC_FILES})

# Cílová složka v projektu, kam chceme binárky kopírovat (vytvoří složku 'bin' v kořeni projektu)
if(WIN32)
    # Pro Windows (klasické MSVC) platí lokální složka
    set(OUTPUT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/bin")
else()
    # Pro Linux/WSL nasměrujeme výstup přímo na namapovaný Windows disk
    # UPRAVTE tuto cestu, pokud se liší od Vaší skutečné cesty na disku C:
    set(OUTPUT_DIR "/mnt/c/work/projekty/ARBot3/Src/NativeFuncs/bin")
endif()

# VYNUCENÍ STEJNÉ CESTY PRO WINDOWS (smaže podadresáře Debug/Release)
if(WIN32)
    set_target_properties(NativeLib PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY_DEBUG "${OUTPUT_DIR}"
        RUNTIME_OUTPUT_DIRECTORY_RELEASE "${OUTPUT_DIR}"
    )
endif()

# Nastavení, kam má CMake knihovnu "nainstalovat"
install(TARGETS NativeLib DESTINATION "${OUTPUT_DIR}")

# Tento příkaz donutí Visual Studio spustit instalační krok hned po buildu
set_target_properties(NativeLib PROPERTIES VISUAL_STUDIO_USER_PROGRAM_ARGUMENTS "")