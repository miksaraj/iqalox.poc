cmake_minimum_required(VERSION 3.20)
project(iqaloxvm CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_executable(iqaloxvm
    src/main.cpp
    src/bytecode.cpp
    src/value.cpp
    src/vm.cpp
)

target_include_directories(iqaloxvm PRIVATE src)

if(NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Debug)
endif()

target_compile_options(iqaloxvm PRIVATE -Wall -Wextra)

enable_testing()
add_subdirectory(tests)
