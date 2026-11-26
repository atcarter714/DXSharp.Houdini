using System.Text.Json;
using System.Numerics;
using Windows.Graphics.Imaging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using DXSharp.Houdini.Geometry;

namespace DXSharp.Houdini.Studio;
internal sealed partial class StudioWindow {
    private readonly TextBox _lopPath=new(){PlaceholderText="/stage/OUT"};
    private readonly TextBox _usdPrim=new(){PlaceholderText="/World/mesh"};
    private readonly TextBox _copPath=new(){PlaceholderText="/img/copnet/OUT"};
    private readonly NumberBox _copOutput=new(){Minimum=0,Maximum=255,Value=0,Header="COP output index"};
    private readonly ComboBox _materials=new(){PlaceholderText="Choose translated material",DisplayMemberPath="Path"};
    private readonly CheckBox _textureSrgb=new(){Content="Decode texture as sRGB",IsChecked=true};
    private readonly CheckBox _flipV=new(){Content="Flip texture V"};
    private TexturePayload? _texturePreview;
    private Vector3 _materialTint=Vector3.One,_materialEmission=Vector3.Zero;
    private readonly TextBlock _pipelineReport=new(){TextWrapping=TextWrapping.Wrap};
    private static readonly JsonSerializerOptions ReportOptions=new(){WriteIndented=true,IncludeFields=true};
    private StackPanel BuildPipelinePanel() {
        var panel=new StackPanel{Spacing=8,MaxWidth=900,HorizontalAlignment=HorizontalAlignment.Left};
        panel.Children.Add(new TextBlock{Text="SCENE & ASSET PROCESSING",FontSize=20});
        panel.Children.Add(new TextBlock{Text="Inspect a composed Solaris stage, compile a triangulated SOP/USD mesh, or export a cooked COP layer.",TextWrapping=TextWrapping.Wrap});
        panel.Children.Add(_lopPath);panel.Children.Add(Button("Inspect Solaris stage",InspectStageAsync));
        panel.Children.Add(_usdPrim);panel.Children.Add(Button("Compile USD mesh",CompileUsdAsync));
        panel.Children.Add(Button("Load DXAM preview",LoadCompiledAsync));
        panel.Children.Add(new TextBlock{Text="Single-material GPU preview",FontSize=16});
        panel.Children.Add(_materials);
        panel.Children.Add(_textureSrgb);panel.Children.Add(_flipV);
        _flipV.Checked+=(_,_)=>Draw();_flipV.Unchecked+=(_,_)=>Draw();
        panel.Children.Add(Button("Preview selected material",PreviewMaterialAsync));
        panel.Children.Add(Button("Load base-color texture",LoadTextureAsync));
        panel.Children.Add(Button("Clear material / texture",()=>{_texturePreview=null;_materialTint=Vector3.One;_materialEmission=Vector3.Zero;Draw();return Task.CompletedTask;}));
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
        _materials.ItemsSource=mats;
        _pipelineReport.Text=$"{data.GetProperty("objects").GetArrayLength():N0} prims · {mats.Length} materials · {mats.Sum(m=>m.Diagnostics.Length)} translation diagnostics";
        string? file=await SaveAssetAsync("Solaris scene report",".json","stage-report");
        if(file!=null)await File.WriteAllTextAsync(file,JsonSerializer.Serialize(new{stage=data,translatedMaterials=mats},ReportOptions),_lifetime.Token);
    }
    private async Task<string?> OpenAssetAsync(params string[] extensions) {
        var picker=new Windows.Storage.Pickers.FileOpenPicker();
        foreach(string extension in extensions)picker.FileTypeFilter.Add(extension);
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
        return (await picker.PickSingleFileAsync())?.Path;
    }
    private async Task LoadCompiledAsync() {
        string? path=await OpenAssetAsync(".dxam");
        if(path==null)return;
        var mesh=await Task.Run(()=>MeshBlob.Read(path),_lifetime.Token);
        _live.IsOn=false;DetachView();
        _points=mesh.Vertices.Select(v=>v.Position).ToArray();
        _normals=mesh.Vertices.Select(v=>v.Normal).ToArray();_uvs=mesh.Vertices.Select(v=>v.UV).ToArray();
        _colors=mesh.Vertices.Select(v=>new Vector3(v.Color.X,v.Color.Y,v.Color.Z)).ToArray();
        _triangles=mesh.Indices.Select(i=>checked((int)i)).ToArray();
        var edges=new HashSet<(int,int)>();
        for(int i=0;i<_triangles.Length;i+=3)for(int j=0;j<3;j++){
            int a=_triangles[i+j],b=_triangles[i+(j+1)%3];edges.Add((Math.Min(a,b),Math.Max(a,b)));
        }
        _edges=edges.ToArray();_compiledPreview=mesh;_compiledPoints=_points;
        _shading.SelectedIndex=1;_backend.SelectedIndex=0;
        _pipelineReport.Text=_status.Text=$"Offline DXAM preview: {mesh.Vertices.Length:N0} vertices; reselect a SOP to return to live data.";Draw();
    }
    private async Task<TexturePayload> ReadTextureAsync(string path,bool srgb) {
        if(!Path.IsPathFullyQualified(path)||!File.Exists(path))
            throw new InvalidDataException("Only resolved local image files are supported by this preview.");
        if(path.EndsWith(".rgba32f",StringComparison.OrdinalIgnoreCase))
            return await Task.Run(()=>TexturePayload.ReadCop(path,srgb),_lifetime.Token);
        var file=await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
        using var stream=await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
        var decoder=await BitmapDecoder.CreateAsync(stream);
        if(decoder.PixelWidth>2048||decoder.PixelHeight>2048)throw new InvalidDataException("Preview texture limit: 2048 × 2048.");
        var pixels=await decoder.GetPixelDataAsync(BitmapPixelFormat.Rgba8,BitmapAlphaMode.Straight,new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);
        var result=new TexturePayload((int)decoder.PixelWidth,(int)decoder.PixelHeight,TextureEncoding.Rgba8,pixels.DetachPixelData(),srgb);
        result.Validate();return result;
    }
    private async Task LoadTextureAsync() {
        string? path=await OpenAssetAsync(".png",".jpg",".jpeg",".bmp",".tif",".tiff",".rgba32f");
        if(path==null)return;
        _texturePreview=await ReadTextureAsync(path,_textureSrgb.IsChecked==true);
        _pipelineReport.Text=$"Base-color preview: {_texturePreview.Width} × {_texturePreview.Height}";Draw();
    }
    private async Task PreviewMaterialAsync() {
        var m=_materials.SelectedItem as RuntimeMaterial??throw new InvalidOperationException("Inspect a stage and choose a material first.");
        TexturePayload? texture=null;
        if(m.Textures.TryGetValue("baseColor",out var binding)&&binding.Texture is {} a) {
            string path=a.ResolvedPath??throw new InvalidOperationException("Base-color reference is unresolved; export or bake it first.");
            bool srgb=a.ColorSpace?.Equals("sRGB",StringComparison.OrdinalIgnoreCase)==true||
                (a.ColorSpace is null or "auto"&&_textureSrgb.IsChecked==true);
            texture=await ReadTextureAsync(path,srgb);
        }
        _texturePreview=texture;
        _materialTint=new(m.BaseColor.X,m.BaseColor.Y,m.BaseColor.Z);_materialEmission=m.Emission;
        _roughness.Value=m.Roughness;_metallic.Value=m.Metallic;
        _pipelineReport.Text="Preview material: "+m.Path+" (single material override)\n"+string.Join("\n",m.Diagnostics);Draw();
    }
    private async Task ExportCopAsync() {
        string? file=await SaveAssetAsync("Raw RGBA float texture",".rgba32f","cop-layer");
        if(file==null)return;
        var data=await Request(new{action="export_cop",path=_copPath.Text.Trim(),output=(int)_copOutput.Value,file});
        await File.WriteAllTextAsync(file+".json",JsonSerializer.Serialize(data,ReportOptions),_lifetime.Token);
        _pipelineReport.Text="Exported COP layer: "+file+"\n"+JsonSerializer.Serialize(data,ReportOptions);
    }
}
