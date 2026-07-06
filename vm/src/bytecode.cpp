#include "bytecode.hpp"

#include <array>
#include <cstring>
#include <fstream>
#include <stdexcept>

namespace iqalox::bytecode {

namespace {

constexpr std::array<char, 4> kMagic{'I', 'Q', 'B', 'C'};
constexpr uint8_t kVersion = 0;
constexpr uint8_t kStringTag = 0x00;

class Reader {
public:
    explicit Reader(std::vector<uint8_t> data) : data_(std::move(data)) {}

    uint8_t read_u8() {
        require(1);
        return data_[pos_++];
    }

    uint32_t read_u32() {
        require(4);
        uint32_t value = static_cast<uint32_t>(data_[pos_]) |
                          (static_cast<uint32_t>(data_[pos_ + 1]) << 8) |
                          (static_cast<uint32_t>(data_[pos_ + 2]) << 16) |
                          (static_cast<uint32_t>(data_[pos_ + 3]) << 24);
        pos_ += 4;
        return value;
    }

    std::string read_bytes(uint32_t length) {
        require(length);
        std::string value(reinterpret_cast<const char*>(&data_[pos_]), length);
        pos_ += length;
        return value;
    }

    std::vector<uint8_t> read_remaining(uint32_t length) {
        require(length);
        std::vector<uint8_t> value(data_.begin() + static_cast<long>(pos_),
                                    data_.begin() + static_cast<long>(pos_ + length));
        pos_ += length;
        return value;
    }

private:
    void require(size_t count) const {
        if (pos_ + count > data_.size()) {
            throw std::runtime_error("iqalox bytecode: unexpected end of file");
        }
    }

    std::vector<uint8_t> data_;
    size_t pos_ = 0;
};

}  // namespace

Chunk load(const std::filesystem::path& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file) {
        throw std::runtime_error("iqalox bytecode: cannot open '" + path.string() + "'");
    }

    std::vector<uint8_t> bytes((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
    Reader reader(std::move(bytes));

    for (char expected : kMagic) {
        if (static_cast<char>(reader.read_u8()) != expected) {
            throw std::runtime_error("iqalox bytecode: bad magic number in '" + path.string() + "'");
        }
    }

    uint8_t version = reader.read_u8();
    if (version != kVersion) {
        throw std::runtime_error("iqalox bytecode: unsupported format version " + std::to_string(version));
    }

    Chunk chunk;

    uint32_t constant_count = reader.read_u32();
    chunk.constants.reserve(constant_count);
    for (uint32_t i = 0; i < constant_count; ++i) {
        uint8_t tag = reader.read_u8();
        if (tag != kStringTag) {
            throw std::runtime_error("iqalox bytecode: unknown constant tag " + std::to_string(tag));
        }
        uint32_t length = reader.read_u32();
        chunk.constants.push_back(reader.read_bytes(length));
    }

    uint32_t code_length = reader.read_u32();
    chunk.code = reader.read_remaining(code_length);

    return chunk;
}

}  // namespace iqalox::bytecode
