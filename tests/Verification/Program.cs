using System.Numerics;
using DXSharp.Houdini.Engine;
using DXSharp.Houdini.Geometry;
using DXSharp.Houdini.Interop;

static void Check( bool condition, string message ) {
    if ( !condition ) throw new InvalidOperationException( message );
}

static void Throws<T>( Action action ) where T : Exception {
    try { action(); }
    catch ( T ) { return; }
    throw new InvalidOperationException( $"Expected {typeof( T ).Name}." );
}

Compatibility.RequireBaseline( Compatibility.Baseline );
Throws<NotSupportedException>( () => Compatibility.RequireBaseline( Compatibility.Baseline with { ApiRevision = 1 } ) );
Throws<NotSupportedException>( () => Compatibility.RequireBaseline( Compatibility.Baseline with { HoudiniBuild = 515 } ) );
Vector3[] points = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.One];
int[] corners = [0, 1, 2, 2, 1, 3];
var mesh = new MeshTopology( points, corners, [3, 3] );
corners[0] = 3;
Check( mesh.GetTriangleIndices().SequenceEqual( new[] { 0, 1, 2, 2, 1, 3 } ), "Topology copy/indices" );
Throws<ArgumentException>( () => new MeshTopology( points, [0, 1, 2], [4] ) );
Throws<ArgumentOutOfRangeException>( () => new MeshTopology( points, [0, 1, 4], [3] ) );
Throws<InvalidOperationException>( () => new MeshTopology( points, [0, 1, 2, 3], [4] ).GetTriangleIndices() );
Console.WriteLine( "PASS: version and mesh topology verification." );

Check( CanonicalAttributes.TryResolve("uv",3,out var uv) && uv.Components==2 && uv.Semantic=="TEXCOORD", "UV layout" );
Check( CanonicalAttributes.ConvertFloatStream("Cd",3,[.2f,.4f,.6f]).SequenceEqual(new[]{.2f,.4f,.6f,1f}), "Color alpha expansion" );
Check( CanonicalAttributes.ConvertFloatStream("uv",3,[.2f,.4f,0f]).SequenceEqual(new[]{.2f,.4f}), "UV dimensional conversion" );
Check( CanonicalAttributes.ElementIndex(AttributeDomain.Vertex,5,17,3)==17, "Corner domain" );
Throws<ArgumentException>( ()=>CanonicalAttributes.ConvertFloatStream("P",3,[1f,2f]) );
Console.WriteLine("PASS: canonical attribute contracts.");

var sourceMesh=new MeshTopology([Vector3.Zero,Vector3.UnitX,Vector3.UnitY,Vector3.One],[0,1,2,2,1,3],[3,3]);
var compiled=GeometryProcessor.Compile(sourceMesh,[
    new("Cd",AttributeDomain.Detail,3,[.2f,.4f,.6f]),
    new("uv",AttributeDomain.Vertex,2,[0,0,1,0,0,1,.5f,.5f,1,0,1,1])
],["A","B"]);
Check(compiled.Vertices.Length==5&&compiled.Ranges.Length==2,"Corner seam / material ranges.");
Check(compiled.Vertices.All(v=>v.Color==new Vector4(.2f,.4f,.6f,1)),"Detail color.");
string blob=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".dxam");
try {
    MeshBlob.Write(blob,compiled);
    var restored=MeshBlob.Read(blob);
    Check(restored.Vertices.SequenceEqual(compiled.Vertices)&&restored.Indices.SequenceEqual(compiled.Indices)&&restored.Ranges.SequenceEqual(compiled.Ranges),"Binary round trip.");
    using(var stream=File.OpenWrite(blob)){stream.Position=32;stream.WriteByte(123);}
    Throws<InvalidDataException>(()=>MeshBlob.Read(blob));
} finally {File.Delete(blob);}
var material=MaterialProcessor.Translate(new("/mat/rock","UsdPreviewSurface",new() {
    ["diffuseColor"]=new([.2f,.3f,.4f],new("rock.png","C:/textures/rock.png","sRGB")),
    ["roughness"]=new([.6f]),["metallic"]=new([.1f]),["normal"]=new(UnsupportedConnection:"procedural graph")
}));
Check(material.Textures.ContainsKey("baseColor")&&material.Roughness==.6f&&material.Diagnostics.Length==1,"Material translation.");
Console.WriteLine("PASS: seam expansion, material translation and DXAM round trip/checksum.");

Throws<InvalidDataException>(()=>GeometryProcessor.Compile(sourceMesh,[new("Cd",AttributeDomain.Point,3,[1,2])]));
Throws<InvalidDataException>(()=>GeometryProcessor.Compile(new MeshTopology(points,[0,1,2,3],[4]),[]));
var precedence=GeometryProcessor.Compile(sourceMesh,[
    new("Cd",AttributeDomain.Detail,3,[1,0,0]),
    new("Cd",AttributeDomain.Vertex,3,[0,1,0,0,1,0,0,1,0,0,1,0,0,1,0,0,1,0])
]);
Check(precedence.Vertices.All(v=>v.Color==new Vector4(0,1,0,1)),"Corner precedence.");
var tex=new TexturePayload(2,2,TextureEncoding.Rgba8,new byte[16],true);
tex.Validate();Check(tex.RowPitch==8,"RGBA8 layout.");
Throws<InvalidDataException>(()=>new TexturePayload(2,2,TextureEncoding.Rgba32Float,new byte[16],false).Validate());
string raw=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".rgba32f");
try {
    byte[] rgba=new byte[16];
    File.WriteAllBytes(raw,rgba);
    File.WriteAllText(raw+".json",System.Text.Json.JsonSerializer.Serialize(new {width=1,height=1,format="R32G32B32A32_FLOAT",rowPitch=16,
        sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(rgba))}));
    Check(TexturePayload.ReadCop(raw).Bytes.SequenceEqual(rgba),"COP payload.");
    File.WriteAllBytes(raw,new byte[]{1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0});
    Throws<InvalidDataException>(()=>TexturePayload.ReadCop(raw));
} finally {File.Delete(raw);File.Delete(raw+".json");}
Console.WriteLine("PASS: strict geometry, attribute precedence and texture payload verification.");

Check(HoudiniInstallation.MatchesVersion(new(22,0,508,0),new(22,0)), "Minor version selector");
Check(HoudiniInstallation.MatchesVersion(new(22,0,508,0),new(22,0,508)), "Exact build selector");
Check(!HoudiniInstallation.MatchesVersion(new(22,0,514,0),new(22,0,508)), "Different builds");
string discoveryRoot=Path.Combine(Path.GetTempPath(),"dxsharp-discovery-"+Guid.NewGuid().ToString("N"));
try {
    string Make(string name) {
        string root=Path.Combine(discoveryRoot,name);
        Directory.CreateDirectory(Path.Combine(root,"bin"));
        File.WriteAllBytes(Path.Combine(root,"bin","libHAPIL.dll"),[]);
        return root;
    }
    string older=Make("Houdini 22.0.99"),newer=Make("Houdini 22.0.508");
    var installations=HoudiniInstallation.DiscoverCandidates([older,newer,older,Path.Combine(discoveryRoot,"missing")]);
    Check(installations.Count==2&&installations[0].Root==newer,"Numeric version sort and deduplication.");
} finally {if(Directory.Exists(discoveryRoot))Directory.Delete(discoveryRoot,true);}
Console.WriteLine("PASS: Houdini discovery and version selection.");
