using System;
using System.Reactive.Disposables;
using Avalonia;
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
            if (ViewModel == null) return;

            ViewModel.ConnectCommand.Subscribe(login =>
            {
                if (login != null)
                {
                    // Notify the app that login succeeded, then close this window
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
