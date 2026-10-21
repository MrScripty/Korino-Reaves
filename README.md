# Korino-Reaves

An Unreal Engine asset workbench for inspecting files, editing properties, comparing versions, and extracting PAK archives. Keep the asset tree, property editor, previews, and diff tools in one desktop workspace.

Korino-Reaves combines a Godot/C# host, a Svelte interface, Rust-based CEF embedding, and UAssetAPI/CUE4Parse asset parsing. It is a development project; format compatibility depends on the Unreal version and the underlying parsers. Work on copies of important assets.

## Run from source

Use Bash on Linux or a compatible Bash environment on Windows. Install:

- Node.js 20+ with npm and Rust stable
- Godot 4 with .NET support, matched to the Godot.NET.Sdk version in [the app project](godot/UAssetViewer.csproj), currently 4.6.0
- The .NET 8 runtime and a [.NET SDK with `.slnx` support](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/dotnet-new-sln-slnx-default), version 9.0.200 or newer

Clone with the UAssetAPI submodule, then use the launcher:

```bash
git clone --recurse-submodules https://github.com/MrScripty/Korino-Reaves.git
cd Korino-Reaves
./launcher.sh --install
./launcher.sh --build
./launcher.sh --run
```

`--install` restores project dependencies. Install the toolchains above yourself first. `--build` compiles both Rust crates, builds the .NET solution, and bundles the frontend. `--run` starts the Svelte development server and Godot; it does not build missing artifacts.

If Godot is not on PATH:

```bash
./launcher.sh --set-godot /path/to/Godot
```

The launcher uses isolated local application state by default. See `./launcher.sh --help` for state controls and other actions.

## Develop and check

```bash
./launcher.sh --test
```

This checks frontend types and runs the .NET tests. Build first to check the native runtime crates too.

For a bundled-frontend run:

```bash
./launcher.sh --build-release
./launcher.sh --run-release
```

## Find your way around

- [Frontend](svelte-ui/README.md): Svelte components, view models, and the typed IPC bridge
- [Runtime](godot/scripts/README.md): application logic, asset operations, and message handlers
- [Tests](godot/tests/README.md): runtime, parsing, diff, and boundary checks
- [CEF extension](cef-gdext/src/README.md) and [helper](cef-helper-rs/src/README.md): embedded browser integration

Application data belongs to the C# backend. Keep the contracts in [Models](godot/scripts/Models/README.md) and [the UI bridge](svelte-ui/src/lib/bridge/README.md) in sync when changing messages.

- [Architecture and workflow reference](docs/reference/README-20261015.md): preserved rationale, invariants and producer/consumer contracts

## License

[MIT](LICENSE), with third-party licenses collected in [NOTICE.md](NOTICE.md).
