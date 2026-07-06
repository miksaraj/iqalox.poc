#pragma once

#include <cstdint>
#include <filesystem>

#include "object.hpp"

namespace iqalox {
class Vm;
}

namespace iqalox::bytecode {

// Bytecode format v1 -- see `compiler/src/Bytecode.fs`'s module doc
// comment for the authoritative on-disk layout (this mirrors it exactly;
// `compiler/` writes, `vm/` reads). Supersedes format v0, Phase 1's
// minimal round-trip proof (a single opcode: push a string constant,
// print it, halt) now that `compiler/`'s codegen (Phase 5) actually
// produces real programs.
[[maybe_unused]] constexpr uint8_t kFormatVersion = 1;

// One-to-one with `compiler/src/Bytecode.fs`'s `opcodeOf` -- same names,
// same byte values.
enum class OpCode : uint8_t {
    Constant = 0x01,
    Nil = 0x02,
    True = 0x03,
    False = 0x04,
    Undef = 0x05,
    Pop = 0x06,
    PopN = 0x07,
    GetLocal = 0x08,
    SetLocal = 0x09,
    GetUpvalue = 0x0A,
    SetUpvalue = 0x0B,
    GetGlobal = 0x0C,
    SetGlobal = 0x0D,
    DefineGlobal = 0x0E,
    Add = 0x0F,
    Subtract = 0x10,
    Multiply = 0x11,
    Divide = 0x12,
    Modulo = 0x13,
    Power = 0x14,
    Negate = 0x15,
    Not = 0x16,
    Equal = 0x17,
    NotEqual = 0x18,
    Greater = 0x19,
    GreaterEqual = 0x1A,
    Less = 0x1B,
    LessEqual = 0x1C,
    Jump = 0x1D,
    JumpIfFalse = 0x1E,
    JumpIfNotNil = 0x1F,
    BuildVector = 0x20,
    Call = 0x21,
    Closure = 0x22,
    Return = 0x23,
    Class = 0x24,
    Method = 0x25,
    Inherit = 0x26,
    GetProperty = 0x27,
    SetProperty = 0x28,
    GetSuper = 0x29,
};

// Loads `path` as a format-v1 bytecode file and returns its top-level
// script function -- every object built while decoding (strings, nested
// function protos, ...) is allocated through `vm` so the GC tracks it
// from the moment it exists. Throws `std::runtime_error` on a malformed
// file (bad magic, unsupported version, truncated data, an unrecognized
// constant tag) -- there's no recovery story, matching how early this
// format still is.
ObjFunction* load(const std::filesystem::path& path, Vm& vm);

}  // namespace iqalox::bytecode
