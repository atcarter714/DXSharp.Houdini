using System.Security.Cryptography;
using System.Text.Json;

namespace DXSharp.Houdini.Geometry;
public enum TextureEncoding { Rgba8, Rgba32Float }
public sealed record TexturePayload(int Width,int Height,TextureEncoding Encoding,byte[] Bytes,bool IsSrgb) {
    public int RowPitch=>checked(Width*(Encoding==TextureEncoding.Rgba8?4:16));
    public void Validate() {
        if(Width<=0||Height<=0||Width>2048||Height>2048||!Enum.IsDefined(Encoding)||
            Bytes.Length!=checked(RowPitch*Height))throw new InvalidDataException("Preview texture exceeds limits or has invalid layout.");
    }
    public static TexturePayload ReadCop(string path,bool srgb=false) {
        using var doc=JsonDocument.Parse(File.ReadAllBytes(path+".json"));
        var m=doc.RootElement;
        int w=m.GetProperty("width").GetInt32(),h=m.GetProperty("height").GetInt32();
        if(w<=0||h<=0||w>2048||h>2048||m.GetProperty("format").GetString()!="R32G32B32A32_FLOAT"||
            m.GetProperty("rowPitch").GetInt32()!=checked(w*16)||new FileInfo(path).Length!=checked((long)w*h*16))
            throw new InvalidDataException("Invalid or oversized RGBA32F preview.");
        byte[] bytes=File.ReadAllBytes(path);
        if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(m.GetProperty("sha256").GetString(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("COP texture checksum mismatch.");
        var result=new TexturePayload(w,h,TextureEncoding.Rgba32Float,bytes,srgb);result.Validate();return result;
    }
}
