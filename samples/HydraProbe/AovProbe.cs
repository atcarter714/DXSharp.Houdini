using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DXSharp.Graphics;
using Windows.Win32.Graphics.Direct3D12;
using Windows.Win32.Graphics.Dxgi.Common;

namespace DXSharp.HydraProbe;

// Experimental C ABI. Caller owns output storage; no managed objects or C++
// USD objects cross this boundary. A synchronous clear/readback proves the
// minimum color-AOV transport, not Hydra integration or production rendering.
public static unsafe class AovProbe {
    [UnmanagedCallersOnly(EntryPoint="dxsharp_probe_abi",CallConvs=[typeof(CallConvCdecl)])]
    public static int Abi()=>1;

    [UnmanagedCallersOnly(EntryPoint="dxsharp_probe_rgba",CallConvs=[typeof(CallConvCdecl)])]
    public static int Render(uint width,uint height,byte* output,nuint capacity,int warp) {
        if(output==null||width==0||height==0||width>2048||height>2048||capacity<(nuint)width*height*4)return -1;
        try {
            using var device=GraphicsDevice.Create(new(){UseWarp=warp!=0,UseAgility=false});
            using var nativeDevice=device.QueryNativeInterface<ID3D12Device>();
            using var queue=device.CreateCommandQueue();
            using var allocator=device.CreateCommandAllocator();
            using var commands=device.CreateCommandList(allocator);
            D3D12_RESOURCE_DESC description=new(){
                Dimension=D3D12_RESOURCE_DIMENSION.D3D12_RESOURCE_DIMENSION_TEXTURE2D,
                Width=width,Height=height,DepthOrArraySize=1,MipLevels=1,
                Format=DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM,SampleDesc=new(){Count=1},
                Flags=D3D12_RESOURCE_FLAGS.D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET
            };
            D3D12_HEAP_PROPERTIES memory=new(){Type=D3D12_HEAP_TYPE.D3D12_HEAP_TYPE_DEFAULT,CreationNodeMask=1,VisibleNodeMask=1};
            using var color=device.CreateCommittedResource(description,memory,ResourceState.RenderTarget);
            using var heap=device.CreateRenderTargetHeap(1);
            var target=device.CreateRenderTargetView(color,heap,0);
            commands.Clear(target,0,1,0,1);
            D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint;
            ulong bytes;
            nativeDevice.Interface.GetCopyableFootprints(&description,0,1,0,&footprint,null,null,&bytes);
            using var readback=device.CreateBuffer(bytes,BufferMemory.Readback);
            commands.Transition(color,ResourceState.RenderTarget,ResourceState.CopySource);
            using(var src=color.QueryNativeInterface<ID3D12Resource>())
            using(var dst=readback.QueryNativeInterface<ID3D12Resource>()) {
                D3D12_TEXTURE_COPY_LOCATION from=new(){pResource=(ID3D12Resource_unmanaged*)src.Pointer,
                    Type=D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX};
                D3D12_TEXTURE_COPY_LOCATION to=new(){pResource=(ID3D12Resource_unmanaged*)dst.Pointer,
                    Type=D3D12_TEXTURE_COPY_TYPE.D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT};
                to.Anonymous.PlacedFootprint=footprint;
                commands.RecordNative(native=>native.CopyTextureRegion(to,0,0,0,from,null),color,readback);
            }
            commands.Close();queue.Wait(queue.Submit(commands));
            byte[] pixels=new byte[checked((int)bytes)];readback.Read<byte>(pixels);
            int row=checked((int)width*4);
            for(uint y=0;y<height;y++)pixels.AsSpan(checked((int)(footprint.Offset+y*(ulong)footprint.Footprint.RowPitch)),row)
                .CopyTo(new Span<byte>(output+checked((int)y*row),row));
            return 0;
        } catch(Exception error) {return error.HResult<0?error.HResult:-2;}
    }
}
