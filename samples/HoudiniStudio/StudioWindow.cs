using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI;
using System.Numerics;
using System.Globalization;
using System.Text.Json;

namespace DXSharp.Houdini.Studio;

internal sealed class StudioWindow : Window {
    private sealed record SessionEntry(string Directory, int Pid, string Version, string Scene) {
        public override string ToString() => $"Houdini {Version} · PID {Pid} · {System.IO.Path.GetFileName(Scene)}";
    }
    private readonly ComboBox _sessions = new() { PlaceholderText = "Discover Houdini sessions", MinWidth = 220 };
    private readonly PreviewRasterizer _raster = new();
    private readonly Image _preview = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly ComboBox _shading = new() { ItemsSource = new[] { "Wireframe", "Shaded", "Shaded + edges" }, SelectedIndex = 2, MinWidth = 140 };
    private readonly ComboBox _operators = new() { PlaceholderText = "Select SOP operator", MaxWidth = 245 };
    private readonly TextBox _operatorFilter = new() { PlaceholderText = "Search operator types..." };
    private readonly ComboBox _tools = new() { PlaceholderText = "SideFX HDA tools", MaxWidth = 245 };
    private readonly TextBlock _attributeText = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
    private readonly List<OperatorEntry> _operatorList = [];
    private int[] _triangles = [];
    private Matrix4x4? _syncedRotation;
    private bool _drawPending;
    private sealed record OperatorEntry(string Name, string Label, int Inputs) {
        public override string ToString() => $"{Label} · {Name}";
    }
    private sealed record ToolEntry(string Name, string File) {
        public override string ToString() => Name;
    }
    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(ColorHelper.FromArgb(255,r,g,b));
    private readonly TextBox _filter = new() { PlaceholderText = "Filter SOP paths..." };
    private readonly TextBox _file = new() { PlaceholderText = "Absolute .bgeo or .bgeo.sc path", MinWidth = 280 };
    private readonly ToggleSwitch _live = new() { Header = "Live update", IsOn = true };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private string[] _allNodes = [];
    private int _revision = -1;
    private string? _previewPath;
    private Windows.Foundation.Point? _drag;
    private bool _panDrag;
    private double _panX, _panY;
    private readonly TextBox _directory = new() { PlaceholderText = "Paste bridge directory from Houdini Python Shell", MinWidth = 180 };
    private readonly TextBlock _status = new() { Text = "Disconnected", TextWrapping = TextWrapping.Wrap };
    private readonly ListView _nodes = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly StackPanel _parameters = new() { Spacing = 6 };
    private readonly Canvas _viewport = new() { Background = new SolidColorBrush( ColorHelper.FromArgb(255, 18, 23, 31) ) };
    private readonly Slider _yaw = new() { Minimum = -180, Maximum = 180, Value = 35, Header = "Orbit yaw" };
    private readonly Slider _pitch = new() { Minimum = -89, Maximum = 89, Value = 25, Header = "Orbit pitch" };
    private readonly Slider _zoom = new() { Minimum = .2, Maximum = 10, Value = 1, StepFrequency = .05, Header = "Zoom" };
    private readonly StackPanel _controls = new() { Spacing = 8 };
    private readonly CancellationTokenSource _lifetime = new();
    private BridgeClient? _bridge;
    private bool _busy;
    private Vector3[] _points = [];
    private (int A, int B)[] _edges = [];
    private string? Selected => _nodes.SelectedItem as string;

