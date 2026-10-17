using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace RecursiveAlgorithms.Views;

public partial class HanoiView : Window
{
    // ── Visual constants ────────────────────────────────────────────────────
    private const double PegWidth = 10;
    private const double BaseHeight = 18;
    private const double BaseWidth = 200;
    private const double DiskHeight = 28;
    private const double MinDiskWidth = 44;

    private static readonly Color[] DiskColors =
    [
        Color.FromRgb(243, 139, 168), // red
        Color.FromRgb(250, 179, 135), // peach
        Color.FromRgb(249, 226, 175), // yellow
        Color.FromRgb(166, 227, 161), // green
        Color.FromRgb(148, 226, 213), // teal
        Color.FromRgb(137, 220, 235), // sky
        Color.FromRgb(116, 199, 236), // sapphire
        Color.FromRgb(137, 180, 250), // blue
        Color.FromRgb(180, 190, 254), // lavender
        Color.FromRgb(203, 166, 247), // mauve
    ];

    // ── State ───────────────────────────────────────────────────────────────
    private int _diskCount;
    private double _canvasWidth;
    private double _canvasHeight;
    private double _maxDiskWidth;
    private double _groundY;

    private readonly Stack<int>[] _pegs = [new(), new(), new()];
    private Rectangle[] _diskRects = [];
    private double[] _pegCenters = [];

    private CancellationTokenSource? _cts;
    private bool _isRunning;
    private int _totalMoves;
    private int _currentMove;

    // ── Init ────────────────────────────────────────────────────────────────
    public HanoiView()
    {
        InitializeComponent();
        HanoiCanvas.LayoutUpdated += OnCanvasLayoutUpdated;
    }

    private bool _layoutInitialized;
    private void OnCanvasLayoutUpdated(object? sender, EventArgs e)
    {
        if (_layoutInitialized || HanoiCanvas.Bounds.Width < 10) return;
        _layoutInitialized = true;
        InitializePuzzle();
    }

    // ── Build pegs + disks on canvas ────────────────────────────────────────
    private void InitializePuzzle()
    {
        HanoiCanvas.Children.Clear();
        foreach (var p in _pegs) p.Clear();

        _diskCount = (int)(DiskCountSpinner.Value ?? 5);
        _canvasWidth = HanoiCanvas.Bounds.Width;
        _canvasHeight = HanoiCanvas.Bounds.Height;
        _maxDiskWidth = (_canvasWidth / 3.0) * 0.85;
        _groundY = _canvasHeight - 20;

        _totalMoves = (1 << _diskCount) - 1;
        _currentMove = 0;

        MoveCounterLabel.Text = $"0 / {_totalMoves}";
        StatusLabel.Text = "Ожидание";
        MoveDescLabel.Text = "";

        double zone = _canvasWidth / 3.0;
        _pegCenters = [zone * 0.5, zone * 1.5, zone * 2.5];

        double pegHeight = _canvasHeight * 0.65;

        // Draw bases, poles, labels
        for (int i = 0; i < 3; i++)
        {
            var baseRect = new Rectangle
            {
                Width = BaseWidth,
                Height = BaseHeight,
                Fill = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
                RadiusX = 4,
                RadiusY = 4
            };
            Canvas.SetLeft(baseRect, _pegCenters[i] - BaseWidth / 2);
            Canvas.SetTop(baseRect, _groundY - BaseHeight + 2);
            HanoiCanvas.Children.Add(baseRect);

            var pole = new Rectangle
            {
                Width = PegWidth,
                Height = pegHeight,
                Fill = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
                RadiusX = 3,
                RadiusY = 3
            };
            Canvas.SetLeft(pole, _pegCenters[i] - PegWidth / 2);
            Canvas.SetTop(pole, _groundY - BaseHeight - pegHeight + 2);
            HanoiCanvas.Children.Add(pole);

            var label = new TextBlock
            {
                Text = ((char)('A' + i)).ToString(),
                FontSize = 18,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(108, 112, 134))
            };
            Canvas.SetLeft(label, _pegCenters[i] - 9);
            Canvas.SetTop(label, _groundY + 2);
            HanoiCanvas.Children.Add(label);
        }

        // Create disk rectangles
        _diskRects = new Rectangle[_diskCount + 1];
        double step = (_maxDiskWidth - MinDiskWidth) / Math.Max(_diskCount - 1, 1);

        // ★ ИСПРАВЛЕНИЕ: кладём в стек СНАЧАЛА БОЛЬШОЕ, ПОТОМ МАЛЕНЬКОЕ ★
        for (int d = _diskCount; d >= 1; d--)
        {
            double dw = MinDiskWidth + (d - 1) * step;
            var rect = new Rectangle
            {
                Width = dw,
                Height = DiskHeight - 2,
                Fill = new SolidColorBrush(DiskColors[(d - 1) % DiskColors.Length]),
                RadiusX = 7,
                RadiusY = 7
            };
            _diskRects[d] = rect;
            HanoiCanvas.Children.Add(rect);
            _pegs[0].Push(d);
        }

