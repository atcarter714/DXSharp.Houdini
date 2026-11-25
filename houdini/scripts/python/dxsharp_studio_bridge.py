"""Run explicitly inside Houdini's Python Shell. Local, opt-in file IPC."""
import json, os, pathlib, tempfile, uuid, math, hou

class StudioBridge:
    def __init__(self):
        self.directory = pathlib.Path(tempfile.mkdtemp(prefix="dxsharp-houdini-"))
        self.callback = self.poll
        self.running = False
        self.busy = False

    def start(self):
        if not self.running:
            hou.ui.addEventLoopCallback(self.callback)
            self.running = True
        print("DXSharp Studio bridge:", self.directory)

    def stop(self):
        if self.running:
            hou.ui.removeEventLoopCallback(self.callback)
            self.running = False

    def poll(self):
        if self.busy:
            return
        self.busy = True
        try:
            for path in self.directory.glob("*.request.json"):
                key = path.name.split(".")[0]
                if len(key) != 32:
                    continue
                try:
                    uuid.UUID(hex=key)
                except ValueError:
                    continue
                response = self.directory / (key + ".response.json")
                tmp = response.with_suffix(".tmp")
                try:
                    if path.stat().st_size > 65536:
                        raise ValueError("Request too large")
                    request = json.loads(path.read_text(encoding="utf-8"))
                    path.unlink()
                    result = {"ok": True, "data": self.dispatch(request)}
                    payload = json.dumps(result, allow_nan=False)
                except Exception as ex:
                    path.unlink(missing_ok=True)
                    payload = json.dumps({"ok": False, "error": str(ex)})
                tmp.write_text(payload, encoding="utf-8")
                os.replace(tmp, response)
                break
        finally:
            self.busy = False

    def dispatch(self, request):
        action = request.get("action")
        if action == "ping":
            return {"version": hou.applicationVersionString(), "scene": hou.hipFile.path()}
        if action == "nodes":
            return sorted(n.path() for n in hou.node("/obj").allSubChildren()
                          if isinstance(n, hou.SopNode))
        if action == "create":
            kind = request.get("kind")
            if kind not in ("box", "sphere", "torus"):
                raise ValueError("Unsupported operator")
            parent = hou.node("/obj").createNode("geo", "dxsharp_studio")
            try:
                for child in parent.children():
                    child.destroy()
                sop = parent.createNode(kind)
                sop.setDisplayFlag(True)
                sop.setRenderFlag(True)
                parent.layoutChildren()
                return sop.path()
            except Exception:
                parent.destroy()
                raise
        node = hou.node(request.get("path", ""))
        if not isinstance(node, hou.SopNode):
            raise ValueError("Select a valid SOP")
        if action == "parameters":
            return [{"name": p.name(), "label": p.parmTemplate().label(),
                     "value": float(p.eval()),
                     "integer": p.parmTemplate().type() == hou.parmTemplateType.Int}
                    for p in node.parms()
                    if p.parmTemplate().type() in (hou.parmTemplateType.Int, hou.parmTemplateType.Float)
                    and not (p.isLocked() or p.isDisabled() or p.isHidden() or p.keyframes())]
        if action == "set":
            p = node.parm(request.get("name", ""))
            if p is None or p.isLocked() or p.isDisabled() or p.isHidden() or p.keyframes():
                raise ValueError("Parameter is not editable")
            kind = p.parmTemplate().type()
            if kind not in (hou.parmTemplateType.Int, hou.parmTemplateType.Float):
                raise ValueError("Only numeric parameters supported")
            value = float(request["value"])
            if not math.isfinite(value):
                raise ValueError("Non-finite value")
            with hou.undos.group("DXSharp Studio parameter edit"):
                p.set(int(value) if kind == hou.parmTemplateType.Int else value)
            return True
        if action == "geometry":
            geo = node.geometry()
            if geo is None:
                raise ValueError("No cooked geometry")
            points = geo.points()
            if len(points) > 100000:
                raise ValueError("Preview limit: 100,000 points")
            positions = [list(p.position()) for p in points]
            indices = {p.number(): i for i, p in enumerate(points)}
            edges = set()
            polygons = 0
            for prim in geo.prims():
                if not isinstance(prim, hou.Polygon):
                    continue
                polygons += 1
                corners = [indices[v.point().number()] for v in prim.vertices()]
                for a, b in zip(corners, corners[1:] + (corners[:1] if prim.isClosed() else [])):
                    if a != b:
                        edges.add((min(a,b), max(a,b)))
                    if len(edges) > 200000:
                        raise ValueError("Preview limit: 200,000 edges")
            return {"positions": positions, "edges": sorted(edges),
                    "primitives": len(geo.prims()), "polygons": polygons}
        raise ValueError("Unknown action")

if hasattr(hou.session, "dxsharp_studio_bridge"):
    hou.session.dxsharp_studio_bridge.stop()
hou.session.dxsharp_studio_bridge = StudioBridge()
hou.session.dxsharp_studio_bridge.start()
