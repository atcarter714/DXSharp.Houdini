"""Discover Houdini USD in hython, or independently qualify the AOV DLL in ordinary x64 Python."""
import argparse, ctypes, json, pathlib

parser=argparse.ArgumentParser()
parser.add_argument("--dll",type=pathlib.Path)
parser.add_argument("--hardware",action="store_true")
parser.add_argument("--standalone",action="store_true")
args=parser.parse_args()
if args.standalone:
    if not args.dll:
        parser.error("--standalone requires --dll")
    if ctypes.sizeof(ctypes.c_void_p)!=8:
        raise RuntimeError("The native probe requires x64 Python")
else:
    from pxr import Usd, Plug
    print(json.dumps({"usdVersion":list(Usd.GetVersion()),
        "registeredImagingPlugins":[p.name for p in Plug.Registry().GetAllPlugins() if p.name.lower().startswith(("hd","hgi"))]},indent=2),flush=True)
    print("PASS: Houdini USD discovery",flush=True)
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
    host="standalone Python" if args.standalone else "hython"
    print(f"PASS: DXSharp NativeAOT D3D12 clear/readback in {host}, {w} x {h} RGBA8.",flush=True)
