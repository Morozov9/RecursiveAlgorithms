using Avalonia.Controls;
using Avalonia.Interactivity;
using RecursiveAlgorithms.Views;

namespace RecursiveAlgorithms;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnFractalClick(object? sender, RoutedEventArgs e)
    {
        var fractalWindow = new FractalView();
        fractalWindow.Show();
    }

    private void OnHanoiClick(object? sender, RoutedEventArgs e)
    {
        var hanoiWindow = new HanoiView();
        hanoiWindow.Show();
    }

    private void OnKnapsackClick(object? sender, RoutedEventArgs e)
    {
        var knapsackWindow = new KnapsackView();
        knapsackWindow.Show();
    }
}