"""Public DXSharp Houdini package entry points. Opt-in local bridge."""
import importlib.util
import pathlib
import hou

def start():
    existing = getattr(hou.session, "dxsharp_studio_bridge", None)
    if existing is not None and existing.running:
        hou.ui.setStatusMessage("DXSharp Studio bridge already running")
        return existing
    path = pathlib.Path(__file__).resolve().parent.parent / "scripts" / "python" / "dxsharp_studio_bridge.py"
    spec = importlib.util.spec_from_file_location("dxsharp_studio_bridge", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    hou.ui.setStatusMessage("DXSharp Studio bridge ready")
    return hou.session.dxsharp_studio_bridge

def stop():
    bridge = getattr(hou.session, "dxsharp_studio_bridge", None)
    if bridge is not None:
        bridge.stop()
    hou.ui.setStatusMessage("DXSharp Studio bridge stopped")
