add_subdirectory(CppParser)

FILE(WRITE ${CMAKE_CURRENT_BINARY_DIR}/restore.targets "
<Project>
    <PropertyGroup>
    </PropertyGroup>
    <Target Name=\"ApplyRestore\" BeforeTargets=\"BeforeBuild\" AfterTargets=\"Restore\">
        <Message Text=\"Triggering restore\" />
        <CallTarget Targets=\"EnableIntermediateOutputPathMismatchWarning\" />
        <CallTarget Targets=\"Restore\" />
    </Target>
</Project>")

string(JOIN "\;" restore_targets
    "$ENV{VSINSTALLDIR}/Common7/IDE/CommonExtensions/Microsoft/NuGet/NuGet.targets"
    ${CMAKE_CURRENT_BINARY_DIR}/restore.targets)
message(${restore_targets})

macro(add_csharp_library name)
    cmake_parse_arguments(CSHARP_ADD "" "DIRECTORY" "DEPENDS" ${ARGN})
    add_library(${name} SHARED)
    FILE(GLOB_RECURSE sources ABSOLUTE ${CMAKE_CURRENT_SOURCE_DIR}/${CSHARP_ADD_DIRECTORY} ${CMAKE_CURRENT_SOURCE_DIR}/${CSHARP_ADD_DIRECTORY}/*.cs)
    target_sources(${name} PRIVATE ${sources})
    set_target_properties(${name} PROPERTIES
        #VS_GLOBAL_BaseOutputPath $(MSBuildProjectDirectory)/out/$(MSBuildProjectName)/bin
        #VS_GLOBAL_BaseIntermediateOutputPath $(MSBuildProjectDirectory)/out/$(MSBuildProjectName)/obj
        #VS_GLOBAL_TargetFrameworks ".NETFramework,Version=v4.8/win-x64"
        VS_DOTNET_TARGET_FRAMEWORK_VERSION "v4.8"
        VS_PROJECT_IMPORT ${restore_targets})
    if (CSHARP_ADD_DEPENDS)
        add_dependencies(${name} ${CSHARP_ADD_DEPENDS})
    endif()
    install(TARGETS ${name} RUNTIME DESTINATION .)
endmacro(add_csharp_library)

add_library(CppSharp.Parser.CLI SHARED)
set(parser_cli_directory ${CMAKE_CURRENT_SOURCE_DIR}/CppParser/Bindings/CLI)
FILE(GLOB sources ABSOLUTE ${parser_cli_directory} ${parser_cli_directory}/*.cpp)
FILE(GLOB headers ABSOLUTE ${parser_cli_directory} ${parser_cli_directory}/*.h)
#add_dependencies(CppSharp.Parser.CLI CppSharp.CppParser)

target_link_libraries(CppSharp.Parser.CLI CppSharp.CppParser)
target_sources(CppSharp.Parser.CLI PRIVATE ${sources})
#target_sources(CppSharp.Parser.CLI INTERFACE ${headers})
target_include_directories(CppSharp.Parser.CLI SYSTEM PRIVATE ${CMAKE_HOME_DIRECTORY}/src/CppParser)
target_include_directories(CppSharp.Parser.CLI PRIVATE ${CMAKE_HOME_DIRECTORY}/include)
#target_include_directories(CppSharp.Parser.CLI PRIVATE ${parser_cli_directory})
set_target_properties(CppSharp.Parser.CLI PROPERTIES COMMON_LANGUAGE_RUNTIME "") # CMAKE_DEFAULT_BUILD_TYPE Release
target_compile_options(CppSharp.Parser.CLI PRIVATE ${clang_msvc_flags})
install(TARGETS CppSharp.Parser.CLI RUNTIME DESTINATION .)

add_csharp_library(CppSharp.AST DIRECTORY AST)
set_target_properties(CppSharp.AST PROPERTIES VS_DOTNET_REFERENCES "System")
set_property(TARGET CppSharp.AST
    PROPERTY VS_PACKAGE_REFERENCES "Microsoft.Win32.Registry"
)

add_csharp_library(CppSharp DIRECTORY Core)
set_target_properties(CppSharp PROPERTIES VS_DOTNET_REFERENCES "System")
set_target_properties(CppSharp PROPERTIES VS_PACKAGE_REFERENCES "Microsoft.Win32.Registry_;Microsoft.VisualStudio.Setup.Configuration.Interop_")

add_csharp_library(CppSharp.Runtime DIRECTORY Runtime)
set_target_properties(CppSharp.Runtime PROPERTIES VS_DOTNET_REFERENCES "mscorlib;System;System.ValueTuple")

add_csharp_library(CppSharp.Parser.Bootstrap DIRECTORY CppParser/Bootstrap DEPENDS CppSharp CppSharp.AST CppSharp.Runtime CppSharp.Parser CppSharp.Parser.CLI CppSharp.Generator)
set_target_properties(CppSharp.Parser.Bootstrap PROPERTIES VS_DOTNET_REFERENCES "mscorlib;System")

#windows && !CI
add_csharp_library(CppSharp.Parser DIRECTORY Parser DEPENDS CppSharp CppSharp.AST CppSharp.Runtime CppSharp.Parser.CLI)
#set_target_properties(CppSharp.Parser PROPERTIES VS_CONFIGURATION_TYPE RELEASE)
#!windows || CI
#add_csharp_library(CppSharp.Parser DIRECTORY Parser DEPENDS CppSharp CppSharp.AST CppSharp.Runtime CppSharp.Parser.CSharp)
set_target_properties(CppSharp.Parser PROPERTIES VS_DOTNET_REFERENCES "mscorlib;System")

add_subdirectory(Generator)

add_csharp_library(CppSharp.CLI DIRECTORY CLI DEPENDS CppSharp CppSharp.Parser.CLI CppSharp.Parser CppSharp.AST CppSharp.Generator)
set_target_properties(CppSharp.CLI PROPERTIES VS_DOTNET_REFERENCES "mscorlib;System")

add_csharp_library(CppSharp.Parser.CSharp DIRECTORY CppParser/Bindings/CSharp/${bootstrap} DEPENDS CppSharp.Runtime CppSharp.CppParser)

add_csharp_library(CppSharp.Parser.Gen DIRECTORY CppParser/ParserGen DEPENDS CppSharp CppSharp.AST CppSharp.Runtime CppSharp.Parser CppSharp.Parser.CLI CppSharp.Generator)
set_target_properties(CppSharp.Parser.Gen PROPERTIES VS_DOTNET_REFERENCES "mscorlib;System")

set_target_properties(Std-symbols PROPERTIES COMMON_LANGUAGE_RUNTIME "")

set_target_properties(CppSharp.Parser PROPERTIES COMPILE_FLAGS "/unsafe")
set_target_properties(CppSharp.Parser.CSharp PROPERTIES COMPILE_FLAGS "/unsafe")
set_target_properties(CppSharp.Runtime PROPERTIES COMPILE_FLAGS "/unsafe") #  VS_DOTNET_TARGET_FRAMEWORK_VERSION netstandard2.0

return()


add_executable(CppSharp.ASTViewer)
set_target_properties(CppSharp.ASTViewer PROPERTIES CXX_STANDARD 17 VS_CONFIGURATION_TYPE ConsoleApp)
set(viewer_directory ${CMAKE_CURRENT_SOURCE_DIR}/ASTViewer)
FILE(GLOB sources ABSOLUTE ${viewer_directory} ${viewer_directory}/*.cpp)
FILE(GLOB headers ABSOLUTE ${viewer_directory} ${viewer_directory}/*.h)
#add_dependencies(CppSharp.Parser.CLI CppSharp.CppParser)

#set(qt_bin "C:/Program Files/FreeCAD 0.18/bin")
#find_package(Qt5 COMPONENTS core gui widgets)

add_library(Qt.Core SHARED IMPORTED)
add_library(Qt.Gui SHARED IMPORTED)
add_library(Qt.Widgets SHARED IMPORTED)
set_target_properties(Qt.Core Qt.Gui Qt.Widgets PROPERTIES IMPORTED_IMPLIB ${qt_bin})

target_link_libraries(CppSharp.ASTViewer CppSharp.CppParser CppSharp.AST Qt.Core Qt.Gui Qt.Widgets clangAST)
target_sources(CppSharp.ASTViewer PRIVATE ${sources})

target_include_directories(CppSharp.ASTViewer SYSTEM PRIVATE ${viewer_directory})
#target_include_directories(CppSharp.Parser.CLI PRIVATE ${parser_cli_directory})
#set_target_properties(CppSharp.ASTViewer PROPERTIES COMMON_LANGUAGE_RUNTIME "") # CMAKE_DEFAULT_BUILD_TYPE Release
target_compile_options(CppSharp.ASTViewer PRIVATE ${clang_msvc_flags} "/wd4141" "/wd4146" "/wd4996")
#install(TARGETS CppSharp.ASTViewer RUNTIME DESTINATION .)