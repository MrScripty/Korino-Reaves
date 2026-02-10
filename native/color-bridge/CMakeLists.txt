cmake_minimum_required(VERSION 3.16)
project(color_bridge LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Use pkg-config to find OCIO and OIIO (more reliable on Ubuntu than find_package
# since the OIIO cmake config references optional tool binaries)
find_package(PkgConfig REQUIRED)
pkg_check_modules(OCIO REQUIRED IMPORTED_TARGET OpenColorIO)
pkg_check_modules(OIIO REQUIRED IMPORTED_TARGET OpenImageIO)

# Build shared library
add_library(color_bridge SHARED src/color_bridge.cpp)

target_include_directories(color_bridge PUBLIC include)

target_link_libraries(color_bridge
    PRIVATE PkgConfig::OCIO
    PRIVATE PkgConfig::OIIO
)

# Only export functions marked with BRIDGE_API
set_target_properties(color_bridge PROPERTIES
    C_VISIBILITY_PRESET hidden
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON
    # Ensure the output is libcolor_bridge.so (no version suffix)
    VERSION ""
    SOVERSION ""
)