        // Position all disks on peg A
        SnapAllDisks();
    }

    // ── Place all disks instantly at their correct positions ─────────────────
    private void SnapAllDisks()
    {
        for (int p = 0; p < 3; p++)
        {
            var arr = _pegs[p].ToArray();   // [top, ..., bottom]
            Array.Reverse(arr);             // [bottom, ..., top]
            for (int s = 0; s < arr.Length; s++)
                SnapDisk(arr[s], p, s);
        }
    }

    private void SnapDisk(int disk, int pegIdx, int stackPos)
    {
        var rect = _diskRects[disk];
        Canvas.SetLeft(rect, _pegCenters[pegIdx] - rect.Width / 2);
        Canvas.SetTop(rect, _groundY - BaseHeight - (stackPos + 1) * DiskHeight);
        rect.ZIndex = stackPos + 1;
    }

    // ── Get disk target position without moving it ───────────────────────────
    private (double x, double y) DiskTargetPos(int disk, int pegIdx, int stackPos)
    {
        var rect = _diskRects[disk];
        return (
            _pegCenters[pegIdx] - rect.Width / 2,
            _groundY - BaseHeight - (stackPos + 1) * DiskHeight
        );
    }

    // ── Recursive move generator ─────────────────────────────────────────────
    private static void GenerateMoves(int n, int from, int to, int aux, List<(int, int)> moves)
    {
        if (n <= 0) return;
        GenerateMoves(n - 1, from, aux, to, moves);
        moves.Add((from, to));
        GenerateMoves(n - 1, aux, to, from, moves);
    }

    // ── Start ────────────────────────────────────────────────────────────────
    private async void OnStartClick(object? sender, RoutedEventArgs e)
    {
        if (_isRunning) return;

        InitializePuzzle();
        SetRunningState(true);

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        int delayMs = (int)(SpeedSpinner.Value ?? 600);

        var moves = new List<(int from, int to)>();
        GenerateMoves(_diskCount, 0, 2, 1, moves);

        // Transit Y: well above the tallest possible stack
        double transitY = Math.Max(15.0, _groundY - BaseHeight - (_diskCount + 3) * DiskHeight);

        StatusLabel.Text = "Решение...";

        try
        {
            foreach (var (from, to) in moves)
            {
                token.ThrowIfCancellationRequested();

                // Identify disk on top of source peg
                int disk = _pegs[from].Peek();
                var rect = _diskRects[disk];

                double fromX = Canvas.GetLeft(rect);
                double fromY = Canvas.GetTop(rect);

                // Update logical state
                _pegs[from].Pop();
                _pegs[to].Push(disk);

                int newStackPos = _pegs[to].Count - 1;
                var (toX, toY) = DiskTargetPos(disk, to, newStackPos);
                rect.ZIndex = _diskCount + 10; // keep moving disk on top

                _currentMove++;
                MoveCounterLabel.Text = $"{_currentMove} / {_totalMoves}";
                MoveDescLabel.Text = $"Диск {disk}: {(char)('A' + from)} → {(char)('A' + to)}";

                // ★ Smooth 3-phase animation ★
                await AnimateDiskMove(rect, fromX, fromY, toX, toY, transitY, delayMs, token);

                rect.ZIndex = newStackPos + 1;
            }

            StatusLabel.Text = "✅ Решено!";
            MoveDescLabel.Text = $"Всего ходов: {_totalMoves}";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "⏹ Остановлено";
            MoveDescLabel.Text = "";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Ошибка: {ex.Message}";
        }
        finally
        {
            SetRunningState(false);
        }
    }

    // ── 3-Phase smooth disk animation ────────────────────────────────────────
    private static async Task AnimateDiskMove(
        Rectangle disk,
        double fromX, double fromY,
        double toX, double toY,
        double transitY,
        int totalDelayMs,
        CancellationToken token)
    {
        int ms1 = (int)(totalDelayMs * 0.30);
        int ms2 = (int)(totalDelayMs * 0.40);
        int ms3 = totalDelayMs - ms1 - ms2;

        await AnimateSegment(disk, fromX, fromY, fromX, transitY, ms1, token); // up
        await AnimateSegment(disk, fromX, transitY, toX, transitY, ms2, token); // across
        await AnimateSegment(disk, toX, transitY, toX, toY, ms3, token); // down

        Canvas.SetLeft(disk, toX);
        Canvas.SetTop(disk, toY);
    }

    // Animate a single linear segment with easing
    private static async Task AnimateSegment(
        Rectangle disk,
        double x1, double y1,
        double x2, double y2,
        int durationMs,
        CancellationToken token)
    {
        const int FrameMs = 14;
        int frames = Math.Max(2, durationMs / FrameMs);
        int frameDelay = Math.Max(1, durationMs / frames);

        for (int i = 1; i <= frames; i++)
        {
            token.ThrowIfCancellationRequested();
            double t = EaseInOut((double)i / frames);
            Canvas.SetLeft(disk, Lerp(x1, x2, t));
            Canvas.SetTop(disk, Lerp(y1, y2, t));
            await Task.Delay(frameDelay, token);
        }
    }

    // ── Math helpers ─────────────────────────────────────────────────────────
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    private static double EaseInOut(double t) =>
        t < 0.5 ? 2 * t * t : -1 + (4 - 2 * t) * t;

    // ── Stop ─────────────────────────────────────────────────────────────────
    private void OnStopClick(object? sender, RoutedEventArgs e) => _cts?.Cancel();

    // ── Reset ────────────────────────────────────────────────────────────────
    private void OnResetClick(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _layoutInitialized = false;
        InitializePuzzle();
        SetRunningState(false);
    }

    // ── UI state ─────────────────────────────────────────────────────────────
    private void SetRunningState(bool running)
    {
        _isRunning = running;
        StartButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        ResetButton.IsEnabled = !running;
        DiskCountSpinner.IsEnabled = !running;
        SpeedSpinner.IsEnabled = !running;
    }
}