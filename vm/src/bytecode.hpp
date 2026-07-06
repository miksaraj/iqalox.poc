#pragma once

#include <cstdint>
#include <filesystem>
#include <string>
#include <vector>

namespace iqalox::bytecode {

// Bytecode format v0 -- deliberately minimal, just enough for the Phase 1
// compiler/vm round-trip proof (docs/PLAN-0.1.md, Phase 1). Not the real
// instruction set: no locals, jumps, calls, or classes yet (Phase 5).
//
// Layout (all multi-byte integers little-endian):
//
//   offset  size  field
//   0       4     magic: 'I' 'Q' 'B' 'C'
//   4       1     format version (0x00 for v0)
//   5       4     constant_count (u32)
//           for each constant:
//             1     tag (0x00 = UTF-8 string; only tag defined in v0)
//             4     length (u32)
//             N     UTF-8 bytes
//           4     code_length (u32) -- byte length of the instruction stream
//           N     instruction bytes
//
// Opcodes (v0):
//   0x01 CONST_STRING <u32 constant index>  push constants[index]
//   0x02 PRINT                              pop one value, print + newline
//   0xFF HALT                               stop execution
enum class OpCode : uint8_t {
    ConstString = 0x01,
    Print = 0x02,
    Halt = 0xFF,
};

struct Chunk {
    std::vector<std::string> constants;
    std::vector<uint8_t> code;
};

// Throws std::runtime_error on a malformed file (bad magic, unsupported
// version, truncated data) -- there's no recovery story yet, matching how
// early this format still is.
Chunk load(const std::filesystem::path& path);

}  // namespace iqalox::bytecode
