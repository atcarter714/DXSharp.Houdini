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
    private readonly RadioButtons _shading = new() { MaxColumns=3 };
    private readonly CheckBox _legacy = new() { Content="Include hidden / deprecated" };
    private readonly TextBlock _catalogCount = new() { FontSize=12, Foreground=Brush(141,177,193) };
    private readonly TextBlock _context = new() { Text="SOP · /obj", TextWrapping=TextWrapping.Wrap };
    private readonly TextBlock _project = new() { FontFamily=new FontFamily("Consolas"), TextWrapping=TextWrapping.Wrap };
    private readonly CheckBox _autoApply = new() { Content="Auto-apply edits" };
    private readonly Dictionary<(string Path,string Name),object> _pending = [];
    private DateTime _lastEdit;
    private Vector3[] _colors=[];
    private long _nodeId=-1;
    private string? _scene;
    private double _inspectorWidth=420;
    private readonly ColumnDefinition _inspectorColumn=new() { Width=new GridLength(420), MinWidth=300 };
    private readonly SemaphoreSlim _operationGate=new(1,1);
    private bool _started;
    private readonly Dictionary<string,TreeViewNode> _treeNodes=new(StringComparer.Ordinal);
    private sealed record SceneEntry(string Path,bool IsSop,string Category="",string Type="",string ChildCategory="",string[]? Inputs=null) {
        public override string ToString()=>Path.Split('/').Last()+(string.IsNullOrEmpty(Type)?"":$" · {Type}");
    }
    private readonly Dictionary<string,SceneEntry> _sceneEntries=new(StringComparer.Ordinal);
    private readonly ComboBox _operators = new() { PlaceholderText = "Select SOP operator", MaxWidth = 245 };
    private readonly TextBox _operatorFilter = new() { PlaceholderText = "Search operator types..." };
    private readonly ComboBox _tools = new() { PlaceholderText = "SideFX HDA tools", MaxWidth = 245 };
    private readonly TextBlock _attributeText = new() { TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
    private readonly List<OperatorEntry> _operatorList = [];
    private int[] _triangles = [];
    private Matrix4x4? _syncedRotation;
    private SyncedView? _syncedView;
    private readonly TextBlock _viewStatus=new() { Text="Local camera", Foreground=Brush(141,177,193), FontSize=12 };
    private bool _drawPending;
    private readonly CheckBox _followView = new() { Content = "Follow Houdini view" };
    private readonly TextBlock _renderTime = new() { FontSize = 12, Foreground = Brush(141,177,193), VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _quality = new() { ItemsSource = new[] { "Fast", "Balanced", "Detailed" }, SelectedIndex = 1 };
    private static string SettingsFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DXSharp", "HoudiniStudio", "settings.json");
    private sealed record Preferences(int Shading, int Quality, bool Live, double InspectorWidth=420, bool AutoApply=false, bool FollowView=false);
    private void LoadPreferences() {
        try {
            if (!File.Exists(SettingsFile)) return;
            var p=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(SettingsFile));
            if (p==null) return;
            _shading.SelectedIndex=Math.Clamp(p.Shading,0,2);
            _quality.SelectedIndex=Math.Clamp(p.Quality,0,2);
            _live.IsOn=p.Live;
            _autoApply.IsChecked=p.AutoApply; _followView.IsChecked=p.FollowView;
            _inspectorWidth=Math.Clamp(p.InspectorWidth,300,720); _inspectorColumn.Width=new GridLength(_inspectorWidth);
        } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (JsonException) { }
    }
    private void SavePreferences() {
        try {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile,JsonSerializer.Serialize(new Preferences(_shading.SelectedIndex,_quality.SelectedIndex,_live.IsOn,_inspectorWidth,_autoApply.IsChecked==true,_followView.IsChecked==true)));
        } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
    private sealed record OperatorEntry(string Name, string Label, int Inputs, bool Hidden, bool Deprecated) {
        public override string ToString() => $"{Label} · {Name}"+(Deprecated?" [legacy]":Hidden?" [hidden]":"");
    }
    private sealed record ToolEntry(string Name, string File) {
        public override string ToString() => Name;
    }
    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(ColorHelper.FromArgb(255,r,g,b));
    private readonly TextBox _filter = new() { PlaceholderText = "Filter SOP paths..." };
    private readonly TextBox _file = new() { PlaceholderText = "Absolute .bgeo or .bgeo.sc path", MinWidth = 280 };
    private readonly ToggleSwitch _live = new() { Header = "Live update", IsOn = true };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private string[] _allNodes = [];
    private int _revision = -1;
    private string? _previewPath;
    private Windows.Foundation.Point? _drag;
    private bool _panDrag;
    private double _panX, _panY;
    private readonly TextBox _directory = new() { PlaceholderText = "Paste bridge directory from Houdini Python Shell", MinWidth = 180 };
    private readonly TextBlock _status = new() { Text = "Disconnected", TextWrapping = TextWrapping.Wrap };
    private readonly TreeView _nodes = new() { SelectionMode = TreeViewSelectionMode.Single };
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
    private string? Selected => _nodes.SelectedNode?.Content is SceneEntry { IsSop:true } n?n.Path:null;
    private string? Network => _nodes.SelectedNode?.Content is SceneEntry n ? n.IsSop?n.Path[..n.Path.LastIndexOf('/')]:n.Path : null;
    private void UpdateContext() {
        string network=Network??"/obj";
        string category=_sceneEntries.TryGetValue(network,out var entry)?entry.ChildCategory:"";
        _context.Text=(string.IsNullOrEmpty(category)?"Network":category)+" · "+network;
        if(_nodes.SelectedNode?.Content is SceneEntry { Inputs: { Length: >0 } inputs })
            _context.Text+="\nInputs: "+string.Join(", ",inputs.Select(x=>x.Split('/').Last()));
    }
    private void SelectPath(string? path) {
        if(path!=null && _treeNodes.TryGetValue(path,out var node)) {
            for(var parent=node.Parent;parent!=null;parent=parent.Parent) parent.IsExpanded=true;
            _nodes.SelectedNode=node;
            UpdateContext();
        }
    }

    public StudioWindow() {
        Title = "DXSharp Houdini Studio | Live SOP Explorer";
        var root = new Grid { Padding = new Thickness(12), RowSpacing = 10, ColumnSpacing = 10, Background = Brush(15,22,32), RequestedTheme = ElementTheme.Dark };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(285) });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(8) });
        root.ColumnDefinitions.Add(_inspectorColumn);
        _shading.Items.Add(Mode("Wireframe","\uE80A"));
        _shading.Items.Add(Mode("Shaded","\uE91F"));
        _shading.Items.Add(Mode("Shaded + edges","\uE81E"));
        _shading.SelectedIndex=2;
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(new TextBlock { Text = "DXSHARP / HOUDINI", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Brush(105,207,219), VerticalAlignment = VerticalAlignment.Center, FontSize = 16 });
        header.Children.Add(_sessions);
        header.Children.Add(Button("Discover", DiscoverAsync));
        var settingsPanel=new StackPanel { Spacing=10, Width=330 };
        settingsPanel.Children.Add(new TextBlock { Text="Connection & preview", FontSize=18 });
        settingsPanel.Children.Add(_directory);
        settingsPanel.Children.Add(new TextBlock { Text="Preview quality" });
        settingsPanel.Children.Add(_quality);
        _quality.SelectionChanged+=(_,_)=>Draw();
        var settingsButton=new Button { Content = "⚙ Settings", Background=Brush(38,64,80) };
        header.Children.Add(settingsButton);
        _sessions.SelectionChanged += (_, _) => {
            if (_sessions.SelectedItem is SessionEntry session) _directory.Text = session.Directory;
        };
        header.Children.Add(Button("Connect", ConnectAsync));
        header.Children.Add(Button("Refresh nodes", RefreshAsync));
        Grid.SetColumnSpan(header, 4);
        var headerScroll=new ScrollViewer { Content=header, HorizontalScrollMode=ScrollMode.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto, VerticalScrollMode=ScrollMode.Disabled };
        Grid.SetColumnSpan(headerScroll,4); root.Children.Add(headerScroll);
        _controls.Children.Add(new TextBlock { Text = "SCENE EXPLORER", FontSize = 17 });
        _controls.Children.Add(_context);
        _controls.Children.Add(_filter);
        _filter.TextChanged += (_, _) => FilterNodes();
        var create = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (string kind in new[] {"box", "sphere", "torus"})
            create.Children.Add(Button(kind, () => CreateAsync(kind)));
        _controls.Children.Add(create);
        var library = new StackPanel { Spacing = 8, Padding = new Thickness(4) };
        library.Children.Add(new TextBlock { Text = "OPERATOR LIBRARY", Foreground = Brush(104,196,207), FontSize = 12 });
        library.Children.Add(_operatorFilter);
        _operatorFilter.TextChanged += (_, _) => FilterOperators();
        library.Children.Add(_legacy);
        _legacy.Checked+=(_,_)=>FilterOperators(); _legacy.Unchecked+=(_,_)=>FilterOperators();
        library.Children.Add(_catalogCount);
        library.Children.Add(_operators);
        library.Children.Add(Button("＋ Add operator", AddOperatorAsync));
        library.Children.Add(_tools);
        library.Children.Add(Button("↓ Install selected HDA", InstallToolAsync));
        library.Children.Add(Button("… Install HDA file", InstallFileAsync));
        library.Children.Add(Button("↻ Load operator catalog", LoadCatalogAsync));
        var left = new Grid { RowSpacing = 8 };
        left.RowDefinitions.Add(new() { Height = GridLength.Auto });
        left.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        left.Children.Add(_controls);
        Grid.SetRow(_nodes, 1); left.Children.Add(_nodes);
        var leftTabs = new Pivot();
        leftTabs.Items.Add(new PivotItem { Header="Scene", Content=left });
        leftTabs.Items.Add(new PivotItem { Header="Library", Content=new ScrollViewer { Content=library } });
        var leftPanel = new Border { Background = Brush(25,35,47), CornerRadius = new CornerRadius(8), Child = leftTabs, Padding = new Thickness(6) };
        Grid.SetRow(leftPanel, 1); root.Children.Add(leftPanel);
        var center = new Grid { RowSpacing = 8, VerticalAlignment=VerticalAlignment.Stretch };
        center.RowDefinitions.Add(new() { Height = GridLength.Auto });
        center.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        center.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _viewport.Children.Add(_preview);
        _viewport.HorizontalAlignment=HorizontalAlignment.Stretch;
        _viewport.VerticalAlignment=VerticalAlignment.Stretch;
        Grid.SetRow(_viewport,1); center.Children.Add(_viewport);
        var camera = new StackPanel { Spacing = 4 };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tools.Children.Add(_shading);
        _shading.SelectionChanged += (_, _) => Draw();
        tools.Children.Add(Button("◎ Frame", () => { DetachView(); _panX = _panY = 0; _zoom.Value = 1; Draw(); return Task.CompletedTask; }));
        tools.Children.Add(Button("↻ Cook", RecookAsync));
        tools.Children.Add(Button("⇄ Match view", MatchViewAsync));
        center.Children.Add(new ScrollViewer { Content=tools, HorizontalScrollMode=ScrollMode.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto, VerticalScrollMode=ScrollMode.Disabled });
        var sync = new StackPanel { Orientation=Orientation.Horizontal, Spacing=10 };
        sync.Children.Add(_live); sync.Children.Add(_followView); sync.Children.Add(_renderTime); sync.Children.Add(_viewStatus);
        camera.Children.Add(new ScrollViewer { Content=sync, HorizontalScrollMode=ScrollMode.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto, VerticalScrollMode=ScrollMode.Disabled });
        camera.Children.Add(new TextBlock { Text = "Left drag: orbit  |  Middle/right: pan  |  Wheel: zoom  |  Ctrl+wheel: inspector" });
        var io = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        io.Children.Add(_file);
        io.Children.Add(Button("… Browse", BrowseAsync));
        io.Children.Add(Button("Import BGEO", ImportAsync));
        io.Children.Add(Button("Export BGEO", ExportAsync));
        camera.Children.Add(new ScrollViewer { Content=io, HorizontalScrollMode=ScrollMode.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto, VerticalScrollMode=ScrollMode.Disabled });
        Grid.SetRow(camera, 2); center.Children.Add(camera);
        Grid.SetColumn(center, 1); Grid.SetRow(center, 1); root.Children.Add(center);
        var inspector = new StackPanel { Spacing = 8 };
        inspector.Children.Add(new TextBlock { Text = "PARAMETERS", FontSize = 17 });
        var parmTools=new StackPanel { Orientation=Orientation.Horizontal, Spacing=6 };
        parmTools.Children.Add(Button("Refresh", InspectAsync));
        parmTools.Children.Add(Button("Sync edits", FlushEditsAsync));
        parmTools.Children.Add(Button("Discard", async()=>{if(Selected is string path){foreach(var key in _pending.Keys.Where(k=>k.Path==path).ToArray())_pending.Remove(key);await InspectAsync();}}));
        inspector.Children.Add(parmTools);
        inspector.Children.Add(_autoApply);
        inspector.Children.Add(_parameters);
        var scroll = new ScrollViewer { Content = inspector, VerticalScrollBarVisibility=ScrollBarVisibility.Visible, HorizontalScrollMode=ScrollMode.Disabled, Padding=new Thickness(0,0,10,0) };
        // Separate large scroll target from WinUI's narrow overlay scrollbar.
        var scrollBar=new Microsoft.UI.Xaml.Controls.Primitives.ScrollBar { Orientation=Orientation.Vertical, Width=16, Minimum=0, SmallChange=48 };
        var inspectorGrid=new Grid { ColumnSpacing=4 };
        inspectorGrid.ColumnDefinitions.Add(new() { Width=new GridLength(1,GridUnitType.Star) });
        inspectorGrid.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        inspectorGrid.Children.Add(scroll); Grid.SetColumn(scrollBar,1); inspectorGrid.Children.Add(scrollBar);
        scroll.VerticalScrollBarVisibility=ScrollBarVisibility.Hidden;
        bool updatingScroll=false;
        void UpdateScroll() {
            updatingScroll=true;
            scrollBar.Maximum=Math.Max(0,scroll.ScrollableHeight);
            scrollBar.ViewportSize=scroll.ViewportHeight;
            scrollBar.LargeChange=Math.Max(48,scroll.ViewportHeight*.8);
            scrollBar.Value=scroll.VerticalOffset;
            updatingScroll=false;
        }
        scroll.ViewChanged+=(_,_)=>UpdateScroll();
        scroll.SizeChanged+=(_,_)=>UpdateScroll();
        inspector.SizeChanged+=(_,_)=>UpdateScroll();
        scrollBar.ValueChanged+=(_,e)=>{if(!updatingScroll)scroll.ChangeView(null,e.NewValue,null,true);};
        var tabs = new Pivot();
        tabs.Items.Add(new PivotItem { Header = "Parameters", Content = inspectorGrid });
        var attrs = new StackPanel { Spacing = 8, Padding = new Thickness(8) };
        attrs.Children.Add(Button("↻ Read attributes", ReadAttributesAsync));
        attrs.Children.Add(_attributeText);
        tabs.Items.Add(new PivotItem { Header = "Attributes", Content = new ScrollViewer { Content = attrs } });
        var rightPanel = new Border { Background = Brush(25,35,47), CornerRadius = new CornerRadius(8), Child = tabs, Padding = new Thickness(6) };
        Grid.SetColumn(rightPanel, 3); Grid.SetRow(rightPanel, 1); root.Children.Add(rightPanel);
        var splitter=new Border { Background=Brush(56,80,96), CornerRadius=new CornerRadius(3), MinWidth=8 };
        Grid.SetColumn(splitter,2); Grid.SetRow(splitter,1); root.Children.Add(splitter);
        double? splitStart=null;
        double originalWidth=0;
        splitter.PointerPressed+=(_,e)=>{splitStart=e.GetCurrentPoint(root).Position.X; originalWidth=_inspectorWidth; splitter.CapturePointer(e.Pointer); e.Handled=true;};
        splitter.PointerMoved+=(_,e)=>{
            if(splitStart is not double x)return;
            _inspectorWidth=Math.Clamp(originalWidth+x-e.GetCurrentPoint(root).Position.X,300,Math.Max(300,Math.Min(720,root.ActualWidth-580)));
            _inspectorColumn.Width=new GridLength(_inspectorWidth);
        };
        splitter.PointerReleased+=(_,e)=>{splitStart=null; splitter.ReleasePointerCapture(e.Pointer);};
        splitter.PointerCaptureLost+=(_,_)=>splitStart=null;
        ToolTipService.SetToolTip(splitter,"Drag to resize inspector");
        var projectPanel=new StackPanel { Spacing=8, Padding=new Thickness(16) };
        projectPanel.Children.Add(Button("Refresh project", ReadProjectAsync));
        projectPanel.Children.Add(_project);
        var projectTab=new TabViewItem { Header="Project", IsClosable=false, Content=new ScrollViewer { Content=projectPanel } };
        settingsPanel.Width=420;
        settingsPanel.Children.Add(Button("Save settings",()=>{SavePreferences(); _status.Text="Settings saved: "+SettingsFile; return Task.CompletedTask;}));
        settingsPanel.Children.Add(new TextBlock { Text=SettingsFile, TextWrapping=TextWrapping.Wrap });
        settingsPanel.Children.Add(new TextBlock { Text="Inspector width, shading, preview quality, live edit and camera follow preferences persist on close.", TextWrapping=TextWrapping.Wrap });
        var settingsTab=new TabViewItem { Header="Settings", IsClosable=false, Content=new ScrollViewer { Content=settingsPanel } };
        Grid.SetRow(_status, 2); Grid.SetColumnSpan(_status, 4); root.Children.Add(_status);
        // Keep the tab strip in an Auto row and the active workspace in a
        // finite Star row. TabView's content presenter must not measure our
        // entire editor at its minimum desired height.
        var shell=new Grid { Background=Brush(15,22,32), RequestedTheme=ElementTheme.Dark,
            HorizontalAlignment=HorizontalAlignment.Stretch, VerticalAlignment=VerticalAlignment.Stretch };
        shell.RowDefinitions.Add(new() { Height=GridLength.Auto });
        shell.RowDefinitions.Add(new() { Height=new GridLength(1,GridUnitType.Star) });
        var workspace=new TabView { IsAddTabButtonVisible=false, HorizontalAlignment=HorizontalAlignment.Stretch };
        var sceneTab=new TabViewItem { Header="Scene workspace", IsClosable=false };
        var projectPage=(FrameworkElement)projectTab.Content!;
        var settingsPage=(FrameworkElement)settingsTab.Content!;
        projectTab.Content=null;
        settingsTab.Content=null;
        workspace.TabItems.Add(sceneTab);
        workspace.TabItems.Add(projectTab);
        workspace.TabItems.Add(settingsTab);
        shell.Children.Add(workspace);
        var pageHost=new Grid { HorizontalAlignment=HorizontalAlignment.Stretch, VerticalAlignment=VerticalAlignment.Stretch };
        Grid.SetRow(pageHost,1);
        shell.Children.Add(pageHost);
        FrameworkElement[] pages=[root,projectPage,settingsPage];
        foreach(var page in pages) {
            page.HorizontalAlignment=HorizontalAlignment.Stretch;
            page.VerticalAlignment=VerticalAlignment.Stretch;
            page.Visibility=Visibility.Collapsed;
            pageHost.Children.Add(page);
        }
        void SelectWorkspace() {
            for(int i=0;i<pages.Length;i++)
                pages[i].Visibility=i==workspace.SelectedIndex?Visibility.Visible:Visibility.Collapsed;
        }
        workspace.SelectionChanged+=(_,_)=>SelectWorkspace();
        workspace.SelectedIndex=0;
        SelectWorkspace();
        settingsButton.Click+=(_,_)=>workspace.SelectedItem=settingsTab;
        Content=shell;
        _viewport.SizeChanged += (_, _) => Draw();
        _yaw.ValueChanged += (_, _) => Draw();
        _pitch.ValueChanged += (_, _) => Draw();
        _zoom.ValueChanged += (_, _) => Draw();
        _nodes.ItemInvoked += async (_,e) => {
            if(e.InvokedItem is TreeViewNode n) _nodes.SelectedNode=n;
            else if(e.InvokedItem is SceneEntry entry) SelectPath(entry.Path);
            _attributeText.Text="";
            UpdateContext();
            if(Selected!=null) await RunAsync(InspectAsync);
            else { _points=[]; _edges=[]; _triangles=[]; _colors=[]; _parameters.Children.Clear(); Draw(); }
        };
        _viewport.PointerPressed += (_, e) => {
            var p = e.GetCurrentPoint(_viewport);
            DetachView();
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
        _viewport.PointerReleased += (_, e) => { _drag = null; _viewport.ReleasePointerCapture(e.Pointer); Draw(); };
        _viewport.PointerCaptureLost += (_, _) => _drag = null;
        _viewport.PointerWheelChanged += (_, e) => {
            if(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down)) {
                scroll.ChangeView(null,Math.Clamp(scroll.VerticalOffset-e.GetCurrentPoint(_viewport).Properties.MouseWheelDelta,0,scroll.ScrollableHeight),null,true); e.Handled=true; return;
            }
            DetachView();
            _zoom.Value = Math.Clamp(_zoom.Value * Math.Pow(1.12, e.GetCurrentPoint(_viewport).Properties.MouseWheelDelta / 120.0), _zoom.Minimum, _zoom.Maximum);
            e.Handled = true;
        };
        _timer.Tick += async (_, _) => {
            if (_bridge != null && !_busy && (_followView.IsChecked==true || (_live.IsOn && Selected!=null) || (_autoApply.IsChecked==true && _pending.Count>0)))
                await RunAsync(async ()=>{
                    if (_autoApply.IsChecked==true && _pending.Count>0 && DateTime.UtcNow-_lastEdit>TimeSpan.FromMilliseconds(350)) await FlushEditsAsync();
                    if (_followView.IsChecked==true && _drag==null) await MatchViewAsync();
                    if (_live.IsOn && Selected!=null) await CheckRevisionAsync();
                });
        };
        _followView.Checked+=async (_,_)=>{if(_bridge!=null) await RunAsync(MatchViewAsync);};
        _timer.Start();
        Closed += (_, _) => { SavePreferences(); _timer.Stop(); _lifetime.Cancel(); };
        LoadPreferences();
        root.Loaded += async (_, _) => {
            if(_started)return; _started=true;
            await RunAsync(async()=>{await DiscoverAsync(); if(_sessions.Items.Count==1) await ConnectAsync();});
        };
    }

    private Button Button(string label, Func<Task> action) {
        string plain=label.TrimStart('＋','↓','↻','…','◎','⇄','✓',' ');
        string glyph=plain switch {
            "Discover" or "Load operator catalog" => "\uE721",
            "Connect" => "\uE8A7",
            "Refresh" or "Sync edits" or "Refresh nodes" or "Cook" or "Read attributes" or "Read geometry / parameters" => "\uE72C",
            "Add operator" => "\uE710",
            "Install selected HDA" or "Install HDA file" or "Import BGEO" => "\uE8B5",
            "Export BGEO" => "\uE74E",
            "Browse" => "\uE838",
            "Frame" => "\uE739",
            "Match view" => "\uE895",
            "Apply" => "\uE73E",
            "box" => "\uE7B8",
            "sphere" => "\uE909",
            "torus" => "\uE91F",
            _ => ""
        };
        var content=new StackPanel { Orientation=Orientation.Horizontal, Spacing=6 };
        if(glyph.Length>0) content.Children.Add(new FontIcon { Glyph=glyph, FontFamily=new FontFamily("Segoe MDL2 Assets"), FontSize=15 });
        content.Children.Add(new TextBlock { Text=plain, VerticalAlignment=VerticalAlignment.Center });
        var b = new Button { Content = content, Background = Brush(38,64,80), Foreground = Brush(226,245,249), CornerRadius = new CornerRadius(5), Padding = new Thickness(10,6,10,6) };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b,plain);
        ToolTipService.SetToolTip(b,plain);
        b.Click += async (_, _) => await RunAsync(action);
        return b;
    }
    private async Task RunAsync(Func<Task> action) {
        try { await _operationGate.WaitAsync(_lifetime.Token); }
        catch(OperationCanceledException) { return; }
        _busy = true;
        try { await action(); }
        catch (Exception e) { _status.Text = e.Message; }
        finally { _busy = false; _operationGate.Release(); }
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
        _previewPath = null; _pending.Clear(); _nodeId=-1; _scene=null; _syncedView=null; _syncedRotation=null;
        _status.Text = $"Connected: Houdini {result.GetProperty("version").GetString()} | {result.GetProperty("scene").GetString()}";
        await RefreshAsync();
        await ReadProjectAsync();
    }
    private async Task RefreshAsync() {
        string? selected = _nodes.SelectedNode?.Content is SceneEntry entry?entry.Path:null;
        var result = await Request(new { action = "scene_graph" });
        _sceneEntries.Clear();
        foreach(var n in result.EnumerateArray()) {
            var sceneEntry=new SceneEntry(n.GetProperty("path").GetString()!,n.GetProperty("isSop").GetBoolean(),n.GetProperty("category").GetString()!,
                n.GetProperty("type").GetString()!,n.GetProperty("childCategory").GetString()!,n.GetProperty("inputs").EnumerateArray().Select(i=>i.GetString()!).ToArray());
            _sceneEntries[sceneEntry.Path]=sceneEntry;
        }
        _allNodes = _sceneEntries.Keys.Order(StringComparer.Ordinal).ToArray();
        FilterNodes();
        SelectPath(selected);
    }
    private void FilterNodes() {
        string? selected = _nodes.SelectedNode?.Content is SceneEntry entry?entry.Path:null;
        var expanded=_treeNodes.Where(x=>x.Value.IsExpanded).Select(x=>x.Key).ToHashSet(StringComparer.Ordinal);
        _nodes.RootNodes.Clear(); _treeNodes.Clear();
        foreach(string path in _allNodes.Where(x=>x.Contains(_filter.Text,StringComparison.OrdinalIgnoreCase))) {
            TreeViewNode? parent=null;
            string prefix="";
            foreach(string part in path.Split('/',StringSplitOptions.RemoveEmptyEntries)) {
                prefix+="/"+part;
                if(!_treeNodes.TryGetValue(prefix,out var node)) {
                    node=new TreeViewNode { Content=_sceneEntries.GetValueOrDefault(prefix)??new SceneEntry(prefix,false), IsExpanded=prefix=="/obj"||expanded.Contains(prefix) };
                    _treeNodes.Add(prefix,node);
                    if(parent==null) _nodes.RootNodes.Add(node); else parent.Children.Add(node);
                }
                parent=node;
            }
        }
        SelectPath(selected);
    }
    private async Task CheckRevisionAsync() {
        string path = Selected!;
        if (_previewPath==path && _parameters.XamlRoot is not null && Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(_parameters.XamlRoot) is DependencyObject focused) {
            for (DependencyObject? parent=focused;parent!=null;parent=VisualTreeHelper.GetParent(parent)) if (ReferenceEquals(parent,_parameters)) return;
        }
        if(_pending.Keys.Any(k=>k.Path==path) && _previewPath==path) return;
        var result = await Request(new { action = "revision", path });
        if (Selected == path && (_previewPath != path || _revision != result.GetProperty("cook").GetInt32() || _nodeId!=result.GetProperty("id").GetInt64() || _scene!=result.GetProperty("scene").GetString()))
            await InspectAsync();
    }
    private async Task RecookAsync() {
        string path = Selected ?? throw new InvalidOperationException("Select a SOP first.");
        await Request(new { action = "recook", path });
        await InspectAsync();
    }
    private async Task BrowseAsync() {
        var picker=new Windows.Storage.Pickers.FileOpenPicker();
        picker.FileTypeFilter.Add(".bgeo");
        picker.FileTypeFilter.Add(".sc");
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file=await picker.PickSingleFileAsync();
        if(file!=null) _file.Text=file.Path;
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
        SelectPath(result.GetString());
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
        SelectPath(result.GetString());
        await InspectAsync();
    }
    private Task InspectAsync()=>InspectAsync(true);
    private async Task InspectAsync(bool rebuildParameters) {
        string path = Selected ?? throw new InvalidOperationException("Select a SOP first.");
        var data = await Request(new { action = "geometry", path });
        if (Selected != path) return;
        _revision = data.TryGetProperty("revision", out var rev) ? rev.GetInt32() : -1;
        _previewPath = path;
        _nodeId=data.GetProperty("nodeId").GetInt64(); _scene=data.GetProperty("scene").GetString();
        _points = data.GetProperty("positions").EnumerateArray().Select(p =>
            new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle())).ToArray();
        _colors=data.TryGetProperty("colors",out var colors)?colors.EnumerateArray().Select(p=>new Vector3(p[0].GetSingle(),p[1].GetSingle(),p[2].GetSingle())).ToArray():[];
        _triangles = data.TryGetProperty("triangles", out var triangles) ? triangles.EnumerateArray().Select(x => x.GetInt32()).ToArray() : [];
        _edges = data.GetProperty("edges").EnumerateArray().Select(e => (e[0].GetInt32(), e[1].GetInt32())).ToArray();
        Draw();
        int sourcePoints = data.TryGetProperty("sourcePoints", out var source) ? source.GetInt32() : _points.Length;
        int unsupported = data.TryGetProperty("unsupported", out var skipped) ? skipped.GetInt32() : 0;
        _status.Text = $"{path} | {sourcePoints:N0} source points | {data.GetProperty("primitives").GetInt32():N0} primitives | {_edges.Length:N0} preview edges | Cook {_revision}" +
            (unsupported > 0 ? $" | {unsupported} unsupported primitives" : "");
        if(!rebuildParameters || _pending.Keys.Any(k=>k.Path==path)) return;
        var parms = await Request(new { action = "tuples", path });
        if (Selected != path) return;
        _parameters.Children.Clear();
        foreach (var p in parms.EnumerateArray()) AddParameter(path,p);
    }

    private void AddParameter(string path, JsonElement p) {
        string name=p.GetProperty("name").GetString()!, label=p.GetProperty("label").GetString()!;
        string kind=p.GetProperty("kind").GetString()!;
        var group=new StackPanel { Spacing=5, Margin=new Thickness(0,4,0,6) };
        var titleRow=new Grid { ColumnSpacing=6 };
        titleRow.ColumnDefinitions.Add(new() { Width=new GridLength(1,GridUnitType.Star) }); titleRow.ColumnDefinitions.Add(new() { Width=GridLength.Auto });
        titleRow.Children.Add(new TextBlock { Text=label, Foreground=Brush(194,222,232), FontWeight=Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment=VerticalAlignment.Center, TextWrapping=TextWrapping.Wrap });
        group.Children.Add(titleRow);
        var tracked=new List<FrameworkElement>();
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
                tracked.Add(key); keyReaders.Add(()=>key.Value); row.Children.Add(key);
                if(values[i].ValueKind==JsonValueKind.Array) {
                    var components=values[i].EnumerateArray().Select(x=>new NumberBox { Value=x.GetDouble(), Width=65 }).ToArray();
                    foreach(var component in components) { row.Children.Add(component); tracked.Add(component); }
                    readers.Add(()=>components.Select(x=>x.Value).ToArray());
                } else {
                    var value=new NumberBox { Value=values[i].GetDouble(), Width=110 };
                    row.Children.Add(value); tracked.Add(value); readers.Add(()=>value.Value);
                }
                group.Children.Add(row);
            }
            group.Children.Add(new TextBlock { Text="Existing ramp keys · interpolation preserved", FontSize=11, Foreground=Brush(140,164,179) });
        } else if(kind=="toggle") {
            var toggle=new ToggleSwitch { IsOn=values[0].ValueKind==JsonValueKind.True || (values[0].ValueKind==JsonValueKind.Number && values[0].GetInt32()!=0) };
            group.Children.Add(toggle); tracked.Add(toggle); readers.Add(()=>toggle.IsOn?1:0);
        } else if(p.TryGetProperty("labels",out var labels) && labels.GetArrayLength()>0 && values.Length==1) {
            var choices=p.GetProperty("choices").EnumerateArray().Select(x=>x.GetString()!).ToArray();
            var combo=new ComboBox { ItemsSource=labels.EnumerateArray().Select(x=>x.GetString()).ToArray(), HorizontalAlignment=HorizontalAlignment.Stretch };
            combo.SelectedIndex=kind=="string"?Array.IndexOf(choices,values[0].GetString()):values[0].GetInt32();
            group.Children.Add(combo); tracked.Add(combo);
            readers.Add(()=>combo.SelectedIndex<0?throw new InvalidOperationException("Choose a menu value."):kind=="string"?choices[combo.SelectedIndex]:(object)combo.SelectedIndex);
        } else if(kind=="string") {
            foreach(var v in values) {
                var input=new TextBox { Text=v.GetString()??"" };
                group.Children.Add(input); tracked.Add(input); readers.Add(()=>input.Text);
            }
        } else {
            var fields=new Grid { ColumnSpacing=4 };
            var controls=new List<NumberBox>();
            for(int i=0;i<values.Length;i++) {
                fields.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
                var input=new NumberBox { Value=values[i].GetDouble(), SmallChange=kind=="int"?1:.1, MinWidth=0,
                    Header=values.Length>1?(kind=="color"?"RGBA"[Math.Min(i,3)].ToString():i<4?"XYZW"[i].ToString():i.ToString()):null };
                controls.Add(input); tracked.Add(input); Grid.SetColumn(input,i); fields.Children.Add(input);
                readers.Add(()=>kind=="int"?(object)(input.Value==Math.Truncate(input.Value)?checked((int)input.Value):throw new InvalidOperationException("Integer value required.")):input.Value);
            }
            group.Children.Add(fields);
            if(kind=="color" && controls.Count>=3) {
                byte Channel(double x)=>(byte)Math.Clamp(Math.Round(x*255),0,255);
                var picker=new ColorPicker { Color=ColorHelper.FromArgb(255,Channel(controls[0].Value),Channel(controls[1].Value),Channel(controls[2].Value)), IsAlphaEnabled=false, IsColorChannelTextInputVisible=false };
                picker.ColorChanged+=(_,e)=>{controls[0].Value=e.NewColor.R/255.0;controls[1].Value=e.NewColor.G/255.0;controls[2].Value=e.NewColor.B/255.0;};
                group.Children.Add(new Button { Content="Color picker", Flyout=new Flyout { Content=picker } });
            }
        }
        long nodeId=_nodeId; string? scene=_scene;
        object Payload() {
            var data=readers.Select(x=>x()).ToArray();
            return kind=="ramp" ? new { action="set_tuple", path, name, nodeId, scene, values=data, keys=keyReaders.Select(x=>x()).ToArray(), basis } : (object)new { action="set_tuple", path, name, nodeId, scene, values=data };
        }
        string? baseline=null;
        try { baseline=JsonSerializer.Serialize(Payload()); } catch(Exception) { }
        void MarkDirty() {
            if(Selected!=path || _previewPath!=path) return;
            try { var payload=Payload(); if(JsonSerializer.Serialize(payload)==baseline && !_pending.ContainsKey((path,name))) return; _pending[(path,name)]=payload; _lastEdit=DateTime.UtcNow; }
            catch(Exception ex) { _status.Text=ex.Message; }
        }
        var apply=Button("Apply",async ()=>{
            if(Selected!=path) throw new InvalidOperationException("Selection changed.");
            _pending[(path,name)]=Payload();
            await FlushEditsAsync();
        });
        apply.Content=new FontIcon { FontFamily=new FontFamily("Segoe MDL2 Assets"), Glyph="\uE72C", FontSize=14 };
        apply.Background=Brush(19,112,123); apply.Padding=new Thickness(7); 
        Grid.SetColumn(apply,1); titleRow.Children.Add(apply);
        foreach(var field in tracked) {
            if(field is NumberBox number) number.ValueChanged+=(_,_)=>MarkDirty();
            else if(field is TextBox text) text.LostFocus+=(_,_)=>MarkDirty();
            else if(field is ComboBox combo) combo.SelectionChanged+=(_,_)=>MarkDirty();
            else if(field is ToggleSwitch toggle) toggle.Toggled+=(_,_)=>MarkDirty();
        }
        _parameters.Children.Add(new Border { Background=Brush(31,46,59), CornerRadius=new CornerRadius(6), Padding=new Thickness(8), Child=group });
    }
    private static RadioButton Mode(string label,string glyph) {
        var panel=new StackPanel { Orientation=Orientation.Horizontal, Spacing=5 };
        panel.Children.Add(new FontIcon { Glyph=glyph, FontFamily=new FontFamily("Segoe MDL2 Assets"), FontSize=15 });
        panel.Children.Add(new TextBlock { Text=label });
        var mode=new RadioButton { Content=panel };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mode,label);
        ToolTipService.SetToolTip(mode,label);
        return mode;
    }
    private async Task FlushEditsAsync() {
        if(_pending.Count==0) { _status.Text="No pending parameter edits."; return; }
        foreach(var pair in _pending.ToArray()) {
            await Request(pair.Value);
            if(_pending.TryGetValue(pair.Key,out var current) && ReferenceEquals(current,pair.Value)) _pending.Remove(pair.Key);
        }
        if(Selected!=null) await InspectAsync(false);
    }
    private async Task ReadProjectAsync() {
        var data=await Request(new { action="project" });
        _project.Text=JsonSerializer.Serialize(data,new JsonSerializerOptions { WriteIndented=true });
        _context.Text="Houdini active network: "+data.GetProperty("network").GetString();
    }
    private void FilterOperators() {
        var matches=_operatorList.Where(x=>(_legacy.IsChecked==true||(!x.Hidden&&!x.Deprecated))&&(x.Name.Contains(_operatorFilter.Text,StringComparison.OrdinalIgnoreCase)||x.Label.Contains(_operatorFilter.Text,StringComparison.OrdinalIgnoreCase))).ToArray();
        _operators.ItemsSource=matches; _catalogCount.Text=$"{matches.Length:N0} / {_operatorList.Count:N0} SOP operators";
    }
    private async Task LoadCatalogAsync() {
        var catalog=await Request(new { action="operators" });
        _operatorList.Clear();
        foreach(var x in catalog.EnumerateArray()) _operatorList.Add(new(x.GetProperty("name").GetString()!,x.GetProperty("label").GetString()!,x.GetProperty("inputs").GetInt32(),x.GetProperty("hidden").GetBoolean(),x.GetProperty("deprecated").GetBoolean()));
        FilterOperators();
        var tools=await Request(new { action="tools" });
        _tools.ItemsSource=tools.EnumerateArray().Select(x=>new ToolEntry(x.GetProperty("name").GetString()!,x.GetProperty("file").GetString()!)).ToArray();
        _status.Text=$"{_operatorList.Count:N0} SOP types · search to narrow the catalog";
    }
    private async Task AddOperatorAsync() {
        var op=_operators.SelectedItem as OperatorEntry??throw new InvalidOperationException("Select an operator.");
        if(op.Inputs>1) throw new InvalidOperationException("This operator requires multiple inputs. Wire it in Houdini first.");
        if(op.Inputs>0 && Selected==null) throw new InvalidOperationException("Select a source SOP.");
        var result=await Request(new { action="operator", kind=op.Name, path=Selected, network=Network });
        _filter.Text=""; await RefreshAsync(); SelectPath(result.GetString()); await InspectAsync();
    }
    private async Task InstallFileAsync() {
        var picker=new Windows.Storage.Pickers.FileOpenPicker();
        foreach(string ext in new[]{".hda",".hdalc",".hdanc",".otl"}) picker.FileTypeFilter.Add(ext);
        WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
        var file=await picker.PickSingleFileAsync();
        if(file==null) return;
        var result=await Request(new { action="install_hda",file=file.Path });
        await LoadCatalogAsync();
        _status.Text="Installed: "+string.Join(", ",result.EnumerateArray().Select(x=>x.GetString()));
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
    private void DetachView() { _followView.IsChecked=false; _syncedView=null; _syncedRotation=null; _viewStatus.Text="Local camera"; }
    private static Matrix4x4 ReadMatrix(JsonElement data) {
        var m=data.EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        if(m.Length!=16) throw new InvalidDataException("Expected a 4x4 matrix.");
        return new(m[0],m[1],m[2],m[3],m[4],m[5],m[6],m[7],m[8],m[9],m[10],m[11],m[12],m[13],m[14],m[15]);
    }
    private async Task MatchViewAsync() {
        var view=await Request(new { action="view", path=Selected });
        var m=view.GetProperty("rotation").EnumerateArray().Select(x=>x.GetSingle()).ToArray();
        var rotation=Matrix4x4.Transpose(new Matrix4x4(m[0],m[1],m[2],0,m[3],m[4],m[5],0,m[6],m[7],m[8],0,0,0,0,1));
        SyncedView? projected=null;
        if(view.TryGetProperty("toCamera",out var camera)&&view.TryGetProperty("toPixels",out var pixels)) {
            var size=view.GetProperty("resolution").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            if(size.Length==2 && size[0]>0 && size[1]>0)
                projected=new(ReadMatrix(camera),ReadMatrix(pixels),size[0],size[1],view.GetProperty("perspective").GetBoolean());
        }
        _viewStatus.Text=(_followView.IsChecked==true?"Following ":"Matched ")+(view.GetProperty("viewport").GetString()??"Houdini")+(projected.HasValue?" · camera + projection":" · orientation only");
        if(_syncedRotation!=rotation||_syncedView!=projected) { _syncedRotation=rotation; _syncedView=projected; Draw(); }
    }
    private void Draw() {
        if(_drawPending || _viewport.ActualWidth<=0 || _viewport.ActualHeight<=0) return;
        _drawPending=true;
        DispatcherQueue.TryEnqueue(()=>{
            _drawPending=false;
            if(_viewport.ActualWidth<=0||_viewport.ActualHeight<=0)return;
            int limit=_drag!=null?640:_quality.SelectedIndex switch { 0=>640, 2=>1280, _=>960 };
            double factor=Math.Min(1,Math.Min(limit/_viewport.ActualWidth,limit*.75/_viewport.ActualHeight));
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var rotation=_syncedRotation??(Matrix4x4.CreateRotationY((float)(_yaw.Value*Math.PI/180))*Matrix4x4.CreateRotationX((float)(_pitch.Value*Math.PI/180)));
            _preview.Width=_viewport.ActualWidth; _preview.Height=_viewport.ActualHeight;
            _preview.Source=_raster.Render((int)(_viewport.ActualWidth*factor),(int)(_viewport.ActualHeight*factor),_points,_edges,_triangles,_colors,rotation,_zoom.Value,_panX*factor,_panY*factor,Math.Max(0,_shading.SelectedIndex),_syncedView);
            _renderTime.Text=$"{watch.Elapsed.TotalMilliseconds:F0} ms · CPU";
        });
    }
}
