using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace BPM.UI.Views;

public partial class CompactWindow : Window
{
    public CompactWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var dragBorder = this.FindControl<Border>("DragBorder");
        if (dragBorder != null)
        {
            dragBorder.PointerPressed += OnDragBorderPointerPressed;
        }

        var expandButton = this.FindControl<Button>("ExpandButton");
        if (expandButton != null)
        {
            expandButton.Click += OnExpandClicked;
        }
    }

    private void OnDragBorderPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnExpandClicked(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            app.ShowFullWindow();
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }
}
