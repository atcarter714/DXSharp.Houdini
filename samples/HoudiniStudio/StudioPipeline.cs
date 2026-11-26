using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DXSharp.Houdini.Geometry;

namespace DXSharp.Houdini.Studio;
internal sealed partial class StudioWindow {
    private readonly TextBox _lopPath=new(){PlaceholderText="/stage/OUT"};
    private readonly TextBox _usdPrim=new(){PlaceholderText="/World/mesh"};
    private readonly TextBox _copPath=new(){PlaceholderText="/img/copnet/OUT"};
    private readonly NumberBox _copOutput=new(){Minimum=0,Maximum=255,Value=0,Header="COP output index"};
    private readonly TextBlock _pipelineReport=new(){TextWrapping=TextWrapping.Wrap};
    private static readonly JsonSerializerOptions ReportOptions=new(){WriteIndented=true,IncludeFields=true};
    private StackPanel BuildPipelinePanel() {
        var panel=new StackPanel{Spacing=8,MaxWidth=900,HorizontalAlignment=HorizontalAlignment.Left};
        panel.Children.Add(new TextBlock{Text="SCENE & ASSET PROCESSING",FontSize=20});
        panel.Children.Add(new TextBlock{Text="Inspect a composed Solaris stage, compile a triangulated SOP/USD mesh, or export a cooked COP layer.",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(_lopPath);panel.Children.Add(Button("Inspect Solaris stage",InspectStageAsync));
        panel.Children.Add(_usdPrim);panel.Children.Add(Button("Compile USD mesh",CompileUsdAsync));
        panel.Children.Add(_copPath);panel.Children.Add(_copOutput);panel.Children.Add(Button("Export COP RGBA32F",ExportCopAsync));
        panel.Children.Add(_pipelineReport);return panel;
    }
    private async Task<string?> SaveAssetAsync(string description,string extension,string name) {
        var picker=new Windows.Storage.Pickers.FileSavePicker{SuggestedFileName=name};
        picker.FileTypeChoices.Add(description,new List<string>{extension});
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
        return (await picker.PickSaveFileAsync())?.Path;
    }
    private async Task CompileSopAsync() {
        string path=Selected??throw new InvalidOperationException("Select a triangulated SOP.");
        string? output=await SaveAssetAsync("DXSharp mesh blob",".dxam","mesh");
        if(output==null)return;
        var data=await Request(new{action="compile_sop",path});
        await SaveCompiledAsync(data,output);
    }
    private async Task CompileUsdAsync() {
        string? output=await SaveAssetAsync("DXSharp mesh blob",".dxam","usd-mesh");
        if(output==null)return;
        var data=await Request(new{action="compile_usd_mesh",path=_lopPath.Text.Trim(),prim=_usdPrim.Text.Trim()});
        await SaveCompiledAsync(data,output);
    }
    private async Task SaveCompiledAsync(JsonElement data,string output) {
        var mesh=await Task.Run(()=>SceneJson.CompileMesh(data),_lifetime.Token);
        await Task.Run(()=>MeshBlob.Write(output,mesh),_lifetime.Token);
        // Keep source transform/units external to the local-coordinate mesh blob.
        var metadata=new Dictionary<string,JsonElement>();
        foreach(string key in new[]{"path","localToWorld","upAxis","metersPerUnit"})
            if(data.TryGetProperty(key,out var value))metadata.Add(key,value);
        await File.WriteAllTextAsync(output+".json",JsonSerializer.Serialize(metadata,ReportOptions),_lifetime.Token);
        _pipelineReport.Text=_status.Text=$"Compiled {mesh.Vertices.Length:N0} vertices, {mesh.Indices.Length:N0} indices, {mesh.Ranges.Length} material ranges → {output}";
    }
    private async Task InspectStageAsync() {
        var data=await Request(new{action="inspect_stage",path=_lopPath.Text.Trim()});
        var mats=SceneJson.TranslateMaterials(data);
        _pipelineReport.Text=$"{data.GetProperty("objects").GetArrayLength():N0} prims · {mats.Length} materials · {mats.Sum(m=>m.Diagnostics.Length)} translation diagnostics";
        string? file=await SaveAssetAsync("Solaris scene report",".json","stage-report");
        if(file!=null)await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{stage=data,translatedMaterials=mats},ReportOptions),_lifetime.Token);
    }
    private async Task ExportCopAsync() {
        string? file=await SaveAssetAsync("Raw RGBA float texture",".rgba32f","cop-layer");
        if(file==null)return;
        var data=await Request(new{action="export_cop",path=_copPath.Text.Trim(),output=(int)_copOutput.Value,file});
        await File.WriteAllTextAsync(file+".json",JsonSerializer.Serialize(data,ReportOptions),_lifetime.Token);
        _pipelineReport.Text="Exported COP layer: "+file+"\n"+JsonSerializer.Serialize(data,ReportOptions);
    }
}
