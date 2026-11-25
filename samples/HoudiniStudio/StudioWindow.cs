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
    private readonly TextBox _directory = new() { PlaceholderText = "Paste bridge directory from Houdini Python Shell", MinWidth = 400 };
    private readonly TextBlock _status = new() { Text = "Disconnected", TextWrapping = TextWrapping.Wrap };
    private readonly ListView _nodes = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly StackPanel _parameters = new() { Spacing = 6 };
    private readonly Canvas _viewport = new() { Background = new SolidColorBrush( ColorHelper.FromArgb(255, 18, 23, 31) ) };
    private readonly Slider _yaw = new() { Minimum = -180, Maximum = 180, Value = 35, Header = "Orbit yaw" };
    private readonly Slider _pitch = new() { Minimum = -89, Maximum = 89, Value = 25, Header = "Orbit pitch" };
    private readonly Slider _zoom = new() { Minimum = .2, Maximum = 3, Value = 1, StepFrequency = .05, Header = "Zoom" };
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
        header.Children.Add(_directory);
        header.Children.Add(Button("Connect", ConnectAsync));
        header.Children.Add(Button("Refresh nodes", RefreshAsync));
        Grid.SetColumnSpan(header, 3);
        root.Children.Add(header);
        _controls.Children.Add(new TextBlock { Text = "SOP NETWORK", FontSize = 17 });
        var create = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (string kind in new[] {"box", "sphere", "torus"})
            create.Children.Add(Button(kind, () => CreateAsync(kind)));
        _controls.Children.Add(create);
        _controls.Children.Add(_nodes);
        Grid.SetRow(_controls, 1);
        root.Children.Add(_controls);
        var center = new Grid { RowSpacing = 8 };
        center.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        center.RowDefinitions.Add(new() { Height = GridLength.Auto });
        center.Children.Add(_viewport);
        var camera = new StackPanel { Spacing = 4 };
        camera.Children.Add(_yaw); camera.Children.Add(_pitch); camera.Children.Add(_zoom);
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
        Closed += (_, _) => _lifetime.Cancel();
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
    private async Task ConnectAsync() {
        _bridge = new BridgeClient(_directory.Text.Trim());
        var result = await Request(new { action = "ping" });
        _status.Text = $"Connected: Houdini {result.GetProperty("version").GetString()} | {result.GetProperty("scene").GetString()}";
        await RefreshAsync();
    }
    private async Task RefreshAsync() {
        string? selected = Selected;
        var result = await Request(new { action = "nodes" });
        _nodes.ItemsSource = result.EnumerateArray().Select(x => x.GetString()!).ToArray();
        _nodes.SelectedItem = selected;
    }
    private async Task CreateAsync(string kind) {
        var result = await Request(new { action = "create", kind });
        await RefreshAsync();
        _nodes.SelectedItem = result.GetString();
        await InspectAsync();
    }
    private async Task InspectAsync() {
        string path = Selected ?? throw new InvalidOperationException("Select a SOP first.");
        var data = await Request(new { action = "geometry", path });
        _points = data.GetProperty("positions").EnumerateArray().Select(p =>
            new Vector3(p[0].GetSingle(), p[1].GetSingle(), p[2].GetSingle())).ToArray();
        _edges = data.GetProperty("edges").EnumerateArray().Select(e => (e[0].GetInt32(), e[1].GetInt32())).ToArray();
        Draw();
        _status.Text = $"{path} | {_points.Length:N0} points | {data.GetProperty("primitives").GetInt32():N0} primitives | {_edges.Length:N0} polygon edges";
        var parms = await Request(new { action = "parameters", path });
        _parameters.Children.Clear();
        foreach (var p in parms.EnumerateArray()) {
            string name = p.GetProperty("name").GetString()!;
            string label = p.GetProperty("label").GetString()!;
            bool integer = p.GetProperty("integer").GetBoolean();
            var input = new TextBox { Header = $"{label} ({name})", Text = p.GetProperty("value").GetDouble().ToString("G", CultureInfo.InvariantCulture) };
            _parameters.Children.Add(input);
            _parameters.Children.Add(Button("Apply " + name, async () => {
                if (Selected != path) throw new InvalidOperationException("Selection changed; refresh inspector.");
                double value = double.Parse(input.Text, CultureInfo.InvariantCulture);
                if (!double.IsFinite(value) || (integer && value != Math.Truncate(value)))
                    throw new InvalidOperationException("Enter a valid numeric parameter.");
                await Request(new { action = "set", path, name, value });
                await InspectAsync();
            }));
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
        double cx = _viewport.ActualWidth / 2, cy = _viewport.ActualHeight / 2;
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
        _viewport.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = geometry, Stroke = new SolidColorBrush(Colors.LightSkyBlue), StrokeThickness = 1 });
    }
}
