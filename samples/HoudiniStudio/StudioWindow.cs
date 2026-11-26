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
        var root = new Grid { Padding = new Thickness(16), RowSpacing = 10, ColumnSpacing = 12 };
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(270) });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new() { Width = new GridLength(280) });
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(_sessions);
        header.Children.Add(Button("Discover", DiscoverAsync));
        header.Children.Add(_directory);
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
        var left = new Grid { RowSpacing = 8 };
        left.RowDefinitions.Add(new() { Height = GridLength.Auto });
        left.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        left.Children.Add(_controls);
        Grid.SetRow(_nodes, 1); left.Children.Add(_nodes);
        Grid.SetRow(left, 1); root.Children.Add(left);
        var center = new Grid { RowSpacing = 8 };
        center.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        center.RowDefinitions.Add(new() { Height = GridLength.Auto });
        center.Children.Add(_viewport);
        var camera = new StackPanel { Spacing = 4 };
        var tools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        tools.Children.Add(Button("Frame", () => { _panX = _panY = 0; _zoom.Value = 1; Draw(); return Task.CompletedTask; }));
        tools.Children.Add(Button("Recook", RecookAsync));
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
        Grid.SetColumn(scroll, 2); Grid.SetRow(scroll, 1); root.Children.Add(scroll);
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
        var b = new Button { Content = label };
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
        if (_parameters.Children.OfType<Grid>().Any(g => g.Children.OfType<TextBox>().Any(t => t.FocusState != FocusState.Unfocused))) return;
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
        _edges = data.GetProperty("edges").EnumerateArray().Select(e => (e[0].GetInt32(), e[1].GetInt32())).ToArray();
        Draw();
        int sourcePoints = data.TryGetProperty("sourcePoints", out var source) ? source.GetInt32() : _points.Length;
        int unsupported = data.TryGetProperty("unsupported", out var skipped) ? skipped.GetInt32() : 0;
        _status.Text = $"{path} | {sourcePoints:N0} source points | {data.GetProperty("primitives").GetInt32():N0} primitives | {_edges.Length:N0} preview edges | Cook {_revision}" +
            (unsupported > 0 ? $" | {unsupported} unsupported primitives" : "");
        var parms = await Request(new { action = "parameters", path });
        if (Selected != path) return;
        _parameters.Children.Clear();
        foreach (var p in parms.EnumerateArray()) {
            string name = p.GetProperty("name").GetString()!;
            string label = p.GetProperty("label").GetString()!;
            bool integer = p.GetProperty("integer").GetBoolean();
            var input = new TextBox { Header = $"{label} ({name})", Text = p.GetProperty("value").GetDouble().ToString("G", CultureInfo.InvariantCulture) };
            var field = new Grid { ColumnSpacing = 6 };
            field.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            field.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            field.Children.Add(input);
            var apply = Button("Apply", async () => {
                if (Selected != path) throw new InvalidOperationException("Selection changed; refresh inspector.");
                double value = double.Parse(input.Text, CultureInfo.InvariantCulture);
                if (!double.IsFinite(value) || (integer && value != Math.Truncate(value)))
                    throw new InvalidOperationException("Enter a valid numeric parameter.");
                await Request(new { action = "set", path, name, value });
                await InspectAsync();
            });
            apply.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(apply, 1); field.Children.Add(apply);
            _parameters.Children.Add(field);
        }
    }
    private void Draw() {
        _viewport.Children.Clear();
        if (_points.Length == 0 || _viewport.ActualWidth <= 0 || _viewport.ActualHeight <= 0) return;
        Vector3 lo = _points[0], hi = lo;
        foreach (var p in _points) { lo = Vector3.Min(lo,p); hi = Vector3.Max(hi,p); }
        var center = (lo + hi) * .5f;
        float radius = Math.Max((hi-lo).Length() * .5f, 1e-6f);
        float scale = (float)(Math.Min(_viewport.ActualWidth,_viewport.ActualHeight) * .42 * _zoom.Value / radius);
        var rotation = Matrix4x4.CreateRotationY((float)(_yaw.Value * Math.PI / 180)) *
                       Matrix4x4.CreateRotationX((float)(_pitch.Value * Math.PI / 180));
        var projected = _points.Select(p => Vector3.Transform(p-center,rotation)*scale).ToArray();
        // Bound visual complexity: a single XAML Path, not one UI element per edge.
        var geometry = new PathGeometry();
        double cx = _viewport.ActualWidth / 2 + _panX, cy = _viewport.ActualHeight / 2 + _panY;
        int stride = Math.Max(1, (_edges.Length + 19999) / 20000);
        for (int i = 0; i < _edges.Length; i += stride) {
            var (a,b) = _edges[i];
            var p = projected[a]; var q = projected[b];
            var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(cx+p.X,cy-p.Y), IsClosed = false, IsFilled = false };
            figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(cx+q.X,cy-q.Y) });
            geometry.Figures.Add(figure);
        }
        if (_edges.Length == 0) {
            foreach (var p in projected.Take(20000)) {
                var figure = new PathFigure { StartPoint = new Windows.Foundation.Point(cx+p.X-1,cy-p.Y), IsFilled = false };
                figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(cx+p.X+1,cy-p.Y) });
                geometry.Figures.Add(figure);
            }
        }
        _viewport.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = new SolidColorBrush(Colors.LightSkyBlue), StrokeThickness = 1, IsHitTestVisible = false });
    }
}
