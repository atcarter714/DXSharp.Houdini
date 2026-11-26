"""Run explicitly inside Houdini's Python Shell. Local, opt-in file IPC."""
import json, os, pathlib, tempfile, uuid, math, shutil, atexit, hou

# Scene extraction is read-only. The strict compile path never fans concave
# polygons or silently drops unsupported primitives.
def _compile_sop(node):
    if not isinstance(node, hou.SopNode):
        raise ValueError("Select a SOP")
    geo = node.geometry()
    points = geo.points()
    prims = geo.prims()
    if len(points) > 100000 or len(prims) > 300000:
        raise ValueError("Interactive compiler limit exceeded")
    indices = {p.number(): i for i,p in enumerate(points)}
    corners = []
    materials = []
    material = geo.findPrimAttrib("shop_materialpath")
    owner = node.parent()
    while owner is not None and not isinstance(owner, hou.ObjNode):
        owner = owner.parent()
    default_mat = owner.parm("shop_materialpath") if owner else None
    default_mat = default_mat.evalAsString() if default_mat else ""
    for prim in prims:
        if not isinstance(prim, hou.Polygon) or not prim.isClosed() or len(prim.vertices()) != 3:
            raise ValueError("Compile requires closed polygon triangles; append a Divide/Triangulate SOP.")
        corners.extend(indices[v.point().number()] for v in prim.vertices())
        materials.append((prim.stringAttribValue(material) or default_mat) if material else default_mat)
    attrs = []
    for domain, find, values in (
        ("Point",geo.findPointAttrib,geo.pointFloatAttribValues),
        ("Vertex",geo.findVertexAttrib,geo.vertexFloatAttribValues),
        ("Primitive",geo.findPrimAttrib,geo.primFloatAttribValues),
        ("Detail",geo.findGlobalAttrib,None)):
        for name in ("N","Cd","uv"):
            a = find(name)
            if a is None or a.dataType() != hou.attribData.Float:
                continue
            if domain == "Detail":
                value = geo.attribValue(a)
                data = list(value) if isinstance(value,(tuple,list)) else [value]
            else:
                data = list(values(name))
            attrs.append(dict(name=name,domain=domain,components=a.size(),values=data))
    return dict(path=node.path(),positions=[list(p.position()) for p in points],corners=corners,
        faces=[3]*len(prims),attributes=attrs,materials=materials,
        localToWorld=list(owner.worldTransform().asTuple()) if owner else None)

def _usd_stage(path):
    node = hou.node(path)
    if not isinstance(node, hou.LopNode):
        raise ValueError("Enter a cooked Solaris LOP path, e.g. /stage/OUT")
    return node.stage()

def _usd_value(value):
    from pxr import Sdf
    if value is None:
        return None
    if isinstance(value,Sdf.AssetPath):
        return dict(identifier=value.path,resolvedPath=value.resolvedPath or None)
    if isinstance(value,(str,bool,int,float)):
        return value
    try:
        return [_usd_value(v) for v in value]
    except TypeError:
        return str(value)

def _usd_material(material,time):
    from pxr import Usd, UsdShade
    graph = []
    for prim in Usd.PrimRange(material.GetPrim()):
        shader = UsdShade.Shader(prim)
        if not shader:
            continue
        graph.append(dict(path=str(prim.GetPath()),shaderId=str(shader.GetIdAttr().Get() or ""),
            inputs=[dict(name=str(i.GetBaseName()),value=_usd_value(i.Get(time)),
                connections=[str(c) for c in i.GetAttr().GetConnections()])
                for i in shader.GetInputs()]))
    # Prefer MaterialX for Karma, then universal preview. Retain complete
    # connection paths for all directly discovered nodes in the report.
    surface = None
    for context in ("mtlx",""):
        result = material.ComputeSurfaceSource(context)
        if result and result[0]:
            surface = result[0]
            break
    inputs = {}
    if surface:
        for i in surface.GetInputs():
            name=str(i.GetBaseName())
            v=_usd_value(i.Get(time))
            entry={}
            if isinstance(v,(int,float)) and not isinstance(v,bool):
                entry["constant"]=[float(v)]
            elif isinstance(v,list) and all(isinstance(x,(int,float)) for x in v):
                entry["constant"]=v
            sources=i.GetAttr().GetConnections()
            if len(sources)==1:
                source=surface.GetPrim().GetStage().GetPrimAtPath(sources[0].GetPrimPath())
                tex=UsdShade.Shader(source)
                sid=str(tex.GetIdAttr().Get() or "") if tex else ""
                if sid=="UsdUVTexture" or sid.startswith("ND_image_"):
                    f=tex.GetInput("file")
                    asset=_usd_value(f.Get(time)) if f else None
                    if isinstance(asset,dict):
                        cs=tex.GetInput("sourceColorSpace")
                        asset["colorSpace"]=str(cs.Get(time)) if cs and cs.Get(time) is not None else None
                        entry["texture"]=asset
                        entry["channel"]=str(sources[0].name).split(":")[-1]
                        entry["unsupportedConnection"]="Sampler/UV transform, image scale and bias not translated yet"
                    else:
                        entry["unsupportedConnection"]=str(sources[0])
                else:
                    entry["unsupportedConnection"]=str(sources[0])
            elif sources:
                entry["unsupportedConnection"]=", ".join(str(x) for x in sources)
            inputs[name]=entry
    return dict(path=str(material.GetPath()),shaderId=str(surface.GetIdAttr().Get() or "") if surface else "",
        inputs=inputs,graph=graph)

