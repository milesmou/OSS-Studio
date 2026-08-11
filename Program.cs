using Aprillz.MewUI;
using OSSStudio.Services;
using OSSStudio.UI;

var stateStore = new WorkspaceStateStore();
var workspace = stateStore.Load();

var lightSeed = new ThemeSeed
{
    WindowBackground = Color.FromHex("#E9EEEE"),
    WindowText = Color.FromHex("#34434E"),
    ControlBackground = Color.FromHex("#F4F5F3"),
    ButtonFace = Color.FromHex("#E7EBE9"),
    ButtonDisabledBackground = Color.FromHex("#D4DAD8")
};

var builder = new ApplicationBuilder(new AppOptions())
    .UseWin32()
    .UseDirect2D()
    .UseTheme(ThemeVariant.Light)
    .UseAccent(Color.FromHex("#438F86"))
    .UseSeed(lightSeed, ThemeSeed.DefaultDark)
    .BuildMainWindow(() => new OssMainWindow(workspace, stateStore));

builder.Run();
