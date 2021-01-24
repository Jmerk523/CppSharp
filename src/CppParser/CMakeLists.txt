
add_library(CppSharp.CppParser SHARED)
target_include_directories(CppSharp.CppParser PUBLIC ${LLVM_INCLUDE_DIRS})
target_include_directories(CppSharp.CppParser PUBLIC ${CLANG_INCLUDE_DIRS})
target_include_directories(CppSharp.CppParser PUBLIC ${LLVM_BUILD_MAIN_SRC_DIR}/../clang/lib)

list(APPEND llvm_exports ${LLVM_AVAILABLE_LIBS}  ${CLANG_EXPORTED_TARGETS} LLVMSupport LLVM-C LTO Remarks libclang)

foreach(llvm_export ${llvm_exports})
    get_target_property(imp_type ${llvm_export} TYPE)
    if (NOT imp_type STREQUAL "EXECUTABLE")
        list(APPEND llvm_imports ${llvm_export})
        get_target_property(imp_loc ${llvm_export} IMPORTED_LOCATION_RELEASE)
        if ("${imp_loc-NOTFOUND}" STREQUAL "")
            string(REPLACE build4 build5 imp_loc ${imp_loc})
            set_target_properties(${llvm_export} PROPERTIES IMPORTED_LOCATION_RELEASE ${imp_loc})
        else()
            #set_target_properties(${llvm_export} PROPERTIES IMPORTED_LOCATION_RELEASE ${repos}/llvm-project11/llvm/build5/Release/lib/${llvm_import}.lib)
        endif()
        get_target_property(imp_lib ${llvm_export} IMPORTED_IMPLIB_RELEASE)
        if ("${imp_lib-NOTFOUND}" STREQUAL "")
            string(REPLACE build4 build5 imp_lib ${imp_lib})
            set_target_properties(${llvm_export} PROPERTIES IMPORTED_IMPLIB_RELEASE ${imp_lib})
        endif()
    endif ()
endforeach()

#set_target_properties(${llvm_imports} PROPERTIES MAP_IMPORTED_CONFIG_DEBUG RELEASE)
target_link_libraries(CppSharp.CppParser PRIVATE ${llvm_imports} )

#set_target_properties(CppSharp.CppParser PROPERTIES VS_CONFIGURATION_TYPE RELEASE)
add_compile_definitions("DLL_EXPORT") # "_ITERATOR_DEBUG_LEVEL=0"

FILE(GLOB headers ${CMAKE_CURRENT_SOURCE_DIR} ${CMAKE_CURRENT_SOURCE_DIR}/*.h)
FILE(GLOB sources ${CMAKE_CURRENT_SOURCE_DIR} ${CMAKE_CURRENT_SOURCE_DIR}/*.cpp)

target_sources(CppSharp.CppParser PRIVATE ${sources})
#target_sources(CppSharp.CppParser PUBLIC ${headers})
target_include_directories(CppSharp.CppParser PUBLIC ${CMAKE_CURRENT_SOURCE_DIR})
install(TARGETS CppSharp.CppParser RUNTIME DESTINATION .)

target_link_options(CppSharp.CppParser PRIVATE "/ignore:4099")
include(SelectLibraryConfigurations)

add_library(Std-symbols SHARED)

# FILE(GLOB sources ${CMAKE_CURRENT_SOURCE_DIR}/Bindings/CSharp/${bootstrap} ${CMAKE_CURRENT_SOURCE_DIR}/Bindings/CSharp/${bootstrap}/*)
# target_sources(Std-symbols PRIVATE ${sources})
# target_include_directories(Std-symbols SYSTEM PRIVATE ${CMAKE_CURRENT_SOURCE_DIR})
target_sources(Std-symbols PRIVATE Bindings/CSharp/${bootstrap}/Std-symbols.cpp)

target_compile_options(CppSharp.CppParser PRIVATE ${clang_msvc_flags})
target_compile_options(Std-symbols PRIVATE ${clang_msvc_flags})
install(TARGETS Std-symbols RUNTIME DESTINATION .)
