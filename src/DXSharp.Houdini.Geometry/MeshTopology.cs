using System.Numerics;

namespace DXSharp.Houdini.Geometry;

// Houdini vertices are corners that reference points; they are not necessarily
// equivalent to render vertices. Face counts partition the corner stream.
public sealed class MeshTopology {
    private readonly Vector3[] _points;
    private readonly int[] _corners;
    private readonly int[] _faces;

    public ReadOnlySpan<Vector3> Points => _points;
    public ReadOnlySpan<int> CornerPointIndices => _corners;
    public ReadOnlySpan<int> FaceCounts => _faces;

    public MeshTopology( ReadOnlySpan<Vector3> points, ReadOnlySpan<int> corners, ReadOnlySpan<int> faces ) {
        long total = 0;
        foreach ( int count in faces ) {
            if ( count < 3 ) throw new ArgumentException( "Polygon faces require at least three corners.", nameof( faces ) );
            total += count;
        }
        if ( total != corners.Length ) throw new ArgumentException( "Face counts must partition the corner stream.", nameof( faces ) );
        foreach ( int index in corners )
            if ( (uint)index >= (uint)points.Length ) throw new ArgumentOutOfRangeException( nameof( corners ) );
        foreach ( var point in points )
            if ( !float.IsFinite( point.X ) || !float.IsFinite( point.Y ) || !float.IsFinite( point.Z ) )
                throw new ArgumentException( "Positions must be finite.", nameof( points ) );
        _points = points.ToArray();
        _corners = corners.ToArray();
        _faces = faces.ToArray();
    }

    // For already-triangulated meshes only; arbitrary concave polygons need
    // a real triangulator or a triangulation cook upstream.
    public int[] GetTriangleIndices() {
        foreach ( int count in _faces )
            if ( count != 3 ) throw new InvalidOperationException( "Mesh must be triangulated first." );
        return (int[])_corners.Clone();
    }
}
