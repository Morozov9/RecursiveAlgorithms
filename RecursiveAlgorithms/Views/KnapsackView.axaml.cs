using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace RecursiveAlgorithms.Views;

public partial class KnapsackView : Window
{
    // ── Data ──────────────────────────────────────────────────────────────
    private record Item(int Index, int Weight, int Value);

    private List<Item> _items = new();
    private int _capacity;

    // Лучшее найденное решение
    private int _bestValue;
    private List<int> _bestItems = new();

    // Текущий набор предметов в рекурсии (номера предметов, начиная с 1)
    private readonly List<int> _current = new();

    // Статистика
    private int _calls;

    // ── Colours ───────────────────────────────────────────────────────────
    private static readonly Color TextColor = Color.FromRgb(205, 214, 244);
    private static readonly Color HeaderTextColor = Color.FromRgb(137, 220, 235);
    private static readonly Color MutedColor = Color.FromRgb(108, 112, 134);
    private static readonly Color DarkText = Color.FromRgb(30, 30, 46);

    private static readonly Color NodeGray = Color.FromRgb(49, 50, 68);
    private static readonly Color NodeGreen = Color.FromRgb(166, 227, 161);
    private static readonly Color NodeGold = Color.FromRgb(249, 226, 175);
    private static readonly Color EdgeGray = Color.FromRgb(88, 91, 112);
    private static readonly Color Red = Color.FromRgb(243, 139, 168);

    // ── Tree layout constants ─────────────────────────────────────────────
    private const double NodeW = 76;
    private const double NodeH = 38;
    private const double NoFitH = 24;
    private const double SlotW = 84;       // ширина одного листа дерева
    private const double LevelH = 78;      // расстояние между уровнями
    private const double LeftMargin = 100; // место под подписи уровней
    private const double TopMargin = 20;

    // Защита от зависания: если вершин больше, рисуем только верхние уровни
    private const int MaxDrawNodes = 3000;

    private enum EdgeKind { Normal, Best, Bad }

    // Вершина дерева рекурсии (для отрисовки)
    private class TreeNode
    {
        public int Index;       // сколько предметов уже рассмотрено
        public int Weight;
        public int Value;
        public int Level;
        public bool OnBest;     // лежит на пути оптимального решения
        public bool IsFinal;    // все предметы рассмотрены
        public bool Truncated;  // дальше дерево не рисуем
        public bool NoFit;      // «заглушка»: предмет не помещается
        public TreeNode? Take;  // ветка «взять»
        public TreeNode? Skip;  // ветка «пропустить»
        public double X;        // координата центра
    }

    private int _treeMaxLevel;
    private HashSet<int> _bestSet = new();

    public KnapsackView()
    {
        InitializeComponent();

        ClearTree();
    }

    // ── Generate items ────────────────────────────────────────────────────
    private void OnGenerateClick(object? sender, RoutedEventArgs e)
    {
        int count = (int)(ItemsCountSpinner.Value ?? 8);
        _capacity = (int)(CapacitySpinner.Value ?? 30);

        // Максимальный вес предмета — треть вместимости (но не меньше 1).
        // Верхняя граница Random.Next исключается, поэтому прибавляем 1.
        int maxWeight = Math.Max(1, _capacity / 3);

        var rnd = new Random();
        _items = new List<Item>();
        for (int i = 0; i < count; i++)
        {
            int weight = rnd.Next(1, maxWeight + 1);
            int value = rnd.Next(1, 30);
            _items.Add(new Item(i + 1, weight, value));
        }

        _bestValue = 0;
        _bestItems = new List<int>();

        RenderItems();
        ClearTree();
        ClearResult();

        ResultStatusLabel.Text = $"Сгенерировано {count} предметов, вместимость {_capacity}";
        StatusLabel.Text = "Готово к решению";
        SolveButton.IsEnabled = true;
        TimerLabel.Text = "0.000 мс";
    }

    // ── Render items list ─────────────────────────────────────────────────
    private void RenderItems()
    {
        ItemsPanel.Children.Clear();

        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var row = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(8, 4)
            };