def _inspect_stage(path):
    from pxr import Usd, UsdGeom, UsdShade, UsdLux
    stage=_usd_stage(path)
    time=Usd.TimeCode(hou.frame())
    cache=UsdGeom.XformCache(time)
    objects=[]
    materials={}
    for prim in stage.Traverse():
        if len(objects)>=10000:
            raise ValueError("Stage inspection limit: 10000 prims")
        path=str(prim.GetPath())
        item=dict(path=path,type=str(prim.GetTypeName()))
        if prim.IsA(UsdGeom.Xformable):
            m=cache.GetLocalToWorldTransform(prim)
            item["localToWorld"]=[float(m[i][j]) for i in range(4) for j in range(4)]
        if prim.IsA(UsdGeom.Mesh):
            mesh=UsdGeom.Mesh(prim)
            binding=UsdShade.MaterialBindingAPI(prim)
            mat,_=binding.ComputeBoundMaterial()
            item.update(pointCount=len(mesh.GetPointsAttr().Get(time) or []),
                faceCount=len(mesh.GetFaceVertexCountsAttr().Get(time) or []),
                material=str(mat.GetPath()) if mat else "",
                subsets=[str(x.GetPath()) for x in binding.GetMaterialBindSubsets()])
        if prim.IsA(UsdGeom.Camera) or prim.HasAPI(UsdLux.LightAPI):
            item["authoredProperties"]={str(a.GetName()):_usd_value(a.Get(time)) for a in prim.GetAuthoredAttributes()}
        if prim.IsA(UsdShade.Material):
            materials[path]=_usd_material(UsdShade.Material(prim),time)
        objects.append(item)
    return dict(source=stage.GetRootLayer().identifier,timeCode=time.GetValue(),
        upAxis=str(UsdGeom.GetStageUpAxis(stage)),metersPerUnit=UsdGeom.GetStageMetersPerUnit(stage),
        objects=objects,materials=list(materials.values()))

def _compile_usd_mesh(path,prim_path):
    from pxr import Usd, UsdGeom, UsdShade
    stage=_usd_stage(path)
    time=Usd.TimeCode(hou.frame())
    prim=stage.GetPrimAtPath(prim_path)
    mesh=UsdGeom.Mesh(prim)
    if not mesh:
        raise ValueError("Enter a USD Mesh prim path")
    points=mesh.GetPointsAttr().Get(time)
    faces=mesh.GetFaceVertexCountsAttr().Get(time)
    corners=mesh.GetFaceVertexIndicesAttr().Get(time)
    if points is None or faces is None or corners is None or any(n!=3 for n in faces):
        raise ValueError("Triangulated USD mesh required")
    if len(points)>100000 or len(faces)>300000:
        raise ValueError("Interactive compiler limit exceeded")
    if mesh.GetSubdivisionSchemeAttr().Get(time)!="none":
        raise ValueError("Bake subdivision surfaces to polygon meshes before compiling")
    if mesh.GetOrientationAttr().Get(time)=="leftHanded":
        raise ValueError("Convert leftHanded USD winding upstream for this prototype")
    domains={"constant":"Detail","uniform":"Primitive","vertex":"Point","varying":"Point","faceVarying":"Vertex"}
    attrs=[]
    pv=UsdGeom.PrimvarsAPI(prim)
    for name,source in (("Cd","displayColor"),("uv","st"),("N","normals")):
        a=pv.GetPrimvar(source)
        if a:
            values=a.ComputeFlattened(time)
            interpolation=str(a.GetInterpolation())
        elif name=="N":
            values=mesh.GetNormalsAttr().Get(time)
            interpolation=str(mesh.GetNormalsInterpolation())
        else:
            continue
        if values is not None and len(values):
            attrs.append(dict(name=name,domain=domains[interpolation],components=len(values[0]),
                values=[float(x) for row in values for x in row]))
    binding=UsdShade.MaterialBindingAPI(prim)
    mat,_=binding.ComputeBoundMaterial()
    materials=[str(mat.GetPath()) if mat else ""]*len(faces)
    for subset in binding.GetMaterialBindSubsets():
        submat,_=UsdShade.MaterialBindingAPI(subset.GetPrim()).ComputeBoundMaterial()
        for f in subset.GetIndicesAttr().Get(time) or []:
            if f<0 or f>=len(faces):
                raise ValueError("Invalid material subset face")
            materials[f]=str(submat.GetPath()) if submat else ""
    m=UsdGeom.XformCache(time).GetLocalToWorldTransform(prim)
    return dict(path=prim_path,positions=[list(x) for x in points],corners=list(corners),faces=list(faces),
        attributes=attrs,materials=materials,localToWorld=[float(m[i][j]) for i in range(4) for j in range(4)],
        upAxis=str(UsdGeom.GetStageUpAxis(stage)),metersPerUnit=UsdGeom.GetStageMetersPerUnit(stage))

