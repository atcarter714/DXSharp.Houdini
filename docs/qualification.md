# Qualification

Run managed CI first, then from a compatible Windows MSVC environment run:

    pwsh -File eng/build.ps1 -HoudiniRoot 'C:/Program Files/Side Effects Software/Houdini 22.0.514' -Native

Inspect artifacts/hapi-abi.json and the diagnostic output. The native probe measures structure layouts; no managed structure mirror is assumed qualified until offsets and sizes are compared. Confirm the HDK API identifier matches the local installation.

For plugin load testing, locate the staged DXSharp_Passthrough.dll, add its directory to HOUDINI_DSO_PATH using Houdini's default-path suffix (&), launch the matching Houdini build, and place DXSharp Passthrough downstream of a polygon box. Compare point and primitive counts and confirm a clean cook. Do not load the plugin into a different HDK version.

The build does not start an engine session or exercise licensing. Live HAPI operations need appropriately licensed local installations. Native CI should run only on trusted self-hosted Windows agents, with proprietary SDKs installed locally; never run arbitrary public PR code on those machines.

Lockfiles: commit the first successful restore output and enable -Locked once reviewed. Automated NuGet publishing is intentionally disabled until the public API, license and dependency packaging are reviewed.
