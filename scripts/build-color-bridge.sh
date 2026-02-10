#!/bin/bash
# Build the OCIO+OIIO color bridge native library
#
# This script builds libcolor_bridge.so which wraps OpenColorIO and
# OpenImageIO for use via P/Invoke from C#.
#
# System dependencies (install first):
#   sudo apt install libopencolorio-dev libopenimageio-dev

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
NATIVE_DIR="$PROJECT_ROOT/native/color-bridge"
BUILD_DIR="$PROJECT_ROOT/.build/color-bridge"
OUTPUT_DIR="$PROJECT_ROOT/godot/.godot/mono/temp/bin/Debug"

echo "=== Building color-bridge (OCIO + OIIO) ==="
echo "Source directory: $NATIVE_DIR"
echo "Build directory:  $BUILD_DIR"
echo "Output directory: $OUTPUT_DIR"

# Check for required tools
for tool in cmake make g++; do
    if ! command -v $tool &> /dev/null; then
        echo "Error: $tool is required but not installed."
        echo "Install with: sudo apt install build-essential cmake"
        exit 1
    fi
done

# Check for system dependencies via pkg-config
missing_deps=()
if ! pkg-config --exists OpenColorIO 2>/dev/null; then
    missing_deps+=("libopencolorio-dev")
fi
if ! pkg-config --exists OpenImageIO 2>/dev/null; then
    missing_deps+=("libopenimageio-dev")
fi

if [ ${#missing_deps[@]} -gt 0 ]; then
    echo ""
    echo "Error: Missing system dependencies:"
    for dep in "${missing_deps[@]}"; do
        echo "  - $dep"
    done
    echo ""
    echo "Install with:"
    echo "  sudo apt install ${missing_deps[*]}"
    exit 1
fi

echo "OpenColorIO: $(pkg-config --modversion OpenColorIO)"
echo "OpenImageIO: $(pkg-config --modversion OpenImageIO)"

# Create build directory
mkdir -p "$BUILD_DIR"

# Configure with CMake
echo ""
echo "Configuring..."
cmake -S "$NATIVE_DIR" -B "$BUILD_DIR" \
    -DCMAKE_BUILD_TYPE=Release

# Build
echo "Building..."
cmake --build "$BUILD_DIR" --config Release -j$(nproc)

# Copy library to output directories
mkdir -p "$OUTPUT_DIR"
cp "$BUILD_DIR"/libcolor_bridge.so "$OUTPUT_DIR/" 2>/dev/null || true

LIB_DIR="$PROJECT_ROOT/lib"
mkdir -p "$LIB_DIR"
cp "$BUILD_DIR"/libcolor_bridge.so "$LIB_DIR/" 2>/dev/null || true

# Also copy the OCIO config alongside the library
cp "$NATIVE_DIR/configs/ue4_viewer.ocio" "$OUTPUT_DIR/" 2>/dev/null || true
cp "$NATIVE_DIR/configs/ue4_viewer.ocio" "$LIB_DIR/" 2>/dev/null || true

echo ""
echo "=== Build complete ==="
echo "Libraries and configs copied to:"
echo "  - $OUTPUT_DIR"
echo "  - $LIB_DIR"
echo ""
echo "Library files:"
ls -la "$LIB_DIR"/libcolor_bridge.so 2>/dev/null || echo "  (check $OUTPUT_DIR)"
ls -la "$LIB_DIR"/ue4_viewer.ocio 2>/dev/null || true
