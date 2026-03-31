using Avalonia.Controls;
using Avalonia.Interactivity;
using ScmMoM.UI.ViewModels;

namespace ScmMoM.UI.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var closeButton = this.FindControl<Button>("CloseButton");
        if (closeButton != null)
        {
            closeButton.Click += (_, _) => Close();
        }

        var themeCombo = this.FindControl<ComboBox>("ThemeComboBox");
        if (themeCombo != null)
        {
            themeCombo.ItemsSource = new[] { "System", "Light", "Dark" };
            if (DataContext is SettingsViewModel vm)
            {
                themeCombo.SelectedItem = vm.ThemeMode;
            }
        }
    }
}
