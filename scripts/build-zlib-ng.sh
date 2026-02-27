#!/bin/bash
# Build zlib-ng for CUE4Parse PAK extraction support
#
# This script downloads and builds zlib-ng, placing the library
# in the Godot project's output directory.

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
BUILD_DIR="$PROJECT_ROOT/.build/zlib-ng"
OUTPUT_DIR="$PROJECT_ROOT/godot/.godot/mono/temp/bin/Debug"

ZLIB_NG_VERSION="2.2.4"
ZLIB_NG_URL="https://github.com/zlib-ng/zlib-ng/archive/refs/tags/${ZLIB_NG_VERSION}.tar.gz"

echo "=== Building zlib-ng ${ZLIB_NG_VERSION} ==="
echo "Build directory: $BUILD_DIR"
echo "Output directory: $OUTPUT_DIR"

# Check for required tools
for tool in cmake make gcc; do
    if ! command -v $tool &> /dev/null; then
        echo "Error: $tool is required but not installed."
        echo "Install with: sudo apt install build-essential cmake"
        exit 1
    fi
done

# Create build directory
mkdir -p "$BUILD_DIR"
cd "$BUILD_DIR"

# Download if not already present
if [ ! -f "zlib-ng-${ZLIB_NG_VERSION}.tar.gz" ]; then
    echo "Downloading zlib-ng ${ZLIB_NG_VERSION}..."
    curl -L -o "zlib-ng-${ZLIB_NG_VERSION}.tar.gz" "$ZLIB_NG_URL"
fi

# Extract
if [ ! -d "zlib-ng-${ZLIB_NG_VERSION}" ]; then
    echo "Extracting..."
    tar -xzf "zlib-ng-${ZLIB_NG_VERSION}.tar.gz"
fi

cd "zlib-ng-${ZLIB_NG_VERSION}"

# Configure with CMake
# -DZLIB_COMPAT=OFF ensures we get zlib-ng API (not zlib-compat mode)
# -DWITH_GTEST=OFF disables tests
# -DBUILD_SHARED_LIBS=ON builds shared library
echo "Configuring..."
cmake -B build \
    -DCMAKE_BUILD_TYPE=Release \
    -DZLIB_COMPAT=OFF \
    -DWITH_GTEST=OFF \
    -DBUILD_SHARED_LIBS=ON

# Build
echo "Building..."
cmake --build build --config Release -j$(nproc)

# Copy library to output directory
mkdir -p "$OUTPUT_DIR"
cp build/libz-ng.so* "$OUTPUT_DIR/" 2>/dev/null || true

# Also copy to a lib directory for easier access
LIB_DIR="$PROJECT_ROOT/lib"
mkdir -p "$LIB_DIR"
cp build/libz-ng.so* "$LIB_DIR/" 2>/dev/null || true

# Copy to tracked native runtimes directory (committed to git)
RUNTIMES_DIR="$PROJECT_ROOT/godot/native/runtimes/linux-x64/native"
mkdir -p "$RUNTIMES_DIR"
cp "build/libz-ng.so.${ZLIB_NG_VERSION}" "$RUNTIMES_DIR/libz-ng.so.2"

echo ""
echo "=== Build complete ==="
echo "Libraries copied to:"
echo "  - $OUTPUT_DIR"
echo "  - $LIB_DIR"
echo "  - $RUNTIMES_DIR (git-tracked)"
echo ""
echo "Library files:"
ls -la "$OUTPUT_DIR"/libz-ng.so* 2>/dev/null || echo "  (check $LIB_DIR)"
ls -la "$RUNTIMES_DIR"/libz-ng.so* 2>/dev/null || true
