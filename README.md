# DXSharp.Houdini

Houdini authoring, procedural asset processing and GPU workload integration for DXSharp.

Initial compatibility target: Houdini **22.0.514**, HDK API **22000475**, Houdini Engine **9.0**, HAPI API **0**. DXSharp.Core **0.4.0-preview.4**, .NET 10, Windows x64.

## Getting started

```powershell
pwsh -File eng/build.ps1
pwsh -File eng/build.ps1 -HoudiniRoot 'C:/Program Files/Side Effects Software/Houdini 22.0.514' -Native
dotnet run --project samples/Diagnostics -- --root 'C:/Program Files/Side Effects Software/Houdini 22.0.514'
```

The managed build and deterministic verification run without a Houdini installation. Native compilation requires the matching local Houdini toolkit and supported MSVC toolset (check `hcustom --output_compiler_range` from Houdini Command Line Tools). The diagnostics application queries the native client version without starting a session or consuming a license; it does not claim to validate cooking or licensing.

The native build emits an ABI probe and a minimal pass-through SOP plugin into the build directory. It does not install into your user Houdini profile. See [native qualification](docs/qualification.md).

## Layout

- `src/DXSharp.Houdini.Interop`: native library discovery, loading and version interrogation.
- `src/DXSharp.Houdini.Engine`: version policy and managed runtime diagnostics.
- `src/DXSharp.Houdini.Geometry`: SDK-independent mesh topology representation.
- `src/DXSharp.Houdini`: integration package referencing DXSharp.Core and the above layers.
- `native`: HDK build and HAPI ABI probe.
- `samples/Diagnostics`: executable diagnostic tool.
- `tests/Verification`: deterministic checks.
- `eng`: build and compatibility metadata.

## Scope

This foundation does not yet implement session creation, HDA cooking, GPU upload, or a compute SOP. These are next vertical slices, gated on ABI and licensing validation. The native pass-through SOP is a build/load qualification operator only.

No SideFX binaries, private Arkaen code, proprietary scenes or generated SideFX binding files are included. Houdini Engine licensing for custom integrations must be verified before live asset execution. Baked asset workflows are independent of the live HAPI runtime.

[HDK documentation](https://www.sidefx.com/docs/hdk/) · [Houdini Engine documentation](https://www.sidefx.com/docs/hengine/) · [Architecture](docs/architecture.md)
