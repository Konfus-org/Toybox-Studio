cmake_minimum_required(VERSION 3.28.3)
project(DefaultTemplate LANGUAGES C CXX)

# The Toybox Engine source directory; supplied by the editor (or by hand) when configuring.
if(NOT DEFINED TBX_ENGINE_DIR)
    message(FATAL_ERROR "TBX_ENGINE_DIR must point to the Toybox Engine source directory.")
endif()
get_filename_component(TBX_ENGINE_DIR "${TBX_ENGINE_DIR}" ABSOLUTE)
if(NOT EXISTS "${TBX_ENGINE_DIR}/CMakeLists.txt")
    message(FATAL_ERROR "TBX_ENGINE_DIR ('${TBX_ENGINE_DIR}') does not contain the engine's CMakeLists.txt.")
endif()

# Everything (engine, launcher, plugins, and this project's app) lands in one bin folder so the
# launcher can load the app module beside itself.
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin")

list(APPEND CMAKE_MODULE_PATH "${TBX_ENGINE_DIR}/tools/cmake")

# Mirror the global build configuration the engine's CMake presets would inject when building
# in-tree (platform defines, config defines, compiler options, resource path).
set(TBX_RESOURCES_PATH "${TBX_ENGINE_DIR}/resources")
include(preset_compile_definitions)
include(preset_msvc_options)

add_subdirectory("${TBX_ENGINE_DIR}" engine)

include(codegen)

add_library(DefaultTemplate SHARED)
target_compile_features(DefaultTemplate PRIVATE cxx_std_23)

file(GLOB_RECURSE PROJECT_SOURCES CONFIGURE_DEPENDS
    "${CMAKE_CURRENT_SOURCE_DIR}/Source/*.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/Source/*.cpp"
)
target_sources(DefaultTemplate PRIVATE ${PROJECT_SOURCES})
source_group(TREE "${CMAKE_CURRENT_SOURCE_DIR}/Source" PREFIX "Source" FILES ${PROJECT_SOURCES})

target_link_libraries(DefaultTemplate PRIVATE
    Tbx::Engine
)
target_include_directories(DefaultTemplate PRIVATE
    "${CMAKE_CURRENT_SOURCE_DIR}/Source"
)
target_precompile_headers(DefaultTemplate PRIVATE "${TBX_ENGINE_DIR}/engine/include/tbx/pch.h")

tbx_codegen_generate_app_registration(
    TARGET DefaultTemplate
    BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}"
    SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/Source"
)
