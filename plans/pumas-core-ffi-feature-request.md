# Feature Request: Cross-Language Bindings via UniFFI and Rustler

## Summary

Add optional FFI support to pumas-core enabling native bindings for Python, C#, Swift, Kotlin, Go, Ruby (via UniFFI) and Elixir/Erlang (via Rustler), while keeping the core codebase clean and the binding infrastructure isolated.

## Motivation

### Current State

pumas-core is a well-designed Rust library for AI model management. Currently, cross-language access is only possible through pumas-rpc, which:

- Was designed specifically for the Pumas-Library GUI
- Adds HTTP/JSON-RPC overhead for what could be direct function calls
- May not expose all functionality needed by external consumers
- Couples external projects to an API designed for a different purpose

### Use Cases

Several projects would benefit from native pumas-core bindings:

1. **Game engine integrations** (Godot/Unity via C#) - Managing AI models for game modding tools
2. **Python automation scripts** - Batch model management, CI/CD pipelines
3. **Elixir/Phoenix applications** - Web services for model distribution
4. **Mobile apps** (Swift/Kotlin) - iOS/Android model management
5. **Desktop applications** - Native performance without HTTP overhead

### Why Not Just Use pumas-rpc?

| Concern | Impact |
|---------|--------|
| Designed for GUI, not general consumption | API gaps, awkward patterns for non-GUI use |
| Breaking changes follow GUI needs | External projects may break unexpectedly |
| HTTP overhead | Latency for frequent operations |
| Extra process to manage | Deployment complexity |
| Not all functionality exposed | Limited to what the GUI needs |

## Proposed Solution

### Architecture: Feature-Gated Derives + Separate Binding Crates

```
pumas-library/
├── rust/crates/
│   ├── pumas-core/           # Core logic (minimal changes)
│   ├── pumas-uniffi/         # UniFFI bindings (new crate)
│   └── pumas-rustler/        # Rustler bindings (new crate)
```

### Changes to pumas-core (Minimal)

**Cargo.toml additions:**
```toml
[features]
default = []
uniffi = ["dep:uniffi"]

[dependencies]
uniffi = { version = "0.28", optional = true }
```

**Type annotations (one line per public struct):**
```rust
#[derive(Debug, Clone, Serialize, Deserialize)]
#[cfg_attr(feature = "uniffi", derive(uniffi::Record))]
pub struct ModelRecord {
    pub id: String,
    pub path: String,
    pub official_name: String,
    // ... existing fields unchanged
}
```

**When the `uniffi` feature is disabled:** Zero binding code compiled. The library works exactly as it does today.

### New Crate: pumas-uniffi

Handles all UniFFI scaffolding and build configuration:

```
pumas-uniffi/
├── Cargo.toml
├── src/
│   ├── lib.rs          # Re-exports and scaffolding
│   └── pumas.udl       # Interface definition
├── build.rs            # UniFFI build script
└── README.md
```

**Cargo.toml:**
```toml
[package]
name = "pumas-uniffi"
version = "0.1.0"

[lib]
crate-type = ["cdylib", "staticlib"]
name = "pumas_uniffi"

[dependencies]
pumas-core = { path = "../pumas-core", features = ["uniffi"] }
uniffi = { version = "0.28", features = ["tokio"] }

[build-dependencies]
uniffi = { version = "0.28", features = ["build"] }
```

**Generated bindings for:**
- Python (official UniFFI support)
- C# (via uniffi-bindgen-cs)
- Kotlin (official)
- Swift (official)
- Ruby (official)
- Go (via uniffi-bindgen-go)

### New Crate: pumas-rustler

Provides Elixir/Erlang NIFs (completely separate, no changes to pumas-core needed):

```
pumas-rustler/
├── Cargo.toml
├── src/lib.rs          # NIF implementations
└── README.md
```

**Cargo.toml:**
```toml
[package]
name = "pumas_rustler"
version = "0.1.0"

[lib]
crate-type = ["cdylib"]
name = "pumas_core_nif"

[dependencies]
pumas-core = { path = "../pumas-core" }  # No special features needed
rustler = "0.31"
tokio = { version = "1", features = ["rt-multi-thread"] }
```

## API Surface to Expose

### Core Operations

```rust
// Model Library
async fn list_models() -> Result<Vec<ModelRecord>>;
async fn get_model(model_id: &str) -> Result<Option<ModelRecord>>;
async fn search_models(query: &str, limit: usize, offset: usize) -> Result<SearchResult>;
async fn rebuild_model_index() -> Result<usize>;

// HuggingFace Integration
async fn search_hf_models(query: &str, kind: Option<&str>, limit: usize) -> Result<Vec<HuggingFaceModel>>;
async fn start_hf_download(request: &DownloadRequest) -> Result<String>;
async fn get_hf_download_progress(download_id: &str) -> Option<ModelDownloadProgress>;
async fn cancel_hf_download(download_id: &str) -> Result<bool>;

// Model Import
async fn import_model(spec: &ModelImportSpec) -> Result<ModelImportResult>;
async fn import_models_batch(specs: Vec<ModelImportSpec>) -> Vec<ModelImportResult>;

// Model Mapping
async fn preview_model_mapping(version_tag: &str, models_path: &Path) -> Result<MappingPreviewResponse>;
async fn apply_model_mapping(version_tag: &str, models_path: &Path) -> Result<MappingApplyResponse>;
async fn get_link_health(version_tag: Option<&str>) -> Result<LinkHealthResponse>;
```

### Types to Annotate

```rust
// Core types (add #[cfg_attr(feature = "uniffi", derive(uniffi::Record))])
ModelRecord
ModelMetadata
ModelFileInfo
ModelHashes
SearchResult

// HuggingFace types
HuggingFaceModel
DownloadRequest
DownloadOption
ModelDownloadProgress

// Import types
ModelImportSpec
ModelImportResult
SecurityTier

// Mapping types
MappingPreviewResponse
MappingApplyResponse
LinkHealthResponse
```

## Benefits

### For pumas-core

| Benefit | Description |
|---------|-------------|
| **Minimal intrusion** | Only `#[cfg_attr(...)]` lines on public types |
| **Zero overhead when disabled** | Feature flag means no compile-time or runtime cost |
| **Clean separation** | All binding plumbing lives in separate crates |
| **Wider adoption** | More projects can use pumas-core directly |

### For Consumers

| Language | Binding Source | Use Case |
|----------|---------------|----------|
| **Python** | UniFFI (official) | Scripting, automation, ML pipelines |
| **C#** | UniFFI (community) | Unity, Godot, .NET applications |
| **Swift** | UniFFI (official) | iOS/macOS applications |
| **Kotlin** | UniFFI (official) | Android applications |
| **Go** | UniFFI (community) | Server applications, CLI tools |
| **Ruby** | UniFFI (official) | Web applications, scripting |
| **Elixir** | Rustler | Phoenix applications, distributed systems |

### For the Project

- **Single source of truth** - Bindings generated from actual code, not manually maintained
- **Type safety** - Generated bindings match Rust types exactly
- **Async support** - Both UniFFI and Rustler handle async/await properly
- **Error propagation** - Rust `Result<T, E>` maps to native error handling

## Implementation Approach

### Phase 1: pumas-core Preparation

1. Add optional `uniffi` feature to Cargo.toml
2. Add `#[cfg_attr(feature = "uniffi", derive(uniffi::Record))]` to public structs
3. Add `#[cfg_attr(feature = "uniffi", derive(uniffi::Enum))]` to public enums
4. Ensure all public API methods could be exported (no complex lifetime issues)

**Estimated changes:** ~50-100 lines of `cfg_attr` annotations across model files.

### Phase 2: pumas-uniffi Crate

1. Create new crate with UniFFI dependencies
2. Write UDL file defining the public API surface
3. Implement scaffolding in lib.rs
4. Set up build.rs for binding generation
5. Test with Python bindings first (official, well-documented)
6. Document binding generation for other languages

### Phase 3: pumas-rustler Crate

1. Create new crate with Rustler dependencies
2. Implement NIF wrappers for core operations
3. Handle async runtime bridging (Tokio → BEAM scheduler)
4. Create corresponding Elixir module
5. Publish to Hex.pm

### Phase 4: Documentation & CI

1. Add binding generation to CI pipeline
2. Document how to generate/use bindings for each language
3. Publish pre-built bindings for common platforms
4. Create example projects for each language

## Alternatives Considered

### 1. Enhance pumas-rpc as the Official Bridge

**Pros:** Already exists, HTTP is universal
**Cons:** Designed for GUI, overhead, extra process, not all functionality exposed

### 2. Manual FFI (C-style exports)

**Pros:** Maximum control
**Cons:** Tedious, error-prone, no type safety, manual memory management

### 3. WASM Bindings

**Pros:** Universal runtime
**Cons:** No filesystem access (critical for model management), performance overhead

### 4. Status Quo (pumas-rpc only)

**Pros:** No work required
**Cons:** Limits pumas-core adoption, forces awkward integration patterns

## Questions for Discussion

1. **API Surface:** Should all public APIs be exposed, or a curated subset?
2. **Versioning:** Should binding crates version independently or track pumas-core?
3. **Pre-built Binaries:** Should CI publish pre-built native libraries?
4. **Hex.pm Publishing:** Is there interest in an official Elixir package?

## References

- [UniFFI User Guide](https://mozilla.github.io/uniffi-rs/)
- [UniFFI GitHub](https://github.com/mozilla/uniffi-rs)
- [uniffi-bindgen-cs (C#)](https://github.com/aspect-build/aspect-workflows-csharp/tree/main/uniffi-bindgen-cs)
- [Rustler Documentation](https://docs.rs/rustler/latest/rustler/)
- [Rustler GitHub](https://github.com/rusterlium/rustler)

---

*Submitted by: [Your Name]*
*Date: [Date]*
*Related Project: Korino-Reaves (UAsset Viewer with AI agent integration)*
