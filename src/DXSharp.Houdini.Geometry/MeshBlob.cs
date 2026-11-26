using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace DXSharp.Houdini.Geometry;

/// <summary>Version 1 little-endian mesh container. Raw 48-byte vertex and
/// UInt32 index streams, followed by UTF-8 range metadata and SHA-256.</summary>
public static class MeshBlob {
    private const uint Magic=0x4D415844; // DXAM
    private const int Version=1, Stride=48, MaxVertices=10_000_000, MaxIndices=30_000_000, MaxMetadata=16*1024*1024;
    private static void Validate(CompiledMesh mesh) {
        if(!BitConverter.IsLittleEndian||Marshal.SizeOf<RenderVertex>()!=Stride)throw new PlatformNotSupportedException("Expected little-endian 48-byte vertex ABI.");
        if(mesh.Vertices.Length>MaxVertices||mesh.Indices.Length>MaxIndices||mesh.Indices.Length%3!=0)
            throw new InvalidDataException("Invalid mesh size.");
        foreach(var i in mesh.Indices)if(i>=mesh.Vertices.Length)throw new InvalidDataException("Invalid vertex index.");
        foreach(float f in MemoryMarshal.Cast<RenderVertex,float>(mesh.Vertices.AsSpan()))
            if(!float.IsFinite(f))throw new InvalidDataException("Non-finite vertex.");
        int next=0;
        foreach(var range in mesh.Ranges) {
            if(range.Material==null||range.StartIndex!=next||range.IndexCount<=0||range.IndexCount%3!=0||range.IndexCount>mesh.Indices.Length-next)
                throw new InvalidDataException("Invalid draw ranges.");
            next+=range.IndexCount;
        }
        if(next!=mesh.Indices.Length)throw new InvalidDataException("Draw ranges do not cover indices.");
    }
    public static void Write(string path,CompiledMesh mesh) {
        Validate(mesh);
        byte[] metadata=JsonSerializer.SerializeToUtf8Bytes(mesh.Ranges);
        if(metadata.Length>MaxMetadata)throw new InvalidDataException("Metadata exceeds limit.");
        string target=Path.GetFullPath(path),temp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None)) {
                using(var w=new BinaryWriter(stream,System.Text.Encoding.UTF8,true)) {
                    w.Write(Magic);w.Write(Version);w.Write(Stride);w.Write(mesh.Vertices.Length);w.Write(mesh.Indices.Length);w.Write(metadata.Length);
                    w.Write(0L); // align vertex stream to 32 bytes
                    w.Write(MemoryMarshal.AsBytes(mesh.Vertices.AsSpan()));
                    w.Write(MemoryMarshal.AsBytes(mesh.Indices.AsSpan()));w.Write(metadata);
                }
                stream.Position=0;var hash=SHA256.HashData(stream);stream.Write(hash);stream.Flush(true);
            }
            File.Move(temp,target,true);
        } finally {File.Delete(temp);}
    }
    public static CompiledMesh Read(string path) {
        using var stream=File.OpenRead(path);
        using var r=new BinaryReader(stream,System.Text.Encoding.UTF8,true);
        if(stream.Length<64||r.ReadUInt32()!=Magic||r.ReadInt32()!=Version||r.ReadInt32()!=Stride)
            throw new InvalidDataException("Unsupported DXAM header.");
        int nv=r.ReadInt32(),ni=r.ReadInt32(),nm=r.ReadInt32();
        if(r.ReadInt64()!=0||nv<0||nv>MaxVertices||ni<0||ni>MaxIndices||nm<0||nm>MaxMetadata||
            stream.Length!=32L+nv*(long)Stride+ni*4L+nm+32)throw new InvalidDataException("Invalid DXAM length.");
        var vertices=new RenderVertex[nv];var indices=new uint[ni];var metadata=new byte[nm];
        stream.ReadExactly(MemoryMarshal.AsBytes(vertices.AsSpan()));stream.ReadExactly(MemoryMarshal.AsBytes(indices.AsSpan()));stream.ReadExactly(metadata);
        byte[] expected=r.ReadBytes(32);
        stream.Position=0;
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer=new byte[64*1024];long remaining=stream.Length-32;
        while(remaining>0){int n=(int)Math.Min(buffer.Length,remaining);stream.ReadExactly(buffer.AsSpan(0,n));hash.AppendData(buffer,0,n);remaining-=n;}
        if(!CryptographicOperations.FixedTimeEquals(expected,hash.GetHashAndReset()))throw new InvalidDataException("DXAM checksum mismatch.");
        var result=new CompiledMesh(vertices,indices,JsonSerializer.Deserialize<DrawRange[]>(metadata)??throw new InvalidDataException("Missing ranges."));
        Validate(result);return result;
    }
}
