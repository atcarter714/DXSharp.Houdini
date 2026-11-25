using System.Text.Json;
namespace DXSharp.Houdini.Studio;

internal sealed class BridgeClient( string directory ) {
    private readonly string _directory = Path.GetFullPath( directory );
    public async Task<JsonElement> RequestAsync( object request, CancellationToken token ) {
        if ( !Directory.Exists( _directory ) ) throw new DirectoryNotFoundException( _directory );
        string key = Guid.NewGuid().ToString( "N" );
        string input = Path.Combine( _directory, key + ".request.json" );
        string output = Path.Combine( _directory, key + ".response.json" );
        string staging = input + ".tmp";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource( token );
        timeout.CancelAfter( TimeSpan.FromSeconds( 60 ) );
        try {
            await File.WriteAllTextAsync( staging, JsonSerializer.Serialize( request ), timeout.Token );
            File.Move( staging, input );
            while ( !File.Exists( output ) ) await Task.Delay( 100, timeout.Token );
            using var doc = JsonDocument.Parse( await File.ReadAllTextAsync( output, timeout.Token ) );
            var root = doc.RootElement;
            if ( !root.GetProperty( "ok" ).GetBoolean() )
                throw new InvalidOperationException( root.GetProperty( "error" ).GetString() );
            return root.GetProperty( "data" ).Clone();
        }
        finally {
            File.Delete( staging );
            File.Delete( input );
            File.Delete( output );
        }
    }
}
