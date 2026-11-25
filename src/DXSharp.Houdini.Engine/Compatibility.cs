using DXSharp.Houdini.Interop;

namespace DXSharp.Houdini.Engine;

public static class Compatibility {
    public static readonly HapiVersion Baseline = new( 22, 0, 514, 0, 9, 0, 0 );
    public const int HdkApiVersion = 22000475;

    public static bool IsQualifiedTarget( HapiVersion version ) => version == Baseline;

    public static void RequireBaseline( HapiVersion actual ) {
        if ( !IsQualifiedTarget( actual ) )
            throw new NotSupportedException( $"Expected {Baseline}; loaded {actual}. This build has not been qualified." );
    }

    public static HapiVersion Probe( string? root = null ) {
        var version = HapiClient.Probe( HoudiniInstallation.Resolve( root ) );
        RequireBaseline( version );
        return version;
    }
}
