#include <cstdlib>
#include <iostream>
#include <vector>

#include "bytecode.hpp"

namespace {

// Deliberately minimal: a stack of strings is enough for the Phase 1
// round-trip proof (docs/PLAN-0.1.md). The real tagged-union Value type,
// covering numbers/booleans/nil/vectors/instances, is Phase 6 scope.
int run(const iqalox::bytecode::Chunk& chunk) {
    using iqalox::bytecode::OpCode;

    std::vector<std::string> stack;
    size_t ip = 0;
    const auto& code = chunk.code;

    while (ip < code.size()) {
        auto op = static_cast<OpCode>(code[ip++]);
        switch (op) {
            case OpCode::ConstString: {
                if (ip + 4 > code.size()) {
                    std::cerr << "iqaloxvm: truncated CONST_STRING operand\n";
                    return 70;
                }
                uint32_t index = static_cast<uint32_t>(code[ip]) | (static_cast<uint32_t>(code[ip + 1]) << 8) |
                                  (static_cast<uint32_t>(code[ip + 2]) << 16) |
                                  (static_cast<uint32_t>(code[ip + 3]) << 24);
                ip += 4;
                if (index >= chunk.constants.size()) {
                    std::cerr << "iqaloxvm: constant index " << index << " out of range\n";
                    return 70;
                }
                stack.push_back(chunk.constants[index]);
                break;
            }
            case OpCode::Print: {
                if (stack.empty()) {
                    std::cerr << "iqaloxvm: PRINT with an empty stack\n";
                    return 70;
                }
                std::cout << stack.back() << "\n";
                stack.pop_back();
                break;
            }
            case OpCode::Halt:
                return 0;
            default:
                std::cerr << "iqaloxvm: unknown opcode 0x" << std::hex << static_cast<int>(op) << "\n";
                return 70;
        }
    }

    return 0;
}

}  // namespace

int main(int argc, char** argv) {
    if (argc != 2) {
        std::cerr << "Usage: iqaloxvm <bytecode file>\n";
        return 64;
    }

    try {
        auto chunk = iqalox::bytecode::load(argv[1]);
        return run(chunk);
    } catch (const std::exception& error) {
        std::cerr << error.what() << "\n";
        return 65;
    }
}
