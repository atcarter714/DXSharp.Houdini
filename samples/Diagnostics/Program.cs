using DXSharp.Houdini.Engine;
using DXSharp.Houdini.Interop;

try {
    string? root = null;
    Version? requested = null;
    bool list = false, strict = false;
    for ( int i = 0; i < args.Length; i++ ) {
        switch ( args[i] ) {
            case "--root" when i + 1 < args.Length: root = args[++i]; break;
            case "--version" when i + 1 < args.Length: requested = Version.Parse( args[++i] ); break;
            case "--list": list = true; break;
            case "--require-baseline": strict = true; break;
            default: Console.Error.WriteLine( "Usage: Diagnostics [--list | [--root <path>] [--version <major.minor[.build]>] [--require-baseline]]" ); return 2;
        }
    }
    if ( list ) {
        foreach ( var item in HoudiniInstallation.Discover() )
            Console.WriteLine( $"{item.InstalledVersion} | HDK: {item.HasHdk} | {item.Root}" );
        return 0;
    }
    var installation = HoudiniInstallation.Resolve( root, requested );
    Console.WriteLine( $"Client: {installation.ClientLibrary}" );
    var version = HapiClient.Probe( installation );
    Console.WriteLine( version );
    if ( strict ) Compatibility.RequireBaseline( version );
    Console.WriteLine( Compatibility.IsQualifiedTarget( version )
        ? "PASS: client version matches the original baseline. Session and license not tested."
        : "PASS: client version query. This build is outside the original baseline; native ABI, session and license are not qualified." );
    return 0;
}
catch ( Exception ex ) {
    Console.Error.WriteLine( ex.Message );
    return 1;
}
