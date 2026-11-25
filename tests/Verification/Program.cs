using System.Numerics;
using DXSharp.Houdini.Engine;
using DXSharp.Houdini.Geometry;

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
