using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace OSSStudio.UI;

public sealed class TextEditorOverlay : ContentControl
{
    private readonly Color Surface;
    private readonly Color HeaderSurface;
    private readonly Color PanelSurface;
    private readonly Color BorderColor;
    private readonly Color MutedText;
    private readonly Color Teal;

    private readonly MultiLineTextBox _editor;
    private readonly TaskCompletionSource<string?> _completion = new();
    private Window? _owner;
    private OssMainWindow? _modalOwner;

    public TextEditorOverlay(string bucketName, string fileName, string objectKey, string content)
    {
        var palette = AppThemePalette.Current;
        Surface = palette.Surface;
        HeaderSurface = palette.HeaderSurface;
        PanelSurface = palette.PanelSurface;
        BorderColor = palette.Border;
        MutedText = palette.MutedText;
        Teal = palette.Teal;
        Background = Color.Black.WithAlpha(105);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        _editor = new MultiLineTextBox()
            .Text(content)
            .FontSize(13)
            .StretchHorizontal()
            .StretchVertical();

        Content = new Border()
            .Width(880)
            .Height(640)
            .CornerRadius(10)
            .Background(Surface)
            .BorderBrush(BorderColor)
            .BorderThickness(1)
            .HorizontalAlignment(HorizontalAlignment.Center)
            .VerticalAlignment(VerticalAlignment.Center)
            .Child(
                new DockPanel()
                    .LastChildFill()
                    .Children(
                        BuildHeader(bucketName, fileName, objectKey).DockTop(),
                        BuildFooter().DockBottom(),
                        new Border()
                            .Margin(16, 12)
                            .BorderBrush(BorderColor)
                            .BorderThickness(1)
                            .Child(_editor)));
    }

    public Task<string?> ShowAsync(Window owner)
    {
        _owner = owner;
        if (owner is OssMainWindow mainWindow)
        {
            _modalOwner = mainWindow;
            mainWindow.ShowModal(this);
        }
        else
        {
            owner.OverlayLayer.Add(this);
        }
        return _completion.Task;
    }

    private FrameworkElement BuildHeader(string bucketName, string fileName, string objectKey)
    {
        return new Border()
            .Padding(18, 12)
            .Background(HeaderSurface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(3)
                    .Children(
                        new TextBlock().Text($"编辑：{fileName}").FontSize(16).Bold(),
                        new TextBlock().Text($"oss://{bucketName}/{objectKey}").FontSize(11).Foreground(MutedText)));
    }

    private FrameworkElement BuildFooter()
    {
        var transparent = Color.White.WithAlpha(0);
        return new Border()
            .Padding(18, 11)
            .Background(PanelSurface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 1, 0, 0))
            .Child(
                new Grid()
                    .Columns("*,Auto")
                    .AutoIndexing()
                    .Children(
                        new TextBlock()
                            .Text("保存后将覆盖 OSS 上的原文件（UTF-8）")
                            .FontSize(12)
                            .Foreground(MutedText)
                            .CenterVertical(),
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new Button()
                                    .Content("取消", accessKey: false)
                                    .StyleName(BuiltInStyles.FlatButton)
                                    .Padding(16, 7)
                                    .OnClick(() => Complete(null))
                                    .WithFeedback("放弃修改并关闭", transparent, BorderColor.WithAlpha(75), BorderColor),
                                new Button()
                                    .Content("保存", accessKey: false)
                                    .Padding(18, 7)
                                    .CornerRadius(6)
                                    .Background(Teal.WithAlpha(38))
                                    .BorderBrush(Teal.WithAlpha(110))
                                    .Foreground(Teal)
                                    .OnClick(() => Complete(_editor.Text))
                                    .WithFeedback(
                                        "保存并覆盖 OSS 上的原文件",
                                        Teal.WithAlpha(38),
                                        Teal.WithAlpha(58),
                                        Teal.WithAlpha(88)))));
    }

    private void Complete(string? result)
    {
        if (_modalOwner is { } modalOwner)
        {
            modalOwner.HideModal(this);
        }
        else if (_owner is { } owner && owner.OverlayLayer.Contains(this))
        {
            owner.OverlayLayer.Remove(this);
        }

        _completion.TrySetResult(result);
    }
}
