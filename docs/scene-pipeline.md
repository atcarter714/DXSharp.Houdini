# Scene pipeline v0.5 — qualification slice

Development branch: `feature/scene-pipeline`. Baseline: DXSharp 0.4.0-preview.4, Houdini 22.0.514, Windows x64, .NET 10. All changes require a Windows build and interactive Houdini test. No changes to the private Arkaen-Houdini package.

## Build and start

```powershell
git switch feature/scene-pipeline
git pull
dotnet restore DXSharp.Houdini.slnx --force-evaluate
pwsh -File eng/build.ps1
dotnet run --project samples/HoudiniStudio -c Release --no-build
# optional WARP:
dotnet run --project samples/HoudiniStudio -c Release --no-build -- --warp
```

Review/commit updated lockfiles before using locked restore. Restart Houdini (or disconnect, reload the bridge script and reconnect) to get the new request handlers. Existing package installation points at the repo; no reinstall needed if that registration is intact.

## Native viewport

Uses the public DXSharp.Framework.WinUI 0.4.0-preview.4 adapter and frame-owned depth buffer. Mesh and texture upload commands are recorded on the same direct queue as the draws. Scene resources remain caller-owned and the host drains before disposal. UI navigation retains its existing orbit/pan/zoom behavior. Settings contains backend, debug shading, exposure, roughness and metallic. Shaded mode is the native path; select CPU reference for comparison. Wireframe, shaded+edges, matched Houdini projective camera, and point-only geometry deliberately use CPU fallback until GPU implementations are qualified. The live preview consumes the original approximate preview triangulation; the export path is stricter.

The PBR preview uses a single directional light, GGX specular and approximate ambient term, linear shading with SDR output. It is NOT a Karma match. No shadow maps, IBL, OCIO configuration, transmission, normal textures or MSAA yet. Native viewport is orthographic with local auto-framing, not a full camera import. CPU fallback does not sample textures.

## SOP → runtime mesh

1. In Houdini, append a Divide SOP configured to triangulate (or provide a closed polygon triangle mesh).
2. Select that SOP in Studio. Click **Compile DXAM**.
3. Save a .dxam file. Its companion .json contains the source path and local-to-world transform.
4. In Project, choose **Load DXAM preview**, select the same blob and return to Scene workspace. This turns live updates off; reselect a SOP and reenable live updates to resume.

The compiler consumes point, corner, primitive and detail-owned N, Cd and uv, with Houdini precedence. Missing normals are area-weighted smooth normals. Render vertices are deduplicated by complete position/normal/color/UV tuples, preserving discontinuous corners. Material ranges retain per-face material names and original index order. Other attributes, tangents, skinning, arbitrary custom streams and full object composition are not yet compiled. Nontriangular/nonpolygon input is rejected rather than fan-triangulated.

DXAM v1: little-endian 32-byte header (magic DXAM, version, stride, vertex count, index count, metadata bytes, reserved uint64), 48-byte interleaved vertices (P float3 at 0, N float3 at 12, Cd float4 at 24, UV float2 at 40), UInt32 indices, UTF-8 JSON draw ranges, and SHA-256 of preceding bytes. No process pointers, D3D resources, descriptors or shader bytecode. MeshBlob reads directly into typed managed arrays, validates lengths/indices/ranges and verifies the checksum. It is not zero-copy/memory-mapped or DirectStorage yet. Transforms and material descriptions are currently separate metadata, not embedded in DXAM. No arbitrary code execution from assets.

## Solaris → mesh / material

1. Create or open a Solaris stage containing a polygon mesh with material assignment. Use a triangulated, subdivisionScheme=none, rightHanded mesh for strict compilation.
2. In Project enter the actual LOP node path, e.g. /stage/OUT. Click **Inspect Solaris stage**. Save the JSON report if desired.
3. The report contains composed prim paths, world transforms, stage up-axis and meters-per-unit, mesh counts, resolved whole-prim material assignments, material subset paths, authored camera/light properties, discovered shader nodes and connections, and translated material metadata.
4. Enter the actual USD mesh prim path (e.g. /World/rock) and **Compile USD mesh**. Load the DXAM preview.
5. Select a material from the inspected stage and click **Preview selected material**. Return to Scene workspace, choose Shaded and D3D12.

The USD adapter uses Houdini's own pxr module and the composed stage; direct asset references use the resolved Sdf.AssetPath. Indexed primvars are flattened; USD interpolation maps to the four geometry domains. Material subsets map per face. The stage report preserves source metadata rather than silently converting coordinate conventions. The preview draws one selected mesh in local coordinates; whole-stage rendering and multi-material GPU draw ranges are future work. Instancing, references to externally located shader networks, graph evaluation, complex texture connections, UDIM, opdef:, OCIO and MaterialX shader generation are not qualified. The translator supports constants and direct image edges; reports unsupported edges/inputs. Single selected material is an explicit override of the entire preview, not per-face material rendering. A supported direct base-color image is uploaded and sampled; other maps are recorded but not sampled. Local file decode uses Windows imaging codecs and limits dimensions to 2048. Unresolved references must be baked or selected manually.

