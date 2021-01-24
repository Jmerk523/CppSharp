
add_csharp_library(CppSharp.Generator DIRECTORY . DEPENDS CppSharp CppSharp.AST CppSharp.Parser CppSharp.Parser.CLI Std-symbols)
set_target_properties(CppSharp.Generator PROPERTIES VS_DOTNET_REFERENCES "mscorlib;System;Microsoft.CSharp")
set(resources Passes/verbs.txt)
set_source_files_properties(${resources} PROPERTIES VS_TOOL_OVERRIDE "EmbeddedResource")
target_sources(CppSharp.Generator PRIVATE ${resources})