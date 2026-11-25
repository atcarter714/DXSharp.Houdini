using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DXSharp.Houdini.Interop;

// HAPI_GetEnvInt and its version selector values are stable since HAPI 2.0.
// Keep the loaded native client alive for the process; unloading a Houdini runtime
// after native initialization is not part of this contract.
public static unsafe partial class HapiClient {
    private static readonly object Gate = new();
    private static nint _module;
    private static string? _path;

    [LibraryImport( "kernel32.dll", EntryPoint = "LoadLibraryExW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true )]
    private static partial nint LoadLibraryEx( string fileName, nint file, uint flags );

    public static HapiVersion Probe( HoudiniInstallation installation ) {
        ArgumentNullException.ThrowIfNull( installation );
        if ( !OperatingSystem.IsWindows() || RuntimeInformation.ProcessArchitecture != Architecture.X64 )
            throw new PlatformNotSupportedException( "HAPI probing requires Windows x64." );

        lock ( Gate ) {
            string path = Path.GetFullPath( installation.ClientLibrary );
            if ( _module == 0 ) {
                // Search the DLL's own directory and trusted default directories
                // for its dependencies rather than changing the process PATH.
                _module = LoadLibraryEx( path, 0, 0x00000100 | 0x00001000 );
                if ( _module == 0 )
                    throw new Win32Exception( Marshal.GetLastPInvokeError(), $"Unable to load {path}" );
                _path = path;
            }
            else if ( !StringComparer.OrdinalIgnoreCase.Equals( _path, path ) )
                throw new InvalidOperationException( "A different Houdini client is already loaded. Use a fresh process." );

            var get = (delegate* unmanaged[Cdecl]<int, int*, int>)NativeLibrary.GetExport( _module, "HAPI_GetEnvInt" );
            return new( Read( get, 100 ), Read( get, 110 ), Read( get, 120 ), Read( get, 130 ),
                Read( get, 200 ), Read( get, 210 ), Read( get, 220 ) );
        }
    }

    private static int Read( delegate* unmanaged[Cdecl]<int, int*, int> get, int selector ) {
        int value = 0;
        int result = get( selector, &value );
        if ( result != 0 ) throw new InvalidOperationException( $"HAPI_GetEnvInt({selector}) returned {result}." );
        return value;
    }
}
