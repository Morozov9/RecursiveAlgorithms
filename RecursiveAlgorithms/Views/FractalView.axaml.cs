using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace RecursiveAlgorithms.Views;

public partial class FractalView : Window
{
    private CancellationTokenSource? _cts;
    private readonly Stopwatch _stopwatch = new();
    private DispatcherTimer? _uiTimer;
    private bool _isRunning;

    // ── Pan / Zoom state ────────────────────────────────────────────────────
    private double _scale = 1.0;
    private double _translateX;
    private double _translateY;
    private bool _isPanning;
    private Point _panStart;
    private double _panStartTX;
    private double _panStartTY;

    // ── Colour palette (depth-indexed, Catppuccin palette) ──────────────────
    private static readonly Color[] DepthColors =
    [
        Color.FromRgb(166, 227, 161), // green
        Color.FromRgb(148, 226, 213), // teal
        Color.FromRgb(137, 220, 235), // sky
        Color.FromRgb(116, 199, 236), // sapphire
        Color.FromRgb(137, 180, 250), // blue
        Color.FromRgb(180, 190, 254), // lavender
        Color.FromRgb(203, 166, 247), // mauve
        Color.FromRgb(245, 194, 231), // pink
        Color.FromRgb(243, 139, 168), // red
        Color.FromRgb(250, 179, 135), // peach
        Color.FromRgb(249, 226, 175), // yellow
        Color.FromRgb(166, 227, 161),
        Color.FromRgb(148, 226, 213),
        Color.FromRgb(137, 220, 235),
    ];

    public FractalView()
    {
        InitializeComponent();
    }

    // ── Pan / Zoom ──────────────────────────────────────────────────────────

    private void ApplyTransform()
    {
        FractalCanvas.RenderTransform = new MatrixTransform(
            new Matrix(_scale, 0, 0, _scale, _translateX, _translateY));
    }

    private void OnCanvasWheel(object? sender, PointerWheelEventArgs e)
    {
        var pos = e.GetPosition(CanvasContainer);
        double factor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        double oldScale = _scale;
        _scale = Math.Clamp(_scale * factor, 0.05, 50.0);

        // Zoom centered on mouse cursor position
        _translateX = pos.X - (pos.X - _translateX) * (_scale / oldScale);
        _translateY = pos.Y - (pos.Y - _translateY) * (_scale / oldScale);

        ApplyTransform();
        e.Handled = true;
    }

    private void OnCanvasPanStart(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(CanvasContainer);
        if (point.Properties.IsLeftButtonPressed)
        {
            _isPanning = true;
            _panStart  = e.GetPosition(CanvasContainer);
            _panStartTX = _translateX;
            _panStartTY = _translateY;
            CanvasContainer.Cursor = new Cursor(StandardCursorType.SizeAll);
        }
    }

    private void OnCanvasPan(object? sender, PointerEventArgs e)
    {
        if (!_isPanning) return;
        var pos = e.GetPosition(CanvasContainer);
        _translateX = _panStartTX + (pos.X - _panStart.X);
        _translateY = _panStartTY + (pos.Y - _panStart.Y);
        ApplyTransform();
    }

    private void OnCanvasPanEnd(object? sender, PointerReleasedEventArgs e)
    {
        _isPanning = false;
        CanvasContainer.Cursor = Cursor.Default;
    }

    private void OnResetViewClick(object? sender, RoutedEventArgs e)
    {
        _scale      = 1.0;
        _translateX = 0.0;
        _translateY = 0.0;
        ApplyTransform();
    }

    // ── Start ──────────────────────────────────────────────────────────────

