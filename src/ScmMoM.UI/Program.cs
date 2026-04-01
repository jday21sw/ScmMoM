using Avalonia;
using Avalonia.ReactiveUI;

namespace ScmMoM.UI;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Console.Error.WriteLine($"[ScmMoM FATAL] {e.ExceptionObject}");

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ScmMoM FATAL] {ex}");
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace()
            .UseReactiveUI();
}
