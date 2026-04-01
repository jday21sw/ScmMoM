using System;
using System.Reactive.Disposables;
using Avalonia;
using Avalonia.Controls;
using Avalonia.ReactiveUI;
using ScmMoM.UI.ViewModels;
using ReactiveUI;

namespace ScmMoM.UI.Views;

public partial class LoginWindow : ReactiveWindow<LoginViewModel>
{
    public LoginWindow()
    {
        InitializeComponent();

        this.WhenActivated(d =>
        {
            // Populate provider combo
            var providerCombo = this.FindControl<ComboBox>("ProviderCombo");
            if (providerCombo != null)
            {
                providerCombo.ItemsSource = new[] { "GitHub", "GitLab", "Gitea" };
                if (providerCombo.SelectedIndex < 0) providerCombo.SelectedIndex = 0;
            }

            if (ViewModel == null) return;

            ViewModel.ConnectCommand.Subscribe(login =>
            {
                if (login != null)
                {
                    if (Application.Current is App app)
                    {
                        app.OnLoginSucceeded(login);
                    }
                    Close();
                }
            }).DisposeWith(d);
        });
    }
}
