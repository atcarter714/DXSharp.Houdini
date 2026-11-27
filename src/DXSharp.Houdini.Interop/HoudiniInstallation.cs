using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace DXSharp.Houdini.Interop;

public sealed record HoudiniInstallation( string Root ) {
    public string BinDirectory => Path.Combine( Root, "bin" );
    public string ClientLibrary => Path.Combine( BinDirectory, "libHAPIL.dll" );
    public string ToolkitDirectory => Path.Combine( Root, "toolkit" );
    public bool HasHdk => File.Exists( Path.Combine( ToolkitDirectory, "cmake", "HoudiniConfig.cmake" ) );
    public Version InstalledVersion {
        get {
            if ( File.Exists( ClientLibrary ) ) {
                var info = FileVersionInfo.GetVersionInfo( ClientLibrary );
                if ( info.FileMajorPart > 0 )
                    return new( info.FileMajorPart, info.FileMinorPart, info.FileBuildPart, info.FilePrivatePart );
            }
            var match = Regex.Match( Path.GetFileName( Path.TrimEndingDirectorySeparator( Root ) ), @"(?<!\d)(\d+\.\d+\.\d+(?:\.\d+)?)(?!\d)" );
            return match.Success && Version.TryParse( match.Value, out var version ) ? version : new( 0, 0, 0, 0 );
        }
    }

    public static HoudiniInstallation Resolve( string? root = null, Version? version = null ) {
        RequireWindowsX64();
        if ( string.IsNullOrWhiteSpace( root ) && version is null ) {
            root = Environment.GetEnvironmentVariable( "DXSHARP_HOUDINI_ROOT" );
            if ( string.IsNullOrWhiteSpace( root ) ) root = Environment.GetEnvironmentVariable( "HFS" );
        }
        if ( !string.IsNullOrWhiteSpace( root ) ) {
            var result = Validate( root );
            if ( version != null && !MatchesVersion( result.InstalledVersion, version ) )
                throw new InvalidOperationException( $"Explicit installation {result.Root} does not match {version}." );
            return result;
        }
        return Discover().FirstOrDefault( i => version == null || MatchesVersion( i.InstalledVersion, version ) )
            ?? throw new DirectoryNotFoundException( "No matching Houdini installation found. Specify --root, DXSHARP_HOUDINI_ROOT or HFS." );
    }

    public static bool MatchesVersion( Version actual, Version requested ) =>
        actual.Major == requested.Major && actual.Minor == requested.Minor &&
        ( requested.Build < 0 || actual.Build == requested.Build ) &&
        ( requested.Revision < 0 || actual.Revision == requested.Revision );

    public static IReadOnlyList<HoudiniInstallation> Discover( IEnumerable<string>? searchRoots = null ) {
        RequireWindowsX64();
        var roots = new List<string>();
        foreach ( string name in new[] { "DXSHARP_HOUDINI_ROOT", "HFS" } )
            if ( Environment.GetEnvironmentVariable( name ) is { Length: > 0 } hint ) roots.Add( hint );
        var bases = ( searchRoots ?? [] ).Append(
            Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.ProgramFiles ), "Side Effects Software" ) );
        foreach ( string directory in bases ) {
            if ( !Directory.Exists( directory ) ) continue;
            roots.Add( directory );
            try { roots.AddRange( Directory.GetDirectories( directory, "Houdini*" ) ); }
            catch ( UnauthorizedAccessException ) { }
            catch ( IOException ) { }
        }
        if ( OperatingSystem.IsWindows() ) roots.AddRange( RegistryRoots() );
        return DiscoverCandidates( roots );
    }

    // Pure directory discovery; does not load any SDK or require Windows.
    public static IReadOnlyList<HoudiniInstallation> DiscoverCandidates( IEnumerable<string> roots ) {
        var found = new Dictionary<string,HoudiniInstallation>( StringComparer.OrdinalIgnoreCase );
        foreach ( string root in roots ) {
            if ( string.IsNullOrWhiteSpace( root ) ) continue;
            try {
                var full = Path.TrimEndingDirectorySeparator( Path.GetFullPath( root ) );
                var install = new HoudiniInstallation( full );
                if ( File.Exists( install.ClientLibrary ) ) found.TryAdd( full, install );
            }
            catch ( ArgumentException ) { }
            catch ( NotSupportedException ) { }
            catch ( IOException ) { }
        }
        return found.Values.OrderByDescending( i => i.InstalledVersion )
            .ThenBy( i => i.Root, StringComparer.OrdinalIgnoreCase ).ToArray();
    }

    [SupportedOSPlatform( "windows" )]
    private static List<string> RegistryRoots() {
        var result = new List<string>();
        foreach ( var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser } ) {
            foreach ( var view in new[] { RegistryView.Registry64, RegistryView.Registry32 } ) {
                try {
                    using var baseKey = RegistryKey.OpenBaseKey( hive, view );
                    using var uninstall = baseKey.OpenSubKey( @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" );
                    if ( uninstall == null ) continue;
                    foreach ( string name in uninstall.GetSubKeyNames() ) {
                        using var entry = uninstall.OpenSubKey( name );
                        if ( entry?.GetValue( "DisplayName" ) is string title &&
                            ( title == "Houdini" || title.StartsWith( "Houdini ", StringComparison.OrdinalIgnoreCase ) ) &&
                            entry.GetValue( "InstallLocation" ) is string { Length: > 0 } location )
                            result.Add( location );
                    }
                }
                catch ( UnauthorizedAccessException ) { }
                catch ( System.Security.SecurityException ) { }
                catch ( IOException ) { }
            }
        }
        return result;
    }

    private static void RequireWindowsX64() {
        if ( !OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 )
            throw new PlatformNotSupportedException( "The initial HAPI client target is Windows x64." );
    }

    private static HoudiniInstallation Validate( string root ) {
        var installation = new HoudiniInstallation( Path.TrimEndingDirectorySeparator( Path.GetFullPath( root ) ) );
        if ( !File.Exists( installation.ClientLibrary ) )
            throw new FileNotFoundException( "HAPI client DLL not found.", installation.ClientLibrary );
        return installation;
    }
}
