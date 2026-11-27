# Houdini Studio — experimental live editor

This first sample uses Houdini's interactive Python API (HOM), not a HAPI session or HDK plugin. It reads actual cooked SOP geometry and edits numeric parameters through an explicitly started local file bridge. It does not bypass Houdini license restrictions. The preview is a CPU depth-buffered bitmap preview, not a DXSharp renderer; GPU rendering and HAPI SessionSync are subsequent work.

## Package installation (recommended)

From the repository root:

```powershell
pwsh -File eng/install-houdini-package.ps1
```

Restart Houdini, add the DXSharp shelf using the shelf + menu, and click Connect. Launch HoudiniStudio from Visual Studio or with the command below. A single discovered bridge is automatically connected on startup; for multiple sessions, select the instance and click Connect. Discover refreshes the list. Disconnect removes the discovery manifest. Manual directory entry remains supported.

The package is separate from Arkaen-Houdini and does not require or modify it. To unregister it, run the installer with -Uninstall and restart Houdini. Use -PreferenceDirectory for nonstandard Houdini preferences.

## Manual fallback

Open a supported local Houdini installation with an existing scene or an empty scene. The original target was 22.0.514; installation selection is described in [qualification](qualification.md). In Windows > Python Shell, execute (adjust path if necessary):

```python
exec(compile(open(r"C:\Users\atcar\source\arkaen\DXSharp.Houdini\houdini\scripts\python\dxsharp_studio_bridge.py", encoding="utf-8").read(), "dxsharp_studio_bridge.py", "exec"))
```

Print the directory explicitly with `print(hou.session.dxsharp_studio_bridge.directory)` and copy it. From the repo root:

```powershell
dotnet run --project samples/HoudiniStudio -c Release
```

Paste the directory into the editor, click Connect, select a SOP or create a box/sphere/torus. Change numeric parameters with Apply. The preview is refreshed from the actual cooked node. Use left mouse drag to orbit, middle/right drag to pan, wheel to zoom and Frame to reset pan/zoom. Enable Live update to poll the selected SOP's cook count once per second; Recook forces a cook. Pause live update when editing complex scenes. Enter an absolute .bgeo or .bgeo.sc path and click Import BGEO to create a File SOP, or Export BGEO to save the selected SOP geometry to a new file. Existing output files are refused.

Stop the bridge explicitly in Houdini:

```python
hou.session.dxsharp_studio_bridge.stop()
```

Only start the bridge for trusted local processes under your account. Do not share its temporary directory. Requests are allowlisted but can edit your open scene; use a disposable scene or save a backup. No network listener is started. Operations run on Houdini's UI thread and complex cooks may block it. Requests time out at 60 seconds; timeouts do not guarantee a running cook or edit was cancelled. No automatic scene saving. Geometry preview limits: 100,000 points, 200,000 edges, up to 300,000 preview triangles; sphere primitives receive a preview-only wireframe; other non-polygon primitives are not tessellated. Full-fidelity materials, textures, transforms, and attributes are not implemented.

The project is included in the solution. The initial editor was validated interactively; the new package and discovery workflow require a fresh Windows/Houdini test. No private assets are included.

After updating bridge code, restart Houdini or stop and restart the shelf bridge. Re-run the package installer to materialize the branded 48px shelf icon. The editor uses a CPU bitmap rasterizer with wireframe, shaded and shaded-with-edges modes; no D3D12 swap chain yet.


## Studio v0.3

