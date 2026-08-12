using Aprillz.MewUI;
using OSSStudio.Services;
using OSSStudio.UI;
using System.Diagnostics;

namespace OSSStudio;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new SingleInstanceCoordinator();
        if (!singleInstance.IsPrimary)
        {
            singleInstance.NotifyPrimary();
            return;
        }

        var stateStore = new WorkspaceStateStore();
        var workspace = stateStore.Load();
        var initialTheme = workspace.ThemeMode switch
        {
            "System" => ThemeVariant.System,
            "Dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Light
        };

        var lightSeed = new ThemeSeed
        {
            WindowBackground = Color.FromHex("#EFF6F4"),
            WindowText = Color.FromHex("#243632"),
            ControlBackground = Color.FromHex("#F7FAF9"),
            ButtonFace = Color.FromHex("#E3EEEB"),
            ButtonDisabledBackground = Color.FromHex("#D1DEDA")
        };

        OssMainWindow? mainWindow = null;
        var builder = new ApplicationBuilder(new AppOptions())
            .UseWin32()
            .UseDirect2D()
            .UseTheme(initialTheme)
            .UseAccent(Color.FromHex("#39847E"))
            .UseSeed(lightSeed, ThemeSeed.DefaultDark)
            .BuildMainWindow(() =>
            {
                var window = new OssMainWindow(workspace, stateStore);
                mainWindow = window;
                singleInstance.SetActivationHandler(() =>
                    Application.Current!.Dispatcher!.BeginInvoke(window.RestoreAndActivate));
                return window;
            });

        builder.Run();

        if (mainWindow?.RestartRequested == true && Environment.ProcessPath is { } executablePath)
        {
            singleInstance.Dispose();
            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = AppContext.BaseDirectory,
                UseShellExecute = true
            });
        }
    }
}