def _export_cop(path,output,file):
    import hashlib
    node=hou.node(path)
    if not isinstance(node,hou.CopNode):
        raise ValueError("Enter a Copernicus node path")
    target=pathlib.Path(file)
    if not target.is_absolute() or target.suffix.lower()!=".rgba32f":
        raise ValueError("Choose an absolute .rgba32f file")
    with node.layer(output) as layer:
        w,h=layer.bufferResolution()
        if not (0<w<=8192 and 0<h<=8192 and w*h<=16777216):
            raise ValueError("COP export limit: 16M pixels")
        raw=bytes(layer.allBufferElements(hou.imageLayerStorageType.Float32,4))
        if len(raw)!=w*h*16:
            raise ValueError("Unexpected COP byte count")
        metadata=dict(source=path,output=output,width=w,height=h,format="R32G32B32A32_FLOAT",
            rowPitch=w*16,sha256=hashlib.sha256(raw).hexdigest(),colorSpace="unspecified",
            rowOrder="HOM native buffer order; verify orientation",pixelScale=list(layer.pixelScale()))
    temp=target.with_name(target.name+"."+uuid.uuid4().hex+".tmp")
    try:
        temp.write_bytes(raw)
        os.replace(temp,target)
    finally:
        temp.unlink(missing_ok=True)
    return metadata