- Scene and Library tabs; load the operator catalog to enumerate actual installed SOP types. Search by label or type. Add operator connects the currently selected SOP to input zero when applicable. Multi-input operators require manual wiring in Houdini.
- SideFX starter HDA discovery checks local Houdini engine/toolkit directories. If the installed layout differs, use Install HDA file to choose a trusted library directly. Install only assets you trust: Houdini may run asset scripts. No SideFX HDA binaries are redistributed.
- Parameter tuples: numeric fields, vector components, RGB picker, menus, toggles, strings, and existing ramp keys/values. Ramp interpolation is retained, not editable yet. Locked/animated/hidden parameters are excluded.
- Attributes tab: metadata for all four domains and small value samples (vertex metadata only). No editing.
- Match view and Follow Houdini view read camera orientation only; projection, pivot, world transform, camera translation, pan and dolly are not synchronized. Dragging in Studio turns off following.
- Three preview modes, reduced raster resolution during mouse drag, quality setting and CPU render-time indicator. Shading uses flat normals and a simple headlight, with fan triangulation (convex polygons only for correct fill). Not a production GPU renderer.
- Preferences persisted under LocalAppData. File picker for existing BGEO and HDA files, typed absolute output paths for exports.
- Reinstall the package if needed and restart Houdini before testing. Shelf activation no longer opens a console for routine startup.


## Studio v0.4 — workspace, attributes and synchronization

- Scene workspace, Project and Settings tabs. Scene tree uses real OBJ/SOP containment, node types, input paths and child categories; locked asset internals are not expanded. Operator creation is still SOP-only. The full catalog has no 500-entry cap; hidden and deprecated types are opt-in. Mountain 2.0 is deprecated; use Attribute Noise for new work.
- Drag the divider to widen/narrow the inspector. The separate scrollbar and Ctrl+wheel over the viewport scroll the inspector; plain wheel continues to zoom. Shading radio controls sit above the viewport.
- Parameter updates use an icon per tuple, Sync edits for queued changes, Discard for the selected SOP and Auto-apply edits (debounced). Queued edits carry the scene path and node session ID; stale targets are rejected. No automatic edits unless the user enables Auto-apply. These are not atomic transactions across multiple tuples.
- Project reads the current HIP path, save status, active network and selection, plus a limited allowlist of Houdini environment variables. No blanket process environment dump. Project and connection information stays local.
- Match view / Follow Houdini view read the selected Scene Viewer camera. In /obj or the selected SOP's object context, the bridge provides model/camera/viewport matrices for projection. Otherwise orientation-only fallback is shown. Preview is letterboxed to the source viewport aspect; near-plane crossing triangles are skipped, not clipped. No camera writes to Houdini. Poll interval 400 ms, subject to cook and UI scheduling.
- Point Cd is interpolated in shaded preview; point N and uv are transported but not yet used by the renderer. CanonicalAttributes in the Geometry assembly defines semantic/format contracts and float tuple conversion, with tests. Vertex/primitive/detail streams, UV seams, texture sampling and GPU uploads remain separate milestones.
- Settings are stored in %LOCALAPPDATA%/DXSharp/HoudiniStudio/settings.json. Save settings explicitly or close normally. Shading, quality, inspector width, auto-apply, live updates and follow mode persist.
- Stop/disconnect removes the active bridge's own temporary directory; normal process exit also attempts cleanup. Old or crashed sessions can leave folders. Run eng/cleanup-houdini-bridge.ps1 to inspect. Use -RemoveStale -WhatIf before removing, and -IncludeUnregistered only after closing Houdini if cleaning old folders without manifests. The cleanup refuses directories with unknown contents or a live recorded PID.

References:
- [Houdini node type metadata](https://www.sidefx.com/docs/houdini/hom/hou/OpNodeType.html)
- [Houdini viewport transforms](https://www.sidefx.com/docs/houdini/hom/hou/GeometryViewport.html)
- [Attribute ownership and precedence](https://www.sidefx.com/docs/houdini/model/attributes.html)
- [WinUI TreeView](https://learn.microsoft.com/windows/apps/develop/ui/controls/tree-view)
- [WinUI TabView](https://learn.microsoft.com/windows/apps/develop/ui/controls/tab-view)
- [WinUI RadioButtons](https://learn.microsoft.com/windows/apps/develop/ui/controls/radio-button)
- [D3D12 vertex input layout](https://learn.microsoft.com/windows/win32/api/d3d12/ns-d3d12-d3d12_input_element_desc)

Windows/Houdini 22.0.514 qualification of v0.4 is pending. Restart Houdini after pulling the new bridge.
