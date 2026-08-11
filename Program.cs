using Aprillz.MewUI;
using OSSStudio.Services;
using OSSStudio.UI;
using System.Diagnostics;

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
    WindowBackground = Color.FromHex("#E9EEEE"),
    WindowText = Color.FromHex("#34434E"),
    ControlBackground = Color.FromHex("#F4F5F3"),
    ButtonFace = Color.FromHex("#E7EBE9"),
    ButtonDisabledBackground = Color.FromHex("#D4DAD8")
};

OssMainWindow? mainWindow = null;
var builder = new ApplicationBuilder(new AppOptions())
    .UseWin32()
    .UseDirect2D()
    .UseTheme(initialTheme)
    .UseAccent(Color.FromHex("#438F86"))
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
