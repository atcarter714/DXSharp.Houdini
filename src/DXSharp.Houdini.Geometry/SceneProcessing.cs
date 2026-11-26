using System.Numerics;
using System.Runtime.InteropServices;

namespace DXSharp.Houdini.Geometry;

// Source-neutral triangle topology. Corner attributes are indexed by the original
// corner stream, not by point number. No implicit triangulation or axis conversion.
public sealed record FloatAttribute(string Name, AttributeDomain Domain, int Components, float[] Values);
[StructLayout(LayoutKind.Sequential, Pack=4)]
public readonly record struct RenderVertex(Vector3 Position, Vector3 Normal, Vector4 Color, Vector2 UV);
public sealed record DrawRange(string Material, int StartIndex, int IndexCount);
public sealed record CompiledMesh(RenderVertex[] Vertices, uint[] Indices, DrawRange[] Ranges);
public sealed record SceneObject(string Path, Matrix4x4 LocalToWorld, string GeometryId);
public sealed record SceneSnapshot(string Source, SceneObject[] Objects, Dictionary<string,CompiledMesh> Meshes,
    Dictionary<string,RuntimeMaterial> Materials);

public static class GeometryProcessor {
    public static CompiledMesh Compile(MeshTopology source, IReadOnlyList<FloatAttribute> attributes,
        IReadOnlyList<string>? faceMaterials=null) {
        var points=source.Points;
        var corners=source.CornerPointIndices;
        var faces=source.FaceCounts;
        if(faces.ContainsAnyExcept(3))throw new InvalidDataException("Triangulate upstream before compiling.");
        if(faceMaterials!=null&&faceMaterials.Count!=faces.Length)throw new InvalidDataException("Material count differs from face count.");
        var streams=new Dictionary<(string,AttributeDomain),FloatAttribute>();
        foreach(var a in attributes) {
            int count=a.Domain switch {
                AttributeDomain.Point=>points.Length, AttributeDomain.Vertex=>corners.Length,
                AttributeDomain.Primitive=>faces.Length, AttributeDomain.Detail=>1,
                _=>throw new InvalidDataException("Invalid domain.")
            };
            if(a.Components<=0||a.Components>16||a.Values.Length!=checked(count*a.Components)||
                a.Values.Any(x=>!float.IsFinite(x))||!streams.TryAdd((a.Name,a.Domain),a))
                throw new InvalidDataException($"Invalid or duplicate attribute {a.Name}.");
        }
        FloatAttribute? Find(string name,params int[] components) {
            foreach(var domain in new[]{AttributeDomain.Vertex,AttributeDomain.Point,AttributeDomain.Primitive,AttributeDomain.Detail})
                if(streams.TryGetValue((name,domain),out var a)) {
                    if(!components.Contains(a.Components))throw new InvalidDataException($"Invalid tuple size for {name}.");
                    return a;
                }
            return null;
        }
        var normal=Find("N",3);var color=Find("Cd",3,4);var uv=Find("uv",2,3);
        static ReadOnlySpan<float> Read(FloatAttribute a,int p,int c,int f) =>
            a.Values.AsSpan(CanonicalAttributes.ElementIndex(a.Domain,p,c,f)*a.Components,a.Components);
        var smooth=new Vector3[points.Length];
        for(int c=0;c<corners.Length;c+=3) {
            int a=corners[c],b=corners[c+1],d=corners[c+2];
            var n=Vector3.Cross(points[b]-points[a],points[d]-points[a]);
            smooth[a]+=n;smooth[b]+=n;smooth[d]+=n;
        }
        static Vector3 Unit(Vector3 n)=>n.LengthSquared()>1e-20f?Vector3.Normalize(n):Vector3.UnitY;
        var vertices=new List<RenderVertex>();
        var index=new Dictionary<RenderVertex,uint>();
        var indices=new uint[corners.Length];
        var ranges=new List<DrawRange>();
        for(int c=0;c<corners.Length;c++) {
            int p=corners[c],f=c/3;
            Vector3 n=Unit(smooth[p]);Vector4 cd=Vector4.One;Vector2 st=Vector2.Zero;
            if(normal!=null){var v=Read(normal,p,c,f);n=Unit(new(v[0],v[1],v[2]));}
            if(color!=null){var v=Read(color,p,c,f);cd=new(v[0],v[1],v[2],v.Length==4?v[3]:1);}
            if(uv!=null){var v=Read(uv,p,c,f);st=new(v[0],v[1]);}
            var vertex=new RenderVertex(points[p],n,cd,st);
            if(!index.TryGetValue(vertex,out uint i)){i=checked((uint)vertices.Count);index.Add(vertex,i);vertices.Add(vertex);}
            indices[c]=i;
            if(c%3==0) {
                string material=faceMaterials?[f]??"";
                if(ranges.Count>0&&ranges[^1].Material==material)ranges[^1]=ranges[^1] with {IndexCount=ranges[^1].IndexCount+3};
                else ranges.Add(new(material,c,3));
            }
        }
        return new(vertices.ToArray(),indices,ranges.ToArray());
    }
}
