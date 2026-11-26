"""Run explicitly inside Houdini's Python Shell. Local, opt-in file IPC."""
import json, os, pathlib, tempfile, uuid, math, hou

class StudioBridge:
    def __init__(self):
        self.directory = pathlib.Path(tempfile.mkdtemp(prefix="dxsharp-houdini-"))
        self.callback = self.poll
        self.running = False
        self.busy = False
        self.manifest = self.directory / "session.json"

    def start(self):
        if not self.running:
            hou.ui.addEventLoopCallback(self.callback)
            self.running = True
        self.manifest.write_text(json.dumps({"protocol": 1, "pid": os.getpid(),
            "version": hou.applicationVersionString(), "scene": hou.hipFile.path()}), encoding="utf-8")
        hou.ui.setStatusMessage("DXSharp Studio bridge ready")

    def stop(self):
        if self.running:
            hou.ui.removeEventLoopCallback(self.callback)
            self.running = False
        self.manifest.unlink(missing_ok=True)

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
        if action == "operators":
            result = []
            for name, typ in hou.sopNodeTypeCategory().nodeTypes().items():
                definition = typ.definition()
                result.append({"name": name, "label": typ.description(), "icon": typ.icon(),
                    "inputs": typ.minNumInputs(), "maxInputs": typ.maxNumInputs(),
                    "library": definition.libraryFilePath() if definition else ""})
            return sorted(result, key=lambda x: x["label"].lower())
        if action == "tools":
            hfs = pathlib.Path(hou.expandString("$HFS"))
            roots = [hfs / "engine", hfs / "toolkit", hfs / "houdini" / "engine"]
            names = ("he_boolean", "he_calculate_occlusion", "he_curve_instancer",
                "he_delete_small_parts", "he_polyreduce", "he_set_pivot", "he_surface_instancer")
            found = {}
            for root in roots:
                if not root.is_dir():
                    continue
                for file in root.rglob("he_*.json"):
                    if file.stem not in names or file.stem in found or not file.with_suffix(".hda").is_file():
                        continue
                    meta = json.loads(file.read_text(encoding="utf-8-sig"))
                    found[file.stem] = {"name": meta.get("name", file.stem), "tip": meta.get("toolTip", ""),
                        "file": str(file.with_suffix(".hda")), "kind": meta.get("toolType", "")}
            return list(found.values())
        if action == "install_hda":
            source = pathlib.Path(request["file"])
            if not source.is_absolute() or not source.is_file() or source.suffix.lower() not in (".hda", ".hdalc", ".hdanc", ".otl"):
                raise ValueError("Select a trusted HDA library")
            definitions = hou.hda.definitionsInFile(str(source))
            if not definitions:
                raise ValueError("No asset definitions in library")
            hou.hda.installFile(str(source))
            return [d.nodeTypeName() for d in definitions]
        if action == "operator":
            kind = request["kind"]
            typ = hou.sopNodeTypeCategory().nodeTypes().get(kind)
            if typ is None:
                raise ValueError("Unknown SOP type")
            source = hou.node(request.get("path") or "/")
            if isinstance(source, hou.SopNode):
                parent = source.parent()
            else:
                parent = hou.node("/obj").createNode("geo", "dxsharp_studio")
            node = None
            try:
                with hou.undos.group("DXSharp create SOP"):
                    node = parent.createNode(kind, exact_type_name=True)
                    if isinstance(source, hou.SopNode) and typ.maxNumInputs() > 0:
                        node.setInput(0, source)
                    node.setDisplayFlag(True)
                    node.setRenderFlag(True)
                    node.moveToGoodPosition()
                return node.path()
            except Exception:
                if node is not None:
                    node.destroy()
                if not isinstance(source, hou.SopNode):
                    parent.destroy()
                raise
        if action == "view":
            viewer = hou.ui.paneTabOfType(hou.paneTabType.SceneViewer)
            if viewer is None:
                raise ValueError("No Houdini Scene Viewer")
            camera = viewer.curViewport().defaultCamera()
            return {"rotation": list(camera.rotation().asTuple()), "pivot": list(camera.pivot()),
                    "translation": list(camera.translation())}
        if action in ("create", "import"):
            if action == "import":
                source = pathlib.Path(request["file"])
                if not source.is_absolute() or not source.is_file() or not str(source).lower().endswith((".bgeo", ".bgeo.sc")):
                    raise ValueError("Select an existing absolute BGEO file path")

            kind = "file" if action == "import" else request.get("kind")
            if kind not in ("box", "sphere", "torus", "file"):
                raise ValueError("Unsupported operator")
            parent = hou.node("/obj").createNode("geo", "dxsharp_studio")
            try:
                for child in parent.children():
                    child.destroy()
                sop = parent.createNode(kind)
                if action == "import":
                    sop.parm("file").set(source.as_posix())
                if kind == "sphere":
                    parm = sop.parm("type")
                    if parm is not None:
                        for i, label in enumerate(parm.menuLabels()):
                            if label.lower() == "polygon":
                                parm.set(i)
                                break
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
        if action == "revision":
            node.geometry()
            return {"cook": node.cookCount(), "scene": hou.hipFile.path(), "id": node.sessionId()}
        if action == "recook":
            node.cook(force=True)
            return node.cookCount()
        if action == "export":
            target = pathlib.Path(request["file"])
            if not target.is_absolute() or not str(target).lower().endswith((".bgeo", ".bgeo.sc")):
                raise ValueError("Select an absolute BGEO output path")
            if target.exists():
                raise ValueError("Output already exists; choose a new file")
            geo = node.geometry()
            if geo is None:
                raise ValueError("No cooked geometry")
            geo.saveToFile(str(target))
            return str(target)
        if action == "attributes":
            geo = node.geometry()
            result = []
            for owner, attrs, elements in (
                ("Point", geo.pointAttribs(), geo.points()),
                ("Vertex", geo.vertexAttribs(), None),
                ("Primitive", geo.primAttribs(), geo.prims()),
                ("Detail", geo.globalAttribs(), (geo,))):
                for a in attrs:
                    values = []
                    if elements is not None:
                        for element in elements[:8]:
                            try:
                                values.append(str(element.attribValue(a)))
                            except Exception:
                                values.append("Unavailable")
                    result.append({"owner": owner, "name": a.name(), "type": str(a.dataType()),
                        "size": a.size(), "values": values})
            return result
        if action == "tuples":
            result = []
            for group in node.parmTuples():
                template = group.parmTemplate()
                kind = template.type()
                if any(p.isLocked() or p.isDisabled() or p.isHidden() or p.keyframes() for p in group):
                    continue
                if kind == hou.parmTemplateType.Ramp:
                    ramp = group[0].evalAsRamp()
                    result.append({"name": group.name(), "label": template.label(), "kind": "ramp",
                        "keys": ramp.keys(), "values": ramp.values(), "basis": [str(x).split(".")[-1] for x in ramp.basis()]})
                elif kind in (hou.parmTemplateType.Float, hou.parmTemplateType.Int, hou.parmTemplateType.Toggle,
                              hou.parmTemplateType.String, hou.parmTemplateType.Menu):
                    entry = {"name": group.name(), "label": template.label(), "kind": str(kind).split(".")[-1].lower(),
                        "values": group.eval(), "components": [p.name() for p in group]}
                    if kind == hou.parmTemplateType.Float and template.namingScheme() == hou.parmNamingScheme.RGBA:
                        entry["kind"] = "color"
                    if kind in (hou.parmTemplateType.Float, hou.parmTemplateType.Int):
                        entry.update({"min": template.minValue(), "max": template.maxValue()})
                    if kind in (hou.parmTemplateType.Menu, hou.parmTemplateType.String, hou.parmTemplateType.Int):
                        try:
                            entry["choices"] = list(group[0].menuItems())
                            entry["labels"] = list(group[0].menuLabels())
                        except hou.OperationFailed:
                            pass
                    result.append(entry)
            return result
        if action == "set_tuple":
            group = node.parmTuple(request["name"])
            if group is None or any(p.isLocked() or p.isDisabled() or p.isHidden() or p.keyframes() for p in group):
                raise ValueError("Parameter tuple is not editable")
            kind = group.parmTemplate().type()
            with hou.undos.group("DXSharp Studio parameter edit"):
                if kind == hou.parmTemplateType.Ramp:
                    keys = tuple(float(x) for x in request["keys"])
                    values = tuple(tuple(v) if isinstance(v, list) else float(v) for v in request["values"])
                    basis = tuple(getattr(hou.rampBasis, b) for b in request["basis"])
                    if len(keys) != len(values) or len(keys) != len(basis) or not keys or any(not 0 <= k <= 1 for k in keys) or any(a >= b for a,b in zip(keys,keys[1:])):
                        raise ValueError("Ramp keys must be strictly increasing in [0,1]")
                    group[0].set(hou.Ramp(basis, keys, values))
                elif kind in (hou.parmTemplateType.Float, hou.parmTemplateType.Int, hou.parmTemplateType.Toggle,
                              hou.parmTemplateType.String, hou.parmTemplateType.Menu):
                    values = request["values"]
                    if len(values) != len(group):
                        raise ValueError("Tuple size mismatch")
                    if kind != hou.parmTemplateType.String and any(not isinstance(v, (int,float)) or not math.isfinite(v) for v in values):
                        raise ValueError("Invalid numeric tuple")
                    group.set(tuple(values))
                else:
                    raise ValueError("Unsupported parameter type")
            return True
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
            packed = geo.pointFloatAttribValues('P')
            positions = [list(packed[i:i+3]) for i in range(0, len(packed), 3)]
            indices = {p.number(): i for i, p in enumerate(points)}
            edges = set()
            polygons = 0
            triangles = []
            spheres = 0
            unsupported = 0
            for prim in geo.prims():
                if not isinstance(prim, hou.Polygon):
                    if prim.type() == hou.primType.Sphere:
                        matrix = hou.Matrix3(prim.intrinsicValue("transform"))
                        origin = prim.vertices()[0].point().position()
                        base = len(positions)
                        rows, cols = 16, 32
                        for y in range(rows + 1):
                            phi = math.pi * y / rows
                            for x in range(cols):
                                theta = 2 * math.pi * x / cols
                                v = hou.Vector3(math.sin(phi)*math.cos(theta), math.cos(phi), math.sin(phi)*math.sin(theta))
                                positions.append(list(v * matrix + origin))
                                i = base + y * cols + x
                                edges.add((i, base + y * cols + (x+1) % cols))
                                if y:
                                    edges.add((i-cols, i))
                                    j = base + y * cols + (x+1) % cols
                                    triangles.extend((i-cols, j-cols, i, i, j-cols, j))
                        spheres += 1
                        if len(positions) > 100000 or len(edges) > 200000:
                            raise ValueError("Preview limit exceeded")
                    else:
                        unsupported += 1
                    continue
                polygons += 1
                corners = [indices[v.point().number()] for v in prim.vertices()]
                if prim.isClosed() and len(corners) >= 3:
                    for j in range(1, len(corners)-1):
                        triangles.extend((corners[0], corners[j], corners[j+1]))
                if len(triangles) > 900000:
                    raise ValueError("Preview triangle limit exceeded")
                for a, b in zip(corners, corners[1:] + (corners[:1] if prim.isClosed() else [])):
                    if a != b:
                        edges.add((min(a,b), max(a,b)))
                    if len(edges) > 200000:
                        raise ValueError("Preview limit: 200,000 edges")
            return {"positions": positions, "edges": sorted(edges),
                    "primitives": len(geo.prims()), "polygons": polygons,
                    "sourcePoints": len(points), "spheres": spheres, "unsupported": unsupported,
                    "triangles": triangles,
                    "revision": node.cookCount()}
        raise ValueError("Unknown action")

if hasattr(hou.session, "dxsharp_studio_bridge"):
    hou.session.dxsharp_studio_bridge.stop()
hou.session.dxsharp_studio_bridge = StudioBridge()
hou.session.dxsharp_studio_bridge.start()
