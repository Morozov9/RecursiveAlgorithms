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

    private void OnRecursiveClick(object? sender, RoutedEventArgs e)
    {
        // Placeholder — раздел в разработке
        var dialog = new Window
        {
            Title = "Скоро",
            Width = 340,
            Height = 160,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            Background = Avalonia.Media.Brushes.Black
        };

        var panel = new StackPanel
        {
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Spacing = 12
        };

        panel.Children.Add(new TextBlock
        {
            Text = "⚙️  Раздел находится в разработке",
            Foreground = Avalonia.Media.Brushes.White,
            FontSize = 15,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            TextAlignment = Avalonia.Media.TextAlignment.Center
        });

        var closeBtn = new Button
        {
            Content = "OK",
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Width = 80
        };
        closeBtn.Click += (_, _) => dialog.Close();
        panel.Children.Add(closeBtn);

        dialog.Content = panel;
        dialog.ShowDialog(this);
    }
}