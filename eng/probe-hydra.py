"""Run using the exact Houdini installation's hython, not system Python.
Optional NativeAOT probe is loaded only into this short-lived process.
"""
import argparse, ctypes, json, pathlib
from pxr import Usd, Plug

parser=argparse.ArgumentParser()
parser.add_argument("--dll",type=pathlib.Path)
parser.add_argument("--hardware",action="store_true")
args=parser.parse_args()
print(json.dumps({"usdVersion":list(Usd.GetVersion()),
    "registeredImagingPlugins":[p.name for p in Plug.Registry().GetAllPlugins() if p.name.lower().startswith(("hd","hgi"))]},indent=2),flush=True)
if args.dll:
    dll=ctypes.CDLL(str(args.dll.resolve(strict=True)))
    dll.dxsharp_probe_abi.argtypes=[]
    dll.dxsharp_probe_abi.restype=ctypes.c_int
    if dll.dxsharp_probe_abi()!=1:
        raise RuntimeError("Unexpected probe ABI")
    dll.dxsharp_probe_rgba.argtypes=[ctypes.c_uint32,ctypes.c_uint32,ctypes.POINTER(ctypes.c_ubyte),ctypes.c_size_t,ctypes.c_int]
    dll.dxsharp_probe_rgba.restype=ctypes.c_int
    # Deliberately unaligned row width, exercises D3D12 pitch removal.
    w,h=61,47
    buf=(ctypes.c_ubyte*(w*h*4))()
    hr=dll.dxsharp_probe_rgba(w,h,buf,len(buf),0 if args.hardware else 1)
    if hr:
        raise RuntimeError(f"DXSharp AOV probe failed: 0x{hr & 0xffffffff:08X}")
    if bytes(buf)!=bytes([0,255,0,255])*(w*h):
        raise RuntimeError("AOV pixel mismatch")
    print(f"PASS: DXSharp NativeAOT D3D12 clear/readback in hython, {w} x {h} RGBA8.",flush=True)
