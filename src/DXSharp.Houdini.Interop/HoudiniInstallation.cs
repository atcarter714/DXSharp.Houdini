using System.Runtime.InteropServices;

namespace DXSharp.Houdini.Interop;

public sealed record HoudiniInstallation( string Root ) {
    public string BinDirectory => Path.Combine( Root, "bin" );
    public string ClientLibrary => Path.Combine( BinDirectory, "libHAPIL.dll" );
    public string ToolkitDirectory => Path.Combine( Root, "toolkit" );

    public static HoudiniInstallation Resolve( string? root = null ) {
        if ( !OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 )
            throw new PlatformNotSupportedException( "The initial HAPI client target is Windows x64." );

        root ??= Environment.GetEnvironmentVariable( "DXSHARP_HOUDINI_ROOT" );
        if ( string.IsNullOrWhiteSpace( root ) ) root = Environment.GetEnvironmentVariable( "HFS" );
        if ( !string.IsNullOrWhiteSpace( root ) ) return Validate( root );

        string basePath = Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.ProgramFiles ), "Side Effects Software" );
        string preferred = Path.Combine( basePath, "Houdini 22.0.514" );
        if ( File.Exists( Path.Combine( preferred, "bin", "libHAPIL.dll" ) ) ) return Validate( preferred );
        throw new DirectoryNotFoundException( "Houdini 22.0.514 was not found. Specify --root, DXSHARP_HOUDINI_ROOT or HFS." );
    }

    private static HoudiniInstallation Validate( string root ) {
        var installation = new HoudiniInstallation( Path.GetFullPath( root ) );
        if ( !File.Exists( installation.ClientLibrary ) )
            throw new FileNotFoundException( "HAPI client DLL not found.", installation.ClientLibrary );
        return installation;
    }
}
