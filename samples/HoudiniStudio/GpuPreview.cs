using System.Numerics;
using System.Runtime.InteropServices;
using DXSharp.Graphics;
using DXSharp.Shaders;
using DXSharp.Framework.Hosting;
using DXSharp.Houdini.Geometry;

namespace DXSharp.Houdini.Studio;

// Reusable D3D12 scene recording. All uploads are recorded on the presentation
// queue, before draws. DXSharp retains the native mesh resources until retirement.
internal sealed class GpuPreview : IDisposable {
    private readonly GraphicsDevice _device;
    private readonly RootSignature _root;
    private readonly PipelineState _pipeline;
    private Mesh? _mesh;
    private CompiledMesh? _uploaded;
    private Vector3 _center;
    private float _radius=1;
    public CompiledMesh? Source { get; set; }
    public Matrix4x4 Rotation { get; set; }=Matrix4x4.Identity;
    public float Zoom { get; set; }=1;
    public Vector2 Pan { get; set; }
    public float Exposure { get; set; }
    public float Roughness { get; set; }=.5f;
    public float Metallic { get; set; }
    public int DebugMode { get; set; }
    public GpuPreview(GraphicsDevice device) {
        _device=device;
        using var compiler=new ShaderCompiler();
        string shader=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Shaders","Preview.hlsl"));
        _root=device.CreateGraphicsConstantsRootSignature(36);
        try {
            _pipeline=device.CreateGraphicsPipeline(_root,compiler.Compile(shader,"VS","vs_6_0").RequireSuccess(),
                compiler.Compile(shader,"PS","ps_6_0").RequireSuccess(),[
                    new("POSITION",VertexFormat.Float3,0),new("NORMAL",VertexFormat.Float3,12),
                    new("COLOR",VertexFormat.Float4,24),new("TEXCOORD",VertexFormat.Float2,40)
                ],depthEnabled:true);
        } catch {_root.Dispose();throw;}
    }
    public void Draw(RenderFrame frame) {
        var source=Source;
        if(!ReferenceEquals(source,_uploaded)) {
            Mesh? replacement=null;
            if(source is {Indices.Length:>0}) {
                replacement=_device.CreateMesh<RenderVertex>(frame.Commands,source.Vertices,source.Indices);
                Vector3 lo=source.Vertices[0].Position,hi=lo;
                foreach(var v in source.Vertices){lo=Vector3.Min(lo,v.Position);hi=Vector3.Max(hi,v.Position);}
                _center=(lo+hi)*.5f;_radius=Math.Max((hi-lo).Length()*.5f,1e-6f);
            }
            _mesh?.Dispose();_mesh=replacement;_uploaded=source;
        }
        if(_mesh is null)return;
        float aspect=(float)frame.Size.Width/frame.Size.Height;
        // Same orthographic framing and view-space rotation as the CPU reference.
        float sy=.9f*Zoom*Math.Min(1,aspect)/_radius;
        float sx=sy/aspect;
        var transform=Matrix4x4.CreateTranslation(-_center)*Rotation*
            Matrix4x4.CreateScale(sx,sy,-.45f/_radius)*
            Matrix4x4.CreateTranslation(Pan.X,Pan.Y,.5f);
        Span<float> constants=stackalloc float[36];
        MemoryMarshal.Cast<float,Matrix4x4>(constants[..32])[0]=transform;
        MemoryMarshal.Cast<float,Matrix4x4>(constants[..32])[1]=Rotation;
        constants[32]=Exposure;constants[33]=Roughness;constants[34]=Metallic;constants[35]=DebugMode;
        frame.Commands.SetGraphicsPipeline(_pipeline);
        frame.Commands.SetGraphicsConstants(MemoryMarshal.Cast<float,uint>(constants));
        frame.Commands.DrawMesh(_mesh);
    }
    public void Dispose(){_mesh?.Dispose();_pipeline.Dispose();_root.Dispose();}
}
