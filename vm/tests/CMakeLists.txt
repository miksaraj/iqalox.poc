find_package(Catch2 3 REQUIRED)

add_executable(iqaloxvm_tests
    test_bytecode.cpp
    test_vm.cpp
    test_value.cpp
    ../src/bytecode.cpp
    ../src/value.cpp
    ../src/vm.cpp
    ../src/natives.cpp
)

target_include_directories(iqaloxvm_tests PRIVATE ../src)
target_link_libraries(iqaloxvm_tests PRIVATE Catch2::Catch2WithMain)

include(CTest)
include(Catch)
catch_discover_tests(iqaloxvm_tests)
