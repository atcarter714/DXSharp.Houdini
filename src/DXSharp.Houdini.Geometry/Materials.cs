using System.Numerics;

namespace DXSharp.Houdini.Geometry;

public sealed record AssetReference(string Identifier,string? ResolvedPath=null,string? ColorSpace=null);
public sealed record ShaderInput(float[]? Constant=null,AssetReference? Texture=null,string Channel="rgb",
    string? UnsupportedConnection=null);
public sealed record SourceMaterial(string Path,string ShaderId,Dictionary<string,ShaderInput> Inputs);
public sealed record RuntimeMaterial(string Path,Vector4 BaseColor,float Metallic,float Roughness,Vector3 Emission,
    float Opacity,float Ior,Dictionary<string,ShaderInput> Textures,string[] Diagnostics);

public static class MaterialProcessor {
    // A deliberately narrow translator for constant/direct-image Standard Surface
    // and Preview Surface inputs. Preserve unhandled graph edges as diagnostics.
    public static RuntimeMaterial Translate(SourceMaterial source) {
        var warnings=new List<string>();
        var textures=new Dictionary<string,ShaderInput>(StringComparer.Ordinal);
        bool usd=source.ShaderId=="UsdPreviewSurface";
        bool mtlx=source.ShaderId is "ND_standard_surface_surfaceshader" or "ND_standard_surface";
        if(!usd&&!mtlx)warnings.Add($"Unsupported surface shader: {source.ShaderId}");
        var consumed=new HashSet<string>();
        float[] Get(string name,string role,float[] fallback) {
            consumed.Add(name);
            if(!source.Inputs.TryGetValue(name,out var input))return fallback;
            if(input.UnsupportedConnection!=null)warnings.Add($"{name}: {input.UnsupportedConnection}");
            if(input.Texture!=null) {
                textures[role]=input;
                if(string.IsNullOrEmpty(input.Texture.ResolvedPath))warnings.Add($"{name}: unresolved {input.Texture.Identifier}");
            }
            if(input.Constant==null)return fallback;
            if(input.Constant.Length!=fallback.Length||input.Constant.Any(x=>!float.IsFinite(x))) {
                warnings.Add($"{name}: invalid constant tuple");return fallback;
            }
            return input.Constant;
        }
        float Scalar(string name,string role,float fallback)=>Get(name,role,[fallback])[0];
        var c=Get(usd?"diffuseColor":"base_color","baseColor",[.18f,.18f,.18f]);
        float baseWeight=usd?1:Scalar("base","baseWeight",1);
        float metal=Scalar(usd?"metallic":"metalness","metallic",0);
        float rough=Scalar(usd?"roughness":"specular_roughness","roughness",.5f);
        var e=Get(usd?"emissiveColor":"emission_color","emission",[0,0,0]);
        float emissionWeight=usd?1:Scalar("emission","emissionWeight",0);
        var o=Get("opacity","opacity",usd?[1]:[1,1,1]);
        if(o.Length==3&&(o[0]!=o[1]||o[0]!=o[2]))warnings.Add("Colored opacity reduced to its mean.");
        float opacity=Math.Clamp(o.Average(),0,1);
        float ior=Scalar(usd?"ior":"specular_IOR","ior",1.5f);
        _=Get("normal","normal",[0,0,1]);
        foreach(string name in source.Inputs.Keys.Order(StringComparer.Ordinal))
            if(!consumed.Contains(name))warnings.Add($"Untranslated input: {name}");
        return new(source.Path,new(c[0]*baseWeight,c[1]*baseWeight,c[2]*baseWeight,opacity),
            Math.Clamp(metal,0,1),Math.Clamp(rough,.045f,1),new(e[0]*emissionWeight,e[1]*emissionWeight,e[2]*emissionWeight),
            opacity,Math.Max(1,ior),textures,warnings.ToArray());
    }
}
