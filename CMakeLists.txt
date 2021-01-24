cmake_minimum_required(VERSION 3.15)
set(ENV{MSBUILD_VERBOSITY} diag)
project(CppSharp VERSION 0.10.6 LANGUAGES CXX C CSharp)

set(IsWindows true)
set(IsLinux false)
set(IsMacOSX false)
set_property(GLOBAL PROPERTY Configuration Debug)
set (repos ${CMAKE_HOME_DIRECTORY}/..)

set (bootstrap "x86_64-pc-win32-msvc")
#set(LLVM_DIR "${repos}/llvm-project/llvm/build/cmake/modules/CMakeFiles")
#set(LLVM_DIR "${repos}/llvm-project/llvm/cmake/modules")
#set(LLVM_DIR "${repos}/llvm-project/llvm/build/lib/cmake/llvm")
#find_package(LLVM REQUIRED COMPONENTS clang)

include(CSharpUtilities)
include(CMakeParseArguments)

set(Clang_DIR_Release "${repos}/llvm-project11/llvm/build5/lib/cmake/clang")
set(Clang_DIR "${repos}/llvm-project11/llvm/build4/lib/cmake/clang")

find_package(Clang REQUIRED)
find_package(LLVM REQUIRED)

#set(Configuration Debug)
find_program(NUGET nuget)

message("Nuget: ${nuget}")
message("LLVM: ${LLVM_BUILD_MAIN_SRC_DIR}")

#include(${LLVM_DIR}/AddLLVM.Cmake)

list(APPEND clang_msvc_flags "/wd4146" "/wd4244" "/wd4800" "/wd4345"
    "/wd4355" "/wd4996" "/wd4624" "/wd4291"
    "/wd4251"
    "/wd4141"
    "/MP" "/wd4267")

#set(Configuration Debug)
add_subdirectory(src)

if(NOT "${CMAKE_BUILD_TYPE}" STREQUAL "")
    string(REPLACE "$(Configuration)" "${CMAKE_BUILD_TYPE}" intrinsic_headers ${LLVM_LIBRARY_DIR}/clang/${LLVM_PACKAGE_VERSION})
    FILE(COPY "${intrinsic_headers}" DESTINATION ${CMAKE_CURRENT_BINARY_DIR}/clang)
endif()

set(CMAKE_INSTALL_PREFIX ${repos}/SimpleDrawing/Common/bin/Debug)
install(DIRECTORY ${CMAKE_CURRENT_BINARY_DIR}/clang/${LLVM_PACKAGE_VERSION} DESTINATION lib/clang)
