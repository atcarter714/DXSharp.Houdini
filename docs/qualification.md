# Qualification

## Installation selection

Discover installed versions without loading Houdini or requesting a license:

    pwsh -File eng/find-houdini.ps1
    dotnet run --project samples/Diagnostics -- --list

Discovery checks the standard Side Effects Software installation directory, Windows uninstall registration, and the DXSHARP_HOUDINI_ROOT/HFS hints. It checks the client DLL and reports installed Python, husk and HDK components. Version ordering is numeric, using file version metadata with directory-name fallback. SideFX PE versions such as 22.0.0.508 are normalized to application version 22.0.508.0 before sorting and selection; this does not change the native HAPI version query or ABI gate. Custom installation locations can be listed using `find-houdini.ps1 -SearchRoot <directory>` and selected using an explicit root.

Selection precedence: explicit root, explicit version selector (highest matching installation), DXSHARP_HOUDINI_ROOT, HFS, then highest suitable discovered version. The version selector accepts major.minor or major.minor.build, for example 22.0.508. An explicitly selected root must contain the required components; no silent fallback. Package registration defaults to the selected installation's major.minor preference folder under Documents, unless an explicit preference path or HOUDINI_USER_PREF_DIR is supplied. Both 22.0.508 and 22.0.514 normally use houdini22.0.

    dotnet run --project samples/Diagnostics -- --version 22.0.508
    pwsh -File eng/build.ps1 -Native -HoudiniVersion 22.0.508
    pwsh -File eng/probe-hydra.ps1 -HoudiniVersion 22.0.508

A successful version query is not ABI qualification. The exact original HAPI baseline remains in Compatibility.RequireBaseline, and Diagnostics supports --require-baseline for that strict check. Other versions may be investigated with the diagnostic probe without enabling native session operations.

## Native qualification

Run managed CI first, then from a compatible Windows MSVC environment run:

    pwsh -File eng/build.ps1 -Native

Each native build uses a version- and installation-path-specific directory under `artifacts/native/`. Inspect its hapi-abi.json (and hydra-abi.json when requested) and the diagnostic output. The native probe measures structure layouts; no managed structure mirror is assumed qualified until offsets and sizes are compared. Confirm the HDK API identifier matches the local installation.

For plugin load testing, locate the staged DXSharp_Passthrough.dll, add its directory to HOUDINI_DSO_PATH using Houdini's default-path suffix (&), launch the matching Houdini build, and place DXSharp Passthrough downstream of a polygon box. Compare point and primitive counts and confirm a clean cook. Do not load the plugin into a different HDK version.

The build does not start an engine session or exercise licensing. Live HAPI operations need appropriately licensed local installations. Native CI should run only on trusted self-hosted Windows agents, with proprietary SDKs installed locally; never run arbitrary public PR code on those machines.

Lockfiles: commit the first successful restore output and enable -Locked once reviewed. Automated NuGet publishing is intentionally disabled until the public API, license and dependency packaging are reviewed.
