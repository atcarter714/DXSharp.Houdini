# Houdini Studio — experimental live editor

This first sample uses Houdini's interactive Python API (HOM), not a HAPI session or HDK plugin. It reads actual cooked SOP geometry and edits numeric parameters through an explicitly started local file bridge. It does not bypass Houdini license restrictions. The preview is a CPU-projected WinUI wireframe, not a DXSharp renderer; GPU rendering and HAPI SessionSync are subsequent work.

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

Copy the printed bridge directory. From the repo root:

```powershell
dotnet run --project samples/HoudiniStudio -c Release
```

Paste the directory into the editor, click Connect, select a SOP or create a box/sphere/torus. Change numeric parameters with Apply. The preview is refreshed from the actual cooked node. Use left mouse drag to orbit, middle/right drag to pan, wheel to zoom and Frame to reset pan/zoom. Enable Live update to poll the selected SOP's cook count once per second; Recook forces a cook. Pause live update when editing complex scenes. Enter an absolute .bgeo or .bgeo.sc path and click Import BGEO to create a File SOP, or Export BGEO to save the selected SOP geometry to a new file. Existing output files are refused.

Stop the bridge explicitly in Houdini:

```python
hou.session.dxsharp_studio_bridge.stop()
```

Only start the bridge for trusted local processes under your account. Do not share its temporary directory. Requests are allowlisted but can edit your open scene; use a disposable scene or save a backup. No network listener is started. Operations run on Houdini's UI thread and complex cooks may block it. Requests time out at 60 seconds; timeouts do not guarantee a running cook or edit was cancelled. No automatic scene saving. Geometry preview limits: 100,000 points, 200,000 edges, 20,000 displayed edges; sphere primitives receive a preview-only wireframe; other non-polygon primitives are not tessellated. Full-fidelity materials, textures, transforms, and attributes are not implemented.

The project is included in the solution. The initial editor was validated interactively; the new package and discovery workflow require a fresh Windows/Houdini test. No private assets are included.

After updating bridge code, restart Houdini or stop and restart the shelf bridge. Re-run the package installer to materialize the branded 48px shelf icon. The editor remains a CPU wireframe preview, with no D3D12 swap chain yet.
