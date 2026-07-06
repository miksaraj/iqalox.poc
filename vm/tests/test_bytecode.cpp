#include <catch2/catch_test_macros.hpp>

#include <cstdint>
#include <filesystem>
#include <fstream>
#include <random>
#include <vector>

#include "bytecode.hpp"

namespace {

void push_u32(std::vector<uint8_t>& bytes, uint32_t value) {
    bytes.push_back(static_cast<uint8_t>(value & 0xFF));
    bytes.push_back(static_cast<uint8_t>((value >> 8) & 0xFF));
    bytes.push_back(static_cast<uint8_t>((value >> 16) & 0xFF));
    bytes.push_back(static_cast<uint8_t>((value >> 24) & 0xFF));
}

// Writes `bytes` to a fresh temp file and returns its path -- each test gets
// its own file (a random suffix) so parallel/interleaved test runs can't
// collide on the same path.
std::filesystem::path write_temp_file(const std::vector<uint8_t>& bytes) {
    static std::mt19937 rng(std::random_device{}());
    auto path = std::filesystem::temp_directory_path() /
                ("iqaloxvm_test_" + std::to_string(rng()) + ".iqbc");
    std::ofstream file(path, std::ios::binary);
    file.write(reinterpret_cast<const char*>(bytes.data()), static_cast<std::streamsize>(bytes.size()));
    file.close();
    return path;
}

std::vector<uint8_t> header_with_version(uint8_t version) {
    return {'I', 'Q', 'B', 'C', version};
}

}  // namespace

TEST_CASE("an empty chunk (no constants, no code) loads cleanly", "[bytecode]") {
    auto bytes = header_with_version(0);
    push_u32(bytes, 0);  // constant_count
    push_u32(bytes, 0);  // code_length

    auto path = write_temp_file(bytes);
    auto chunk = iqalox::bytecode::load(path);

    REQUIRE(chunk.constants.empty());
    REQUIRE(chunk.code.empty());

    std::filesystem::remove(path);
}

TEST_CASE("a chunk with one string constant and CONST_STRING/PRINT/HALT round-trips", "[bytecode]") {
    auto bytes = header_with_version(0);

    push_u32(bytes, 1);      // constant_count
    bytes.push_back(0x00);   // string tag
    push_u32(bytes, 5);      // length
    for (char c : std::string("hello")) bytes.push_back(static_cast<uint8_t>(c));

    std::vector<uint8_t> code;
    code.push_back(0x01);  // OP_CONST_STRING
    push_u32(code, 0);     // constant index 0
    code.push_back(0x02);  // OP_PRINT
    code.push_back(0xFF);  // OP_HALT

    push_u32(bytes, static_cast<uint32_t>(code.size()));
    bytes.insert(bytes.end(), code.begin(), code.end());

    auto path = write_temp_file(bytes);
    auto chunk = iqalox::bytecode::load(path);

    REQUIRE(chunk.constants == std::vector<std::string>{"hello"});
    REQUIRE(chunk.code == code);

    std::filesystem::remove(path);
}

TEST_CASE("a bad magic number is rejected", "[bytecode]") {
    auto bytes = std::vector<uint8_t>{'N', 'O', 'P', 'E', 0};
    push_u32(bytes, 0);
    push_u32(bytes, 0);

    auto path = write_temp_file(bytes);
    REQUIRE_THROWS_AS(iqalox::bytecode::load(path), std::runtime_error);

    std::filesystem::remove(path);
}

TEST_CASE("an unsupported format version is rejected", "[bytecode]") {
    auto bytes = header_with_version(99);
    push_u32(bytes, 0);
    push_u32(bytes, 0);

    auto path = write_temp_file(bytes);
    REQUIRE_THROWS_AS(iqalox::bytecode::load(path), std::runtime_error);

    std::filesystem::remove(path);
}

TEST_CASE("a truncated file is rejected rather than reading out of bounds", "[bytecode]") {
    auto bytes = header_with_version(0);
    push_u32(bytes, 1);  // claims one constant, but the file ends here

    auto path = write_temp_file(bytes);
    REQUIRE_THROWS_AS(iqalox::bytecode::load(path), std::runtime_error);

    std::filesystem::remove(path);
}
