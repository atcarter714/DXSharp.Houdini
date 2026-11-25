# Houdini Studio — experimental live editor

This first sample uses Houdini's interactive Python API (HOM), not a HAPI session or HDK plugin. It reads actual cooked SOP geometry and edits numeric parameters through an explicitly started local file bridge. It does not bypass Houdini license restrictions. The preview is a CPU-projected WinUI wireframe, not a DXSharp renderer; GPU rendering and HAPI SessionSync are subsequent work.

## Run

Open Houdini 22.0.514 with an existing scene or an empty scene. In Windows > Python Shell, execute (adjust path if necessary):

```python
exec(compile(open(r"C:\Users\atcar\source\arkaen\DXSharp.Houdini\houdini\scripts\python\dxsharp_studio_bridge.py", encoding="utf-8").read(), "dxsharp_studio_bridge.py", "exec"))
```

Copy the printed bridge directory. From the repo root:

```powershell
dotnet run --project samples/HoudiniStudio -c Release
```

Paste the directory into the editor, click Connect, select a SOP or create a box/sphere/torus. Change numeric parameters with Apply. The preview is refreshed from the actual cooked node. Use yaw, pitch and zoom controls to inspect geometry.

Stop the bridge explicitly in Houdini:

```python
hou.session.dxsharp_studio_bridge.stop()
```

Only start the bridge for trusted local processes under your account. Do not share its temporary directory. Requests are allowlisted but can edit your open scene; use a disposable scene or save a backup. No network listener is started. Operations run on Houdini's UI thread and complex cooks may block it. Requests time out at 60 seconds; timeouts do not guarantee a running cook or edit was cancelled. No automatic scene saving. Geometry preview limits: 100,000 points, 200,000 edges, 20,000 displayed edges; non-polygon primitives are not tessellated. Full-fidelity materials, textures, transforms, and attributes are not implemented.

The project is intentionally separate from the qualified base solution until Windows App SDK build and runtime testing pass. No private assets are included.
