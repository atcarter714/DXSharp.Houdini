using System.Numerics;

namespace DXSharp.Houdini.Geometry;

public enum AttributeDomain { Detail, Primitive, Point, Vertex }

public readonly record struct CanonicalAttribute(string Name, string Semantic, int SemanticIndex, int Components, string DxgiFormat, string HlslType);

public static class CanonicalAttributes {
    // IA vertex element formats, not typed UAV/texture format guarantees.
    public static bool TryResolve(string name,int sourceComponents,out CanonicalAttribute attribute) {
        attribute=(name,sourceComponents) switch {
            ("P",3)=>new(name,"POSITION",0,3,"R32G32B32_FLOAT","float3"),
            ("N",3)=>new(name,"NORMAL",0,3,"R32G32B32_FLOAT","float3"),
            ("Cd",3 or 4)=>new(name,"COLOR",0,4,"R32G32B32A32_FLOAT","float4"),
            ("uv",2 or 3)=>new(name,"TEXCOORD",0,2,"R32G32_FLOAT","float2"),
            ("v",3)=>new(name,"VELOCITY",0,3,"R32G32B32_FLOAT","float3"),
            ("tangentu",3)=>new(name,"TANGENT",0,3,"R32G32B32_FLOAT","float3"),
            ("tangentv",3)=>new(name,"BINORMAL",0,3,"R32G32B32_FLOAT","float3"),
            ("orient",4)=>new(name,"ORIENTATION",0,4,"R32G32B32A32_FLOAT","float4"),
            ("pscale",1)=>new(name,"SCALE",0,1,"R32_FLOAT","float"),
            ("Alpha",1)=>new(name,"ALPHA",0,1,"R32_FLOAT","float"),
            _=>default
        };
        return attribute.Components!=0;
    }

    public static float[] ConvertFloatStream(string name, int sourceComponents, ReadOnlySpan<float> source) {
        if(!TryResolve(name,sourceComponents,out var layout)) throw new ArgumentException("Unsupported canonical attribute.",nameof(name));
        if(source.Length%sourceComponents!=0) throw new ArgumentException("Incomplete attribute tuple.",nameof(source));
        int count=source.Length/sourceComponents;
        var result=new float[checked(count*layout.Components)];
        for(int i=0;i<count;i++) {
            var input=source.Slice(i*sourceComponents,sourceComponents);
            var output=result.AsSpan(i*layout.Components,layout.Components);
            input[..Math.Min(input.Length,output.Length)].CopyTo(output);
            if(name=="Cd"&&sourceComponents==3) output[3]=1;
        }
        return result;
    }

    // Houdini precedence: Vertex > Point > Primitive > Detail. This does not
    // weld render corners across UV seams or hard normals.
    public static int ElementIndex(AttributeDomain domain,int pointIndex,int cornerIndex,int primitiveIndex)=>domain switch {
        AttributeDomain.Point=>pointIndex,
        AttributeDomain.Vertex=>cornerIndex,
        AttributeDomain.Primitive=>primitiveIndex,
        AttributeDomain.Detail=>0,
        _=>throw new ArgumentOutOfRangeException(nameof(domain))
    };
}
