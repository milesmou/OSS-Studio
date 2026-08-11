using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace OSSStudio.UI;

internal sealed record AppSettingsResult(
    int UploadConcurrency,
    int DownloadConcurrency,
    string ThemeMode);

internal sealed class SettingsOverlay : ContentControl
{
    private readonly Color Surface;
    private readonly Color HeaderSurface;
    private readonly Color FooterSurface;
    private readonly Color BorderColor;
    private readonly Color MutedText;
    private readonly Color PrimaryText;
    private readonly Color SettingSurface;
    private readonly Color Teal;

    private readonly List<ConcurrencyOption> _concurrencyOptions = Enumerable.Range(1, 8)
        .Select(value => new ConcurrencyOption(value, $"{value} 个任务"))
        .ToList();
    private readonly List<ThemeOption> _themeOptions =
    [
        new("跟随系统", "System"),
        new("浅色", "Light"),
        new("深色", "Dark")
    ];
    private readonly ComboBox _uploadConcurrencyBox = new();
    private readonly ComboBox _downloadConcurrencyBox = new();
    private readonly ComboBox _themeModeBox = new();
    private readonly TaskCompletionSource<AppSettingsResult?> _completion = new();
    private OssMainWindow? _owner;

    public SettingsOverlay(int uploadConcurrency, int downloadConcurrency, string themeMode)
    {
        var palette = AppThemePalette.Current;
        Surface = palette.Surface;
        HeaderSurface = palette.HeaderSurface;
        FooterSurface = palette.PanelSurface;
        BorderColor = palette.Border;
        MutedText = palette.MutedText;
        PrimaryText = palette.PrimaryText;
        SettingSurface = palette.ObjectListSurface;
        Teal = palette.Teal;
        _uploadConcurrencyBox
            .Items(_concurrencyOptions, item => item.DisplayName, item => item.Value)
            .SelectedIndex(Math.Clamp(uploadConcurrency, 1, 8) - 1)
            .Width(150);
        _downloadConcurrencyBox
            .Items(_concurrencyOptions, item => item.DisplayName, item => item.Value)
            .SelectedIndex(Math.Clamp(downloadConcurrency, 1, 8) - 1)
            .Width(150);
        _themeModeBox
            .Items(_themeOptions, item => item.DisplayName, item => item.Value)
            .SelectedIndex(Math.Max(0, _themeOptions.FindIndex(item => item.Value == themeMode)))
            .Width(150);

        Background = Color.Black.WithAlpha(105);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Content = new Border()
            .Width(600)
            .CornerRadius(10)
            .Background(Surface)
            .BorderBrush(BorderColor)
            .BorderThickness(1)
            .HorizontalAlignment(HorizontalAlignment.Center)
            .VerticalAlignment(VerticalAlignment.Center)
            .Child(new DockPanel()
                .LastChildFill()
                .Children(
                    BuildHeader().DockTop(),
                    BuildFooter().DockBottom(),
                    BuildContent()));
    }

    public Task<AppSettingsResult?> ShowAsync(OssMainWindow owner)
    {
        _owner = owner;
        owner.ShowModal(this);
        return _completion.Task;
    }

    private FrameworkElement BuildHeader()
        => new Border()
            .Padding(20, 15)
            .Background(HeaderSurface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .Child(new StackPanel()
                .Vertical()
                .Spacing(3)
                .Children(
                    new TextBlock().Text("设置").FontSize(17).Bold(),
                    new TextBlock().Text("调整传输性能与应用外观").Foreground(MutedText)));

    private FrameworkElement BuildContent()
        => new StackPanel()
            .Vertical()
            .Spacing(12)
            .Margin(20, 18)
            .Children(
                SectionTitle("传输并发"),
                SettingRow(
                    "上传并发数",
                    "同时上传的文件数量。网络或设备负载较高时可适当降低。",
                    _uploadConcurrencyBox),
                SettingRow(
                    "下载并发数",
                    "同时下载的文件数量。目录下载和批量下载均使用此设置。",
                    _downloadConcurrencyBox),
                new Border().Height(1).Background(BorderColor).Margin(0, 4),
                SectionTitle("外观"),
                SettingRow(
                    "主题模式",
                    "可跟随 Windows 外观设置，或固定使用浅色、深色主题。",
                    _themeModeBox),
                new TextBlock()
                    .Text("建议并发数为 3；设置会用于之后开始的传输任务。")
                    .Foreground(MutedText)
                    .FontSize(12));

    private FrameworkElement SectionTitle(string title)
        => new TextBlock().Text(title).FontSize(14).Bold().Foreground(Teal);

    private FrameworkElement SettingRow(string title, string description, FrameworkElement editor)
        => new Border()
            .Padding(14, 12)
            .CornerRadius(7)
            .Background(SettingSurface)
            .BorderBrush(BorderColor.WithAlpha(180))
            .BorderThickness(1)
            .Child(new Grid()
                .Columns("*,Auto")
                .AutoIndexing()
                .Spacing(18)
                .Children(
                    new StackPanel()
                        .Vertical()
                        .Spacing(3)
                        .Children(
                            new TextBlock().Text(title).Bold().Foreground(PrimaryText),
                            new TextBlock { TextWrapping = TextWrapping.Wrap }
                                .Text(description)
                                .Foreground(MutedText)
                                .FontSize(12)),
                    editor.CenterVertical()));

    private FrameworkElement BuildFooter()
        => new Border()
            .Padding(20, 12)
            .Background(FooterSurface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 1, 0, 0))
            .Child(new StackPanel()
                .Horizontal()
                .Spacing(8)
                .HorizontalAlignment(HorizontalAlignment.Right)
                .Children(
                    new Button()
                        .Content("取消", accessKey: false)
                        .StyleName(BuiltInStyles.FlatButton)
                        .Padding(16, 7)
                        .OnClick(() => Complete(null))
                        .WithFeedback("取消并关闭", Color.White.WithAlpha(0), BorderColor.WithAlpha(75), BorderColor),
                    new Button()
                        .Content("保存设置", accessKey: false)
                        .Padding(18, 7)
                        .Background(Teal.WithAlpha(38))
                        .BorderBrush(Teal.WithAlpha(110))
                        .Foreground(Teal)
                        .OnClick(Save)
                        .WithFeedback("保存并应用设置", Teal.WithAlpha(38), Teal.WithAlpha(58), Teal.WithAlpha(88))));

    private void Save()
    {
        var uploadConcurrency = (_uploadConcurrencyBox.SelectedItem as ConcurrencyOption)?.Value ?? 3;
        var downloadConcurrency = (_downloadConcurrencyBox.SelectedItem as ConcurrencyOption)?.Value ?? 3;
        var themeMode = (_themeModeBox.SelectedItem as ThemeOption)?.Value ?? "Light";
        Complete(new AppSettingsResult(uploadConcurrency, downloadConcurrency, themeMode));
    }

    private void Complete(AppSettingsResult? result)
    {
        if (_owner is { } owner)
        {
            owner.HideModal(this);
        }

        _completion.TrySetResult(result);
    }

    private sealed record ConcurrencyOption(int Value, string DisplayName);

    private sealed record ThemeOption(string DisplayName, string Value);
}