## Copernicus

Enter a Copernicus node path and output index, click **Export COP RGBA32F** and save an .rgba32f file. It contains native HOM buffer order, little-endian four-channel float32 pixels, no mipmaps, plus JSON containing width, height, row pitch, SHA-256 and pixel-scale metadata. Export is capped at 16M pixels. In the same Project panel **Load base-color texture** can load the raw file and sidecar (up to 2048 per dimension) or a conventional WIC-supported image. Set sRGB only for perceptually encoded color data; use Flip V to qualify orientation. Color space is unspecified for COP; do not treat height/normal/roughness data as sRGB. The preview can use the same raw pixels without an image encoding step, but only base-color rendering is implemented. The raw layer API does not claim zero-copy GPU interoperability.

## Hydra feasibility (opt-in)

Do NOT install/register a renderer yet. Houdini ships a specific OpenUSD build; its HDK docs explicitly warn that plugins built against another USD binary distribution are incompatible.

```powershell
# Local Houdini's Python USD version and actual registered renderers:
pwsh -File eng/probe-hydra.ps1
# Native SDK header probe (also builds existing native HDK targets):
pwsh -File eng/build.ps1 -HydraProbe
# NativeAOT C ABI clear/readback in ordinary x64 Python, independent of Houdini:
pwsh -File eng/probe-hydra.ps1 -Aov -Standalone
# NativeAOT C ABI clear/readback, executed in a short-lived hython process:
pwsh -File eng/probe-hydra.ps1 -Aov
# After WARP passes, hardware:
pwsh -File eng/probe-hydra.ps1 -Aov -Hardware
```

The C++ probe reports PXR_VERSION, HD_API_VERSION, class sizes and presence of sceneIndex.h. The sizes are diagnostics, not proof of binary ABI compatibility. The NativeAOT project uses the Windows system D3D12 runtime (no Agility selection in the Houdini process), creates a 61x47 offscreen RGBA8 render target, clears it green, reads back the padded footprint, and copies tightly packed RGBA into a caller-owned buffer. The Python host verifies every pixel. This tests native .NET hosting and AOV transport, not a Hydra render delegate or an interactive Houdini GUI. NativeAOT publication requires the Visual Studio Desktop development with C++ workload (default components, including a Windows SDK) and .NET AOT runtime packs. The script discovers MSVC using vswhere and imports the x64 developer environment; it stops on a failed publish or missing Python PASS marker. The standalone option requires ordinary x64 Python (optionally specify -Python with its executable path) and tests the D3D12 DLL independently of Houdini. It does not establish Houdini-hosted interoperability. Close any open Houdini sessions if licenses are occupied, and check the License Administrator for a valid server and applicable Indie/Engine entitlement before retrying hython. husk enumeration is separate from a successful license check or rendering test; the discovery command reports a license diagnostic as a failure even if husk exits zero.

Next: select traditional HdRenderDelegate vs Hydra scene-index interface using the actual header report. Build an exact-version C++ plugin that registers an isolated DXSharp renderer id, accepts settings and mesh dirty updates, implements render pass and HdRenderBuffer color/depth AOVs, and invokes shared renderer via versioned C ABI. Qualify husk before Solaris interactive rendering; add pause/stop ownership and COP access only after basic rendering is reliable. No .NET objects or OpenUSD C++ objects cross the C ABI. Start with CPU readback before investigating shared D3D12 surfaces and Hydra/Hgi interoperability.

References: [DXSharp WinUI hosting](https://github.com/atcarter714/DXSharp/blob/main/doc/WinUI-Hosting.md), [Houdini USD plugin ABI](https://www.sidefx.com/docs/hdk/_h_d_k__u_s_d.html), [Houdini Hydra integration](https://www.sidefx.com/docs/hdk/_h_d_k__u_s_d_hydra.html), [HOM image layer](https://www.sidefx.com/docs/houdini/hom/hou/ImageLayer.html), [USD material binding](https://openusd.org/release/api/class_usd_shade_material_binding_a_p_i.html).

## Acceptance gates

- Managed verification passes seam, domain precedence, malformed topology, binary round-trip/corruption, material translation and raw texture checks.
- Box, sphere, rock, Tommy: local shaded D3D12 renders, resize and close repeatedly, no device removal; CPU modes remain usable.
- UV seam test: compile, load offline, display UV checker and inspect exact texture orientation.
- Solaris simple Preview Surface and MaterialX test: translated constant/texture values, resolved paths and diagnostics match authored graph.
- COP checkerboard: export RGBA32F and sample on UV mesh, verify orientation and channel values.
- Hydra: collect ABI report; verify WARP then hardware AOV pixels in isolated hython, then proceed to native delegate.

This is not a Windows-tested release. Do not claim material equivalence, Hydra registration, GPU wireframe or whole-stage rendering from these source changes.