class StudioBridge:
    def __init__(self):
        self.directory = pathlib.Path(tempfile.mkdtemp(prefix="dxsharp-houdini-"))
        self.callback = self.poll
        self.running = False
        self.busy = False
        self.manifest = self.directory / "session.json"
        atexit.register(self.cleanup)

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
        self.cleanup()

    def cleanup(self):
        # Only remove the unique directory owned by this bridge.
        shutil.rmtree(self.directory, ignore_errors=True)

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
        if action == "compile_sop":
            return _compile_sop(hou.node(request["path"]))
        if action == "inspect_stage":
            return _inspect_stage(request["path"])
        if action == "compile_usd_mesh":
            return _compile_usd_mesh(request["path"],request["prim"])
        if action == "export_cop":
            return _export_cop(request["path"],int(request.get("output",0)),request["file"])
        if action == "ping":
            return {"version": hou.applicationVersionString(), "scene": hou.hipFile.path()}
        if action == "project":
            editor = hou.ui.paneTabOfType(hou.paneTabType.NetworkEditor)
            names = ("HIP", "HIPNAME", "JOB", "HFS", "HOUDINI_USER_PREF_DIR",
                     "HOUDINI_PATH", "HOUDINI_OTLSCAN_PATH", "HOUDINI_TEMP_DIR")
            return {"hipFile": hou.hipFile.path(), "unsaved": hou.hipFile.isNewFile(),
                    "dirty": hou.hipFile.hasUnsavedChanges(),
                    "network": editor.pwd().path() if editor else hou.pwd().path(),
                    "selected": [n.path() for n in hou.selectedNodes()],
                    "environment": {name: hou.getenv(name, "") for name in names},
                    "bridge": str(self.directory)}
        if action == "scene_graph":
            root = hou.node("/obj")
            result = []
            for n in (root,) + root.allSubChildren(recurse_in_locked_nodes=False):
                if not isinstance(n, hou.OpNode):
                    continue
                category = n.type().category()
                child_category = n.childTypeCategory()
                result.append({"path": n.path(), "isSop": isinstance(n, hou.SopNode),
                    "category": category.name(), "type": n.type().name(),
                    "childCategory": child_category.name() if child_category else "",
                    "inputs": [i.path() for i in n.inputs() if i is not None]})
            return result
        if action == "nodes":
            return sorted(n.path() for n in hou.node("/obj").allSubChildren()
                          if isinstance(n, hou.SopNode))
        if action == "operators":
            result = []
            for name, typ in hou.sopNodeTypeCategory().nodeTypes().items():
                definition = typ.definition()
                result.append({"name": name, "label": typ.description(), "icon": typ.icon(),
                    "inputs": typ.minNumInputs(), "maxInputs": typ.maxNumInputs(),
                    "library": definition.libraryFilePath() if definition else "",
                    "hidden": typ.hidden(), "deprecated": bool(typ.deprecated())})
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
            created_parent = False
            if isinstance(source, hou.SopNode):
                parent = source.parent()
            else:
                network = hou.node(request.get("network") or "/obj")
                if network is not None and network.childTypeCategory() == hou.sopNodeTypeCategory():
                    parent = network
                else:
                    parent = hou.node("/obj").createNode("geo", "dxsharp_studio")
                    created_parent = True
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
                if created_parent:
                    parent.destroy()
                raise
        if action == "view":
            viewer = hou.ui.paneTabOfType(hou.paneTabType.SceneViewer)
            if viewer is None:
                raise ValueError("No Houdini Scene Viewer")
            viewport = viewer.curViewport()
            camera = viewport.defaultCamera()
            result = {"rotation": list(camera.rotation().asTuple()), "pivot": list(camera.pivot()),
                      "translation": list(camera.translation()), "viewport": viewport.name(),
                      "perspective": camera.isPerspective(), "resolution": viewport.resolutionInPixels()}
            source = hou.node(request.get("path") or "/")
            if isinstance(source, hou.SopNode):
                # HOM matrices use row vectors. Compose SOP local -> camera -> viewport pixels.
                obj = source.parent()
                while obj is not None and not isinstance(obj, hou.ObjNode):
                    obj = obj.parent()
                current = viewer.pwd()
                to_camera = None
                if obj is not None and current == obj:
                    to_camera = (viewport.cameraToModelTransform() * viewport.modelToGeometryTransform()).inverted()
                elif obj is not None and current.path() == "/obj":
                    to_camera = obj.worldTransform() * viewport.cameraToModelTransform().inverted()
                if to_camera is not None:
                    to_pixels = viewport.ndcToCameraTransform().inverted() * viewport.viewportToNDCTransform().inverted()
                    result.update({"toCamera": to_camera.asTuple(), "toPixels": to_pixels.asTuple()})
            return result
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
                ("Vertex", geo.vertexAttribs(), tuple(v for p in geo.prims()[:8] for v in p.vertices()[:8])),
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
            if "nodeId" in request and (node.sessionId() != request["nodeId"] or hou.hipFile.path() != request.get("scene")):
                raise ValueError("The node or scene changed; refresh the inspector before editing")
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
            def point_stream(name, sizes):
                attrib = geo.findPointAttrib(name)
                if attrib is None or attrib.dataType() != hou.attribData.Float or attrib.size() not in sizes:
                    return []
                values = geo.pointFloatAttribValues(name)
                return [list(values[i:i+attrib.size()]) for i in range(0, len(values), attrib.size())]
            colors = point_stream("Cd", (3, 4))
            normals = point_stream("N", (3,))
            uvs = point_stream("uv", (2, 3))
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
                                if colors:
                                    colors.append(colors[indices[prim.vertices()[0].point().number()]])
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
                    "triangles": triangles, "colors": colors, "normals": normals if len(normals)==len(positions) else [],
                    "uv": uvs if len(uvs)==len(positions) else [],
                    "nodeId": node.sessionId(), "scene": hou.hipFile.path(),
                    "revision": node.cookCount()}
        raise ValueError("Unknown action")

if hasattr(hou.session, "dxsharp_studio_bridge"):
    hou.session.dxsharp_studio_bridge.stop()
hou.session.dxsharp_studio_bridge = StudioBridge()
hou.session.dxsharp_studio_bridge.start()