            var grid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*")
            };

            var idxLabel = new TextBlock
            {
                Text = $"#{item.Index}",
                FontSize = 13,
                FontWeight = FontWeight.Bold,
                Foreground = new SolidColorBrush(HeaderTextColor),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(idxLabel, 0);

            var weightLabel = new TextBlock
            {
                Text = $"⚖ {item.Weight}",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(250, 179, 135)),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(weightLabel, 1);

            var valueLabel = new TextBlock
            {
                Text = $"💰 {item.Value}",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161)),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            };
            Grid.SetColumn(valueLabel, 2);

            grid.Children.Add(idxLabel);
            grid.Children.Add(weightLabel);
            grid.Children.Add(valueLabel);

            row.Child = grid;
            ItemsPanel.Children.Add(row);
        }
    }

    // ── Solve 0/1 Knapsack (recursively) ──────────────────────────────────
    private void OnSolveClick(object? sender, RoutedEventArgs e)
    {
        if (_items.Count == 0) return;

        // сбрасываем состояние перед новым запуском
        _bestValue = 0;
        _bestItems = new List<int>();
        _current.Clear();
        _calls = 0;

        var sw = Stopwatch.StartNew();
        Search(0, 0, 0);
        sw.Stop();

        TimerLabel.Text = $"{sw.Elapsed.TotalMilliseconds:F3} мс";
        StatusLabel.Text = "Решено";
        ResultStatusLabel.Text =
            $"Оптимальная ценность: {_bestValue} · рекурсивных вызовов: {_calls}";

        RenderTree();
        RenderResult();
    }

    /// <summary>
    /// Рекурсивный перебор с возвратом.
    /// Для предмета с номером index есть два варианта:
    ///   1) взять его (если он помещается в рюкзак),
    ///   2) не брать.
    /// После каждого выбора рекурсивно решаем задачу для оставшихся предметов.
    ///
    /// index  — номер рассматриваемого предмета (с нуля),
    /// weight — суммарный вес уже взятых предметов,
    /// value  — суммарная ценность уже взятых предметов.
    ///
    /// База рекурсии: все предметы рассмотрены (index == число предметов) —
    /// сравниваем набор с лучшим из найденных.
    /// </summary>
    private void Search(int index, int weight, int value)
    {
        _calls++;

        // База рекурсии: решение для всех предметов принято
        if (index == _items.Count)
        {
            if (value > _bestValue)
            {
                _bestValue = value;
                _bestItems = new List<int>(_current);
            }
            return;
        }

        var item = _items[index];

        // Вариант 1: взять предмет, если он помещается
        if (weight + item.Weight <= _capacity)
        {
            _current.Add(item.Index);
            Search(index + 1, weight + item.Weight, value + item.Value);
            _current.RemoveAt(_current.Count - 1); // возврат: отменяем выбор
        }

        // Вариант 2: не брать предмет
        Search(index + 1, weight, value);
    }

    // ── Recursion tree ────────────────────────────────────────────────────

    /// <summary>
    /// Рисует полное дерево рекурсии. Ширина дерева зависит от числа реальных
    /// листьев, поэтому ветки, обрезанные по вместимости, место не занимают.
    /// Если вершин больше MaxDrawNodes, рисуются только верхние уровни.
    /// </summary>
    private void RenderTree()
    {
        TreeCanvas.Children.Clear();

        int n = _items.Count;
        _treeMaxLevel = ChooseTreeLevels();
        _bestSet = new HashSet<int>(_bestItems);

        // 1) строим дерево, 2) раскладываем по X, 3) рисуем
        TreeNode root = BuildTree(0, 0, 0, 0, true);

        double nextSlot = 0;
        LayoutTree(root, ref nextSlot);
        int leaves = (int)nextSlot;

        _contentW = LeftMargin + leaves * SlotW + 20;
        _contentH = TopMargin + _treeMaxLevel * LevelH + NodeH + 50;

        // Подписи уровней слева
        for (int level = 0; level <= _treeMaxLevel; level++)
        {
            // на уровне level решаем, брать ли предмет #(level+1); последний уровень — итог
            string text = level == n ? "Итог" : $"Предмет #{level + 1}";
            AddText(text, 8, TopMargin + level * LevelH + NodeH / 2 - 8, 12, MutedColor);
        }

        DrawTree(root, null);

        // Информация под заголовком
        if (_treeMaxLevel < n)
        {
            TreeInfoLabel.Text = $"Вершин: {_calls}. Показаны первые {_treeMaxLevel} уровней из {n}";
            AddText("⋯ дерево слишком большое, чтобы нарисовать его целиком. " +
                    "Уменьшите число предметов или вместимость.",
                LeftMargin, TopMargin + _treeMaxLevel * LevelH + NodeH + 22, 12, MutedColor);
        }
        else
        {
            TreeInfoLabel.Text = $"Полное дерево: {_calls} вершин";
        }

        // после раскладки вписываем дерево в окно (но не увеличиваем больше 100%)
        Dispatcher.UIThread.Post(() => FitView(1.0), DispatcherPriority.Background);
    }

    // ── Pan / Zoom (как в окне фракталов) ─────────────────────────────────
    private double _contentW = 600;   // размер нарисованного дерева
    private double _contentH = 40;
    private double _scale = 1.0;
    private double _translateX;
    private double _translateY;
    private bool _isPanning;
    private Point _panStart;
    private double _panStartTX;
    private double _panStartTY;

    private void ApplyTransform()
    {
        TreeCanvas.RenderTransform = new MatrixTransform(
            new Matrix(_scale, 0, 0, _scale, _translateX, _translateY));
    }

    // Колесо мыши — зум относительно позиции курсора
    private void OnTreeWheel(object? sender, PointerWheelEventArgs e)
    {
        var pos = e.GetPosition(TreeContainer);
        double factor = e.Delta.Y > 0 ? 1.15 : 1.0 / 1.15;
        double oldScale = _scale;
        _scale = Math.Clamp(_scale * factor, 0.01, 100.0);

        _translateX = pos.X - (pos.X - _translateX) * (_scale / oldScale);
        _translateY = pos.Y - (pos.Y - _translateY) * (_scale / oldScale);

        ApplyTransform();
        e.Handled = true;
    }

    // Зажатая левая кнопка — перемещение
    private void OnTreePanStart(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(TreeContainer);
        if (!point.Properties.IsLeftButtonPressed) return;

        _isPanning = true;
        _panStart = e.GetPosition(TreeContainer);
        _panStartTX = _translateX;
        _panStartTY = _translateY;
        e.Pointer.Capture(TreeContainer);
        TreeContainer.Cursor = new Cursor(StandardCursorType.SizeAll);
    }

    private void OnTreePan(object? sender, PointerEventArgs e)
    {
        if (!_isPanning) return;

        var pos = e.GetPosition(TreeContainer);
        _translateX = _panStartTX + (pos.X - _panStart.X);
        _translateY = _panStartTY + (pos.Y - _panStart.Y);
        ApplyTransform();
    }

    private void OnTreePanEnd(object? sender, PointerReleasedEventArgs e)
    {
        _isPanning = false;
        e.Pointer.Capture(null);
        TreeContainer.Cursor = Cursor.Default;
    }

    private void ResetView()
    {
        _scale = 1.0;
        _translateX = 0.0;
        _translateY = 0.0;
        ApplyTransform();
    }

    // Подгоняет дерево под размер поля и ставит по центру
    private void FitView(double maxScale)
    {
        double viewW = TreeContainer.Bounds.Width;
        double viewH = TreeContainer.Bounds.Height;
        if (viewW < 50 || viewH < 50) return;

        double s = Math.Min((viewW - 40) / _contentW, (viewH - 40) / _contentH);
        _scale = Math.Clamp(s, 0.01, maxScale);
        _translateX = (viewW - _contentW * _scale) / 2;
        _translateY = Math.Max(10, (viewH - _contentH * _scale) / 2);
        ApplyTransform();
    }

    private void OnResetViewClick(object? sender, RoutedEventArgs e) => ResetView();
    private void OnFitViewClick(object? sender, RoutedEventArgs e) => FitView(100.0);

    /// <summary>
    /// Подбирает число уровней: полное дерево, если оно умещается в MaxDrawNodes вершин.
    /// </summary>
    private int ChooseTreeLevels()
    {
        int n = _items.Count;
        var counts = new int[n + 2];
        CountNodes(0, 0, 0, counts);

        int total = 0;
        int levels = 0;
        for (int l = 0; l <= n; l++)
        {
            total += counts[l];
            if (total > MaxDrawNodes) break;
            levels = l;
        }
        return Math.Min(Math.Max(1, levels), n);
    }

    // Рекурсивно считает, сколько вершин на каждом уровне
    private void CountNodes(int index, int weight, int level, int[] counts)
    {
        counts[level]++;
        if (index == _items.Count) return;

        var item = _items[index];
        if (weight + item.Weight <= _capacity)
            CountNodes(index + 1, weight + item.Weight, level + 1, counts);
        else
            counts[level + 1]++; // заглушка «не влезает»

        CountNodes(index + 1, weight, level + 1, counts);
    }

    // Рекурсивно строит дерево вершин
    private TreeNode BuildTree(int index, int weight, int value, int level, bool onBest)
    {
        var node = new TreeNode
        {
            Index = index,
            Weight = weight,
            Value = value,
            Level = level,
            OnBest = onBest,
            IsFinal = index == _items.Count
        };

        if (node.IsFinal) return node;

        if (level == _treeMaxLevel)
        {
            node.Truncated = true;
            return node;
        }

        var item = _items[index];

        if (weight + item.Weight <= _capacity)
        {
            bool takeOnBest = onBest && _bestSet.Contains(item.Index);
            node.Take = BuildTree(index + 1, weight + item.Weight, value + item.Value,
                level + 1, takeOnBest);
        }
        else
        {
            node.Take = new TreeNode { Level = level + 1, NoFit = true };
        }

        bool skipOnBest = onBest && !_bestSet.Contains(item.Index);
        node.Skip = BuildTree(index + 1, weight, value, level + 1, skipOnBest);

        return node;
    }

    // Листья идут слева направо подряд, родитель стоит по центру между детьми
    private static void LayoutTree(TreeNode node, ref double nextSlot)
    {
        if (node.Take == null || node.Skip == null)
        {
            node.X = LeftMargin + (nextSlot + 0.5) * SlotW;
            nextSlot++;
            return;
        }

        LayoutTree(node.Take, ref nextSlot);
        LayoutTree(node.Skip, ref nextSlot);
        node.X = (node.Take.X + node.Skip.X) / 2;
    }

    // Рекурсивно рисует вершину, ветку к ней от родителя и её потомков
    private void DrawTree(TreeNode node, TreeNode? parent)
    {
        double cy = TopMargin + node.Level * LevelH + NodeH / 2;

        if (parent != null)
        {
            double py = TopMargin + parent.Level * LevelH + NodeH / 2;
            double childTop = node.NoFit ? cy - NoFitH / 2 : cy - NodeH / 2;
            EdgeKind kind = node.NoFit ? EdgeKind.Bad
                          : node.OnBest ? EdgeKind.Best
                          : EdgeKind.Normal;
            AddEdge(parent.X, py + NodeH / 2, node.X, childTop, kind);
        }

        if (node.NoFit)
        {
            AddNoFitBox(node.X, cy);
            return;
        }

        AddNodeBox(node.X, cy,
            $"вес {node.Weight}/{_capacity}\nценность {node.Value}",
            node.OnBest ? (node.IsFinal ? NodeGold : NodeGreen) : NodeGray,
            node.OnBest);

        if (node.Truncated)
        {
            AddText("⋯", node.X - 5, cy + NodeH / 2 + 2, 16, MutedColor);
            return;
        }

        if (node.Take != null) DrawTree(node.Take, node);
        if (node.Skip != null) DrawTree(node.Skip, node);
    }

    private void AddNodeBox(double cx, double cy, string text, Color background, bool onBest)
    {
        var border = new Border
        {
            Width = NodeW,
            Height = NodeH,
            Background = new SolidColorBrush(background),
            BorderBrush = new SolidColorBrush(onBest ? NodeGreen : EdgeGray),
            BorderThickness = new Thickness(onBest ? 2 : 1),
            CornerRadius = new CornerRadius(8),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 10,
                FontWeight = onBest ? FontWeight.Bold : FontWeight.Normal,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(onBest ? DarkText : TextColor),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };

        Canvas.SetLeft(border, cx - NodeW / 2);
        Canvas.SetTop(border, cy - NodeH / 2);
        TreeCanvas.Children.Add(border);
    }

    private void AddNoFitBox(double cx, double cy)
    {
        var border = new Border
        {
            Width = NodeW,
            Height = NoFitH,
            Background = Brushes.Transparent,
            BorderBrush = new SolidColorBrush(Red),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new TextBlock
            {
                Text = "✕ не влезает",
                FontSize = 10,
                Foreground = new SolidColorBrush(Red),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
            }
        };

        Canvas.SetLeft(border, cx - NodeW / 2);
        Canvas.SetTop(border, cy - NoFitH / 2);
        TreeCanvas.Children.Add(border);
    }

    private void AddEdge(double x1, double y1, double x2, double y2, EdgeKind kind)
    {
        Color color = kind switch
        {
            EdgeKind.Best => NodeGreen,
            EdgeKind.Bad => Red,
            _ => EdgeGray
        };

        var line = new Line
        {
            StartPoint = new Point(x1, y1),
            EndPoint = new Point(x2, y2),
            Stroke = new SolidColorBrush(color),
            StrokeThickness = kind == EdgeKind.Best ? 2.5 : 1.2
        };
        TreeCanvas.Children.Add(line);
    }

    private void AddText(string text, double x, double y, double fontSize, Color color)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(color)
        };
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, y);
        TreeCanvas.Children.Add(tb);
    }

    private void ClearTree()
    {
        TreeCanvas.Children.Clear();
        _contentW = 600;
        _contentH = 40;
        TreeInfoLabel.Text = "";
        ResetView();
        AddText("Нажмите «Сгенерировать», затем «Решить» — здесь появится дерево рекурсии",
            0, 0, 13, MutedColor);
    }

    // ── Render result ─────────────────────────────────────────────────────
    private void RenderResult()
    {
        ResultPanel.Children.Clear();

        if (_bestItems.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "Нет подходящих предметов",
                FontSize = 13,
                Foreground = new SolidColorBrush(MutedColor)
            };
            ResultPanel.Children.Add(empty);
            return;
        }

        var title = new TextBlock
        {
            Text = $"Выбрано предметов: {_bestItems.Count}",
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(HeaderTextColor)
        };
        ResultPanel.Children.Add(title);

        foreach (int idx in _bestItems)
        {
            var item = _items[idx - 1];
            var row = new TextBlock
            {
                Text = $"✓ #{item.Index}: вес {item.Weight}, ценность {item.Value}",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(166, 227, 161))
            };
            ResultPanel.Children.Add(row);
        }

        int totalWeight = _bestItems.Sum(i => _items[i - 1].Weight);
        var summary = new TextBlock
        {
            Text = $"Итого: вес {totalWeight} / {_capacity}, ценность {_bestValue}",
            FontSize = 13,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(249, 226, 175)),
            Margin = new Thickness(0, 6, 0, 0)
        };
        ResultPanel.Children.Add(summary);
    }

    // ── Clear ─────────────────────────────────────────────────────────────
    private void OnClearClick(object? sender, RoutedEventArgs e)
    {
        _items.Clear();
        _bestItems = new List<int>();
        _bestValue = 0;
        _current.Clear();

        ItemsPanel.Children.Clear();
        ResultPanel.Children.Clear();
        ClearTree();

        SolveButton.IsEnabled = false;
        TimerLabel.Text = "0.000 мс";
        StatusLabel.Text = "Ожидание";
        ResultStatusLabel.Text = "Нажмите «Сгенерировать» для начала";
    }

    private void ClearResult()
    {
        ResultPanel.Children.Clear();
    }
}
