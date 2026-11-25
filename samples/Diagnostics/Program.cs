using DXSharp.Houdini.Engine;
using DXSharp.Houdini.Interop;

try {
    if ( args.Length != 0 && (args.Length != 2 || args[0] != "--root") ) {
        Console.Error.WriteLine( "Usage: Diagnostics [--root <Houdini installation>]" );
        return 2;
    }
    var installation = HoudiniInstallation.Resolve( args.Length == 2 ? args[1] : null );
    Console.WriteLine( $"Client: {installation.ClientLibrary}" );
    var version = HapiClient.Probe( installation );
    Console.WriteLine( version );
    Compatibility.RequireBaseline( version );
    Console.WriteLine( "PASS: exact client version matches the target. Session and license not tested." );
    return 0;
}
catch ( Exception ex ) {
    Console.Error.WriteLine( ex.Message );
    return 1;
}