    public StudioWindow() {
        Title = "DXSharp Houdini Studio | Live SOP Explorer";
        var root = new Grid { Padding = new Thickness(12), RowSpacing = 10, ColumnSpacing = 10, Background = Brush(15,22,32), RequestedTheme = ElementTheme.Dark };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(285) });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(330) });
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock { Text = "DXSHARP / HOUDINI", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(105,207,219), VerticalAlignment = VerticalAlignment.Center, FontSize = 16 });
        header.Children.Add(_sessions);
        header.Children.Add(Button("Discover", DiscoverAsync));
        var advanced = new Flyout { Content = _directory };
        header.Children.Add(new Button { Content = "⚙", Flyout = advanced });
        _sessions.SelectionChanged += (_, _) => {
            if (_sessions.SelectedItem is SessionEntry session) _directory.Text = session.Directory;
        };
        header.Children.Add(Button("Connect", ConnectAsync));
        header.Children.Add(Button("Refresh nodes", RefreshAsync));
        Grid.SetColumnSpan(header, 3);
        root.Children.Add(header);
        _controls.Children.Add(new TextBlock { Text = "SCENE EXPLORER", FontSize = 17 });
        _controls.Children.Add(_filter);
        _filter.TextChanged += (_, _) => FilterNodes();
        var create = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (string kind in new[] {"box", "sphere", "torus"})
            create.Children.Add(Button(kind, () => CreateAsync(kind)));
        _controls.Children.Add(create);
        _controls.Children.Add(new TextBlock { Text = "OPERATOR LIBRARY", Foreground = Brush(104,196,207), FontSize = 12 });
        _controls.Children.Add(_operatorFilter);
        _operatorFilter.TextChanged += (_, _) => FilterOperators();
        _controls.Children.Add(_operators);
        _controls.Children.Add(Button("＋ Add operator", AddOperatorAsync));
        _controls.Children.Add(_tools);
        _controls.Children.Add(Button("↓ Install selected HDA", InstallToolAsync));
        _controls.Children.Add(Button("↻ Load operator catalog", LoadCatalogAsync));
        var left = new Grid { RowSpacing = 8 };
        left.RowDefinitions.Add(new() { Height = GridLength.Auto });
        left.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        left.Children.Add(_controls);
        Grid.SetRow(_nodes, 1); left.Children.Add(_nodes);
        var leftPanel = new Border { Background = Brush(25,35,47), CornerRadius = new CornerRadius(8), Child = left, Padding = new Thickness(10) };
        Grid.SetRow(leftPanel, 1); root.Children.Add(leftPanel);
        var center = new Grid { RowSpacing = 8 };
        center.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        center.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _viewport.Children.Add(_preview);
        center.Children.Add(_viewport);
        var camera = new StackPanel { Spacing = 4 };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tools.Children.Add(_shading);
        _shading.SelectionChanged += (_, _) => Draw();
        tools.Children.Add(Button("◎ Frame", () => { _panX = _panY = 0; _zoom.Value = 1; Draw(); return Task.CompletedTask; }));
        tools.Children.Add(Button("↻ Cook", RecookAsync));
        tools.Children.Add(Button("⇄ Match view", MatchViewAsync));
        tools.Children.Add(_live);
        camera.Children.Add(tools);
        camera.Children.Add(new TextBlock { Text = "Left drag: orbit   |   Middle/right drag: pan   |   Wheel: zoom" });
        var io = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        io.Children.Add(_file);
        io.Children.Add(Button("Import BGEO", ImportAsync));
        io.Children.Add(Button("Export BGEO", ExportAsync));
        camera.Children.Add(io);
        Grid.SetRow(camera, 1); center.Children.Add(camera);
        Grid.SetColumn(center, 1); Grid.SetRow(center, 1); root.Children.Add(center);
        var inspector = new StackPanel { Spacing = 8 };
        inspector.Children.Add(new TextBlock { Text = "PARAMETERS", FontSize = 17 });
        inspector.Children.Add(Button("Read geometry / parameters", InspectAsync));
        inspector.Children.Add(_parameters);
        var scroll = new ScrollViewer { Content = inspector };
        var tabs = new Pivot();
        tabs.Items.Add(new PivotItem { Header = "Parameters", Content = scroll });
        var attrs = new StackPanel { Spacing = 8, Padding = new Thickness(8) };
        attrs.Children.Add(Button("↻ Read attributes", ReadAttributesAsync));
        attrs.Children.Add(_attributeText);
        tabs.Items.Add(new PivotItem { Header = "Attributes", Content = new ScrollViewer { Content = attrs } });
        var rightPanel = new Border { Background = Brush(25,35,47), CornerRadius = new CornerRadius(8), Child = tabs, Padding = new Thickness(6) };
        Grid.SetColumn(rightPanel, 2); Grid.SetRow(rightPanel, 1); root.Children.Add(rightPanel);
        Grid.SetRow(_status, 2); Grid.SetColumnSpan(_status, 3); root.Children.Add(_status);
        Content = root;
        _viewport.SizeChanged += (_, _) => Draw();
        _yaw.ValueChanged += (_, _) => Draw();
        _pitch.ValueChanged += (_, _) => Draw();
        _zoom.ValueChanged += (_, _) => Draw();
        _nodes.SelectionChanged += async (_, _) => { if (!_busy && Selected != null) await RunAsync(InspectAsync); };
        _viewport.PointerPressed += (_, e) => {
            var p = e.GetCurrentPoint(_viewport);
            _drag = p.Position;
            _panDrag = p.Properties.IsMiddleButtonPressed || p.Properties.IsRightButtonPressed;
            _viewport.CapturePointer(e.Pointer);
            e.Handled = true;
        };
        _viewport.PointerMoved += (_, e) => {
            if (_drag is not Windows.Foundation.Point previous) return;
            var p = e.GetCurrentPoint(_viewport).Position;
            double dx = p.X - previous.X, dy = p.Y - previous.Y;
            _drag = p;
            if (_panDrag) { _panX += dx; _panY += dy; Draw(); }
            else {
                _syncedRotation = null;
                _yaw.Value = ((_yaw.Value + dx * .5 + 180) % 360 + 360) % 360 - 180;
                _pitch.Value = Math.Clamp(_pitch.Value + dy * .5, -89, 89);
            }
        };
        _viewport.PointerReleased += (_, e) => { _drag = null; _viewport.ReleasePointerCapture(e.Pointer); };
        _viewport.PointerCaptureLost += (_, _) => _drag = null;
        _viewport.PointerWheelChanged += (_, e) => {
            _zoom.Value = Math.Clamp(_zoom.Value * Math.Pow(1.12, e.GetCurrentPoint(_viewport).Properties.MouseWheelDelta / 120.0), _zoom.Minimum, _zoom.Maximum);
            e.Handled = true;
        };
        _timer.Tick += async (_, _) => {
            if (_live.IsOn && _bridge != null && Selected != null && !_busy)
                await RunAsync(CheckRevisionAsync);
        };
        _timer.Start();
        Closed += (_, _) => { _timer.Stop(); _lifetime.Cancel(); };
        root.Loaded += async (_, _) => await RunAsync(async () => {
            await DiscoverAsync();
            if (_sessions.Items.Count == 1) await ConnectAsync();
        });
    }

    private Button Button(string label, Func<Task> action) {
        var b = new Button { Content = label, Background = Brush(38,64,80), Foreground = Brush(226,245,249), CornerRadius = new CornerRadius(5), Padding = new Thickness(10,6,10,6) };
        b.Click += async (_, _) => await RunAsync(action);
        return b;
    }
    private async Task RunAsync(Func<Task> action) {
        if (_busy) return;
        _busy = true;
        try { await action(); }
        catch (Exception e) { _status.Text = e.Message; }
        finally { _busy = false; }
    }
    private Task<JsonElement> Request(object request) =>
        (_bridge ?? throw new InvalidOperationException("Connect to Houdini first.")).RequestAsync(request, _lifetime.Token);
    private Task DiscoverAsync() {
        var sessions = new List<SessionEntry>();
        foreach (string directory in Directory.EnumerateDirectories(System.IO.Path.GetTempPath(), "dxsharp-houdini-*")) {
            try {
                using var manifest = JsonDocument.Parse(File.ReadAllText(System.IO.Path.Combine(directory, "session.json")));
                var data = manifest.RootElement;
                if (data.GetProperty("protocol").GetInt32() != 1) continue;
                int pid = data.GetProperty("pid").GetInt32();
                using var process = System.Diagnostics.Process.GetProcessById(pid);
                if (process.HasExited || !process.ProcessName.StartsWith("houdini", StringComparison.OrdinalIgnoreCase)) continue;
                sessions.Add(new(directory, pid, data.GetProperty("version").GetString()!, data.GetProperty("scene").GetString()!));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or KeyNotFoundException) { }
        }
        _sessions.ItemsSource = sessions.OrderBy(x => x.Pid).ToArray();
        if (sessions.Count == 1) _sessions.SelectedIndex = 0;
        _status.Text = sessions.Count == 0 ? "No active bridge. Connect from the DXSharp shelf in Houdini." : $"{sessions.Count} Houdini session(s) found.";
        return Task.CompletedTask;
    }
    private async Task ConnectAsync() {
        if (string.IsNullOrWhiteSpace(_directory.Text))
            throw new InvalidOperationException("Start the Houdini bridge, then Discover and select a session.");
        _bridge = null;
        var candidate = new BridgeClient(_directory.Text.Trim());
        var result = await candidate.RequestAsync(new { action = "ping" }, _lifetime.Token);
        _bridge = candidate;
        _revision = -1;
        _previewPath = null;
        _status.Text = $"Connected: Houdini {result.GetProperty("version").GetString()} | {result.GetProperty("scene").GetString()}";
        await RefreshAsync();
    }
    private async Task RefreshAsync() {
        string? selected = Selected;
        var result = await Request(new { action = "nodes" });
        _allNodes = result.EnumerateArray().Select(x => x.GetString()!).ToArray();
        FilterNodes();
        _nodes.SelectedItem = selected;
    }
    private void FilterNodes() {
        string? selected = Selected;
        _nodes.ItemsSource = _allNodes.Where(x => x.Contains(_filter.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        _nodes.SelectedItem = selected;
    }
    private async Task CheckRevisionAsync() {
        string path = Selected!;
        if (_parameters.XamlRoot is not null && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(_parameters.XamlRoot) is DependencyObject focused) {
            for (DependencyObject? parent=focused;parent!=null;parent=VisualTreeHelper.GetParent(parent)) if (ReferenceEquals(parent,_parameters)) return;
        }
        var result = await Request(new { action = "revision", path });
        if (Selected == path && (_previewPath != path || _revision != result.GetProperty("cook").GetInt32()))
            await InspectAsync();
    }
    private async Task RecookAsync() {
        string path = Selected ?? throw new InvalidOperationException("Select a SOP first.");
        await Request(new { action = "recook", path });
        await InspectAsync();
    }
    private string GeometryFile() {
        string file = _file.Text.Trim().Trim('"');
        if (!System.IO.Path.IsPathFullyQualified(file)) throw new InvalidOperationException("Enter an absolute BGEO file path.");
        return file;
    }
    private async Task ImportAsync() {
        var result = await Request(new { action = "import", file = GeometryFile() });
        _filter.Text = "";
        await RefreshAsync();
        _nodes.SelectedItem = result.GetString();
        await InspectAsync();
    }
    private async Task ExportAsync() {
        string path = Selected ?? throw new InvalidOperationException("Select a SOP first.");
        var result = await Request(new { action = "export", path, file = GeometryFile() });
        _status.Text = $"Exported {result.GetString()}";
    }
    private async Task CreateAsync(string kind) {
        var result = await Request(new { action = "create", kind });
        _filter.Text = "";
        await RefreshAsync();
        _nodes.SelectedItem = result.GetString();
        await InspectAsync();
    }
    private async Task InspectAsync() {
        string path = Selected ?? throw new InvalidOperationException("Select a SOP first.");
        var data = await Request(new { action = "geometry", path });
        if (Selected != path) return;
        _revision = data.TryGetProperty("revision", out var rev) ? rev.GetInt32() : -1;
        _previewPath = path;
        _points = data.GetProperty("positions").EnumerateArray().Select(p =>
            new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle())).ToArray();
        _triangles = data.TryGetProperty("triangles", out var triangles) ? triangles.EnumerateArray().Select(x => x.GetInt32()).ToArray() : [];
        _edges = data.GetProperty("edges").EnumerateArray().Select(e => (e[0].GetInt32(), e[1].GetInt32())).ToArray();
        Draw();
        int sourcePoints = data.TryGetProperty("sourcePoints", out var source) ? source.GetInt32() : _points.Length;
        int unsupported = data.TryGetProperty("unsupported", out var skipped) ? skipped.GetInt32() : 0;
        _status.Text = $"{path} | {sourcePoints:N0} source points | {data.GetProperty("primitives").GetInt32():N0} primitives | {_edges.Length:N0} preview edges | Cook {_revision}" +
            (unsupported > 0 ? $" | {unsupported} unsupported primitives" : "");
        var parms = await Request(new { action = "tuples", path });
        if (Selected != path) return;
        _parameters.Children.Clear();
        foreach (var p in parms.EnumerateArray()) AddParameter(path,p);
    }

    private void AddParameter(string path, JsonElement p) {
        string name=p.GetProperty("name").GetString()!, label=p.GetProperty("label").GetString()!;
        string kind=p.GetProperty("kind").GetString()!;
        var group=new StackPanel { Spacing=5, Margin=new Thickness(0,4,0,6) };
        group.Children.Add(new TextBlock { Text=label, Foreground=Brush(194,222,232), FontWeight=Microsoft.UI.Text.FontWeights.SemiBold });
        var values=p.GetProperty("values").EnumerateArray().ToArray();
        var readers=new List<Func<object>>();
        var keyReaders=new List<Func<double>>();
        string[] basis=[];
        if(kind=="ramp") {
            var keys=p.GetProperty("keys").EnumerateArray().ToArray();
            basis=p.GetProperty("basis").EnumerateArray().Select(x=>x.GetString()!).ToArray();
            for(int i=0;i<keys.Length;i++) {
                var row=new StackPanel { Orientation=Orientation.Horizontal, Spacing=4 };
                var key=new NumberBox { Value=keys[i].GetDouble(), Minimum=0, Maximum=1, Width=90 };
                keyReaders.Add(()=>key.Value); row.Children.Add(key);
                if(values[i].ValueKind==JsonValueKind.Array) {
                    var components=values[i].EnumerateArray().Select(x=>new NumberBox { Value=x.GetDouble(), Width=65 }).ToArray();
                    foreach(var component in components) row.Children.Add(component);
                    readers.Add(()=>components.Select(x=>x.Value).ToArray());
                } else {
                    var value=new NumberBox { Value=values[i].GetDouble(), Width=110 };
                    row.Children.Add(value); readers.Add(()=>value.Value);
                }
                group.Children.Add(row);
            }
            group.Children.Add(new TextBlock { Text="Existing ramp keys · interpolation preserved", FontSize=11, Foreground=Brush(140,164,179) });
        } else if(kind=="toggle") {
            var toggle=new ToggleSwitch { IsOn=values[0].GetInt32()!=0 };
            group.Children.Add(toggle); readers.Add(()=>toggle.IsOn?1:0);
        } else if(p.TryGetProperty("labels",out var labels) && labels.GetArrayLength()>0 && values.Length==1) {
            var choices=p.GetProperty("choices").EnumerateArray().Select(x=>x.GetString()!).ToArray();
            var combo=new ComboBox { ItemsSource=labels.EnumerateArray().Select(x=>x.GetString()).ToArray(), HorizontalAlignment=HorizontalAlignment.Stretch };
            combo.SelectedIndex=kind=="string"?Array.IndexOf(choices,values[0].GetString()):values[0].GetInt32();
            group.Children.Add(combo);
            readers.Add(()=>combo.SelectedIndex<0?throw new InvalidOperationException("Choose a menu value."):kind=="string"?choices[combo.SelectedIndex]:(object)combo.SelectedIndex);
        } else if(kind=="string") {
            foreach(var v in values) {
                var input=new TextBox { Text=v.GetString()??"" };
                group.Children.Add(input); readers.Add(()=>input.Text);
            }
        } else {
            var fields=new Grid { ColumnSpacing=4 };
            var controls=new List<NumberBox>();
            for(int i=0;i<values.Length;i++) {
                fields.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
                var input=new NumberBox { Value=values[i].GetDouble(), SmallChange=kind=="int"?1:.1, MinWidth=0,
                    Header=values.Length>1?(kind=="color"?"RGBA"[Math.Min(i,3)].ToString():i<4?"XYZW"[i].ToString():i.ToString()):null };
                controls.Add(input); Grid.SetColumn(input,i); fields.Children.Add(input);
                readers.Add(()=>kind=="int"?(object)checked((int)input.Value):input.Value);
            }
            group.Children.Add(fields);
            if(kind=="color" && controls.Count>=3) {
                byte Channel(double x)=>(byte)Math.Clamp(Math.Round(x*255),0,255);
                var picker=new ColorPicker { Color=ColorHelper.FromArgb(255,Channel(controls[0].Value),Channel(controls[1].Value),Channel(controls[2].Value)), IsAlphaEnabled=false, IsColorChannelTextInputVisible=false };
                picker.ColorChanged+=(_,e)=>{controls[0].Value=e.NewColor.R/255.0;controls[1].Value=e.NewColor.G/255.0;controls[2].Value=e.NewColor.B/255.0;};
                group.Children.Add(new Button { Content="Color picker", Flyout=new Flyout { Content=picker } });
            }
        }
        var apply=Button("✓ Apply", async ()=>{
            if(Selected!=path) throw new InvalidOperationException("Selection changed.");
            var data=readers.Select(x=>x()).ToArray();
            if(kind=="ramp") await Request(new { action="set_tuple", path, name, values=data, keys=keyReaders.Select(x=>x()).ToArray(), basis });
            else await Request(new { action="set_tuple", path, name, values=data });
            await InspectAsync();
        });
        apply.Background=Brush(19,112,123);
        apply.HorizontalAlignment=HorizontalAlignment.Stretch;
        group.Children.Add(apply);
        _parameters.Children.Add(new Border { Background=Brush(31,46,59), CornerRadius=new CornerRadius(6), Padding=new Thickness(8), Child=group });
    }
    private void FilterOperators() {
        _operators.ItemsSource=_operatorList.Where(x=>x.Name.Contains(_operatorFilter.Text,StringComparison.OrdinalIgnoreCase)||x.Label.Contains(_operatorFilter.Text,StringComparison.OrdinalIgnoreCase)).Take(500).ToArray();
    }
    private async Task LoadCatalogAsync() {
        var catalog=await Request(new { action="operators" });
        _operatorList.Clear();
        foreach(var x in catalog.EnumerateArray()) _operatorList.Add(new(x.GetProperty("name").GetString()!,x.GetProperty("label").GetString()!,x.GetProperty("inputs").GetInt32()));
        FilterOperators();
        var tools=await Request(new { action="tools" });
        _tools.ItemsSource=tools.EnumerateArray().Select(x=>new ToolEntry(x.GetProperty("name").GetString()!,x.GetProperty("file").GetString()!)).ToArray();
        _status.Text=$"{_operatorList.Count:N0} SOP types · search to narrow the catalog";
    }
    private async Task AddOperatorAsync() {
        var op=_operators.SelectedItem as OperatorEntry??throw new InvalidOperationException("Select an operator.");
        if(op.Inputs>1) throw new InvalidOperationException("This operator requires multiple inputs. Wire it in Houdini first.");
        if(op.Inputs>0 && Selected==null) throw new InvalidOperationException("Select a source SOP.");
        var result=await Request(new { action="operator", kind=op.Name, path=Selected });
        _filter.Text=""; await RefreshAsync(); _nodes.SelectedItem=result.GetString(); await InspectAsync();
    }
    private async Task InstallToolAsync() {
        var tool=_tools.SelectedItem as ToolEntry??throw new InvalidOperationException("Select a SideFX tool.");
        var result=await Request(new { action="install_hda", file=tool.File });
        await LoadCatalogAsync();
        _status.Text="Installed: "+string.Join(", ",result.EnumerateArray().Select(x=>x.GetString()));
    }
    private async Task ReadAttributesAsync() {
        string path=Selected??throw new InvalidOperationException("Select a SOP first.");
        var data=await Request(new { action="attributes",path });
        _attributeText.Text=string.Join("\n\n",data.EnumerateArray().Select(x=>$"{x.GetProperty("owner").GetString()} / {x.GetProperty("name").GetString()} [{x.GetProperty("type").GetString()} × {x.GetProperty("size").GetInt32()}]\n"+string.Join("\n",x.GetProperty("values").EnumerateArray().Select(v=>v.GetString()))));
    }
    private async Task MatchViewAsync() {
        var view=await Request(new { action="view" });
        var m=view.GetProperty("rotation").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        _syncedRotation=Matrix4x4.Transpose(new Matrix4x4(m[0],m[1],m[2],0,m[3],m[4],m[5],0,m[6],m[7],m[8],0,0,0,0,1));
        Draw();
    }
    private void Draw() {
        if(_drawPending || _viewport.ActualWidth<=0 || _viewport.ActualHeight<=0) return;
        _drawPending=true;
        DispatcherQueue.TryEnqueue(()=>{
            _drawPending=false;
            if(_viewport.ActualWidth<=0||_viewport.ActualHeight<=0)return;
            double factor=Math.Min(1,Math.Min(1280/_viewport.ActualWidth,900/_viewport.ActualHeight));
            var rotation=_syncedRotation??(Matrix4x4.CreateRotationY((float)(_yaw.Value*Math.PI/180))*Matrix4x4.CreateRotationX((float)(_pitch.Value*Math.PI/180)));
            _preview.Width=_viewport.ActualWidth; _preview.Height=_viewport.ActualHeight;
            _preview.Source=_raster.Render((int)(_viewport.ActualWidth*factor),(int)(_viewport.ActualHeight*factor),_points,_edges,_triangles,rotation,_zoom.Value,_panX*factor,_panY*factor,Math.Max(0,_shading.SelectedIndex));
        });
    }
}
