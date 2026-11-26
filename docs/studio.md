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

Open Houdini 22.0.514 with an existing scene or an empty scene. In Windows > Python Shell, execute (adjust path if necessary):

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
