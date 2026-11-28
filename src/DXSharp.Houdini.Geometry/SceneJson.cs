using System.Numerics;
using System.Text.Json;

namespace DXSharp.Houdini.Geometry;
public static class SceneJson {
    public static CompiledMesh CompileMesh(JsonElement data) {
        var points=data.GetProperty("positions").EnumerateArray().Select(p=>new Vector3(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle())).ToArray();
        var corners=data.GetProperty("corners").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        var faces=data.GetProperty("faces").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
        var attributes=data.GetProperty("attributes").EnumerateArray().Select(a=>new FloatAttribute(
            a.GetProperty("name").GetString()!,Enum.Parse<AttributeDomain>(a.GetProperty("domain").GetString()!),
            a.GetProperty("components").GetInt32(),a.GetProperty("values").EnumerateArray().Select(x=>x.GetSingle()).ToArray())).ToArray();
        var mats=data.GetProperty("materials").EnumerateArray().Select(x=>x.GetString()??"").ToArray();
        return GeometryProcessor.Compile(new(points,corners,faces),attributes,mats);
    }
    public static RuntimeMaterial[] TranslateMaterials(JsonElement stage) {
        var options=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
        return stage.GetProperty("materials").EnumerateArray().Select(m=>
            MaterialProcessor.Translate(m.Deserialize<SourceMaterial>(options)??throw new InvalidDataException("Missing material."))).ToArray();
    }
}