    private async void OnStartClick(object? sender, RoutedEventArgs e)
    {
        if (_isRunning) return;

        ClearCanvas();
        SetRunningState(true);

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        int depth = (int)(DepthSpinner.Value ?? 8);
        int delay = (int)(DelaySpinner.Value ?? 40);

        _stopwatch.Restart();
        StartUiTimer();
        StatusLabel.Text = $"Отрисовка... глубина {depth}";

        try
        {
            double canvasW = FractalCanvas.Bounds.Width;
            double canvasH = FractalCanvas.Bounds.Height;

            // Fallback if layout not yet ready
            if (canvasW < 10)
            {
                canvasW = Width  - 20;
                canvasH = Height - 160;
            }

            double startX   = canvasW / 2.0;
            double startY   = canvasH - 30;
            double trunkLen = Math.Min(canvasW, canvasH) * 0.18;

            await DrawTree(startX, startY, startX, startY - trunkLen, depth, delay, token);

            if (!token.IsCancellationRequested)
            {
                _stopwatch.Stop();
                StatusLabel.Text = $"✅ Готово! Уровней: {depth}, линий: {CountLines(depth)}";
            }
            else
            {
                StatusLabel.Text = "⏹ Остановлено";
            }
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "⏹ Остановлено";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            StopUiTimer();
            _stopwatch.Stop();
            UpdateTimerLabel();
            SetRunningState(false);
        }
    }

    // ── Stop / Clear ───────────────────────────────────────────────────────

    private void OnStopClick(object? sender, RoutedEventArgs e) => _cts?.Cancel();

    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        ClearCanvas();
        TimerLabel.Text  = "00:00.000";
        StatusLabel.Text = "Готово к запуску";
        _scale = 1.0; _translateX = 0; _translateY = 0;
        ApplyTransform();
    }

    // ── Recursive drawing algorithm ────────────────────────────────────────

    private async Task DrawTree(
        double x1, double y1,
        double x2, double y2,
        int depth, int delayMs,
        CancellationToken token)
    {
        if (depth <= 0 || token.IsCancellationRequested)
            return;

        var line = new Line
        {
            StartPoint      = new Point(x1, y1),
            EndPoint        = new Point(x2, y2),
            Stroke          = new SolidColorBrush(DepthColors[depth % DepthColors.Length]),
            StrokeThickness = Math.Max(0.5, depth * 0.7),
            StrokeLineCap   = PenLineCap.Round
        };
        FractalCanvas.Children.Add(line);

        // Animate only for branches thick enough to see
        if (delayMs > 0 && depth > 2)
            await Task.Delay(delayMs, token);

        double dx     = x2 - x1;
        double dy     = y2 - y1;
        double len    = Math.Sqrt(dx * dx + dy * dy) * 0.72;
        double angle  = Math.Atan2(dy, dx);
        const double spread = Math.PI / 4.0; // 45°

        double lx2 = x2 + len * Math.Cos(angle - spread);
        double ly2 = y2 + len * Math.Sin(angle - spread);
        double rx2 = x2 + len * Math.Cos(angle + spread);
        double ry2 = y2 + len * Math.Sin(angle + spread);

        await DrawTree(x2, y2, lx2, ly2, depth - 1, delayMs, token);
        await DrawTree(x2, y2, rx2, ry2, depth - 1, delayMs, token);
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void ClearCanvas() => FractalCanvas.Children.Clear();

    private static int CountLines(int depth) => (int)Math.Pow(2, depth) - 1;

    private void SetRunningState(bool running)
    {
        _isRunning             = running;
        StartButton.IsEnabled  = !running;
        StopButton.IsEnabled   =  running;
        ClearButton.IsEnabled  = !running;
        DepthSpinner.IsEnabled = !running;
        DelaySpinner.IsEnabled = !running;
    }

    private void StartUiTimer()
    {
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _uiTimer.Tick += (_, _) => UpdateTimerLabel();
        _uiTimer.Start();
    }

    private void StopUiTimer() { _uiTimer?.Stop(); _uiTimer = null; }

    private void UpdateTimerLabel()
    {
        var el = _stopwatch.Elapsed;
        TimerLabel.Text = $"{(int)el.TotalMinutes:D2}:{el.Seconds:D2}.{el.Milliseconds:D3}";
    }
}
