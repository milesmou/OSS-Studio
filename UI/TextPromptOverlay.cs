using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace OSSStudio.UI;

internal sealed class TextPromptOverlay : ContentControl
{
    private readonly Color Surface;
    private readonly Color HeaderSurface;
    private readonly Color PanelSurface;
    private readonly Color BorderColor;
    private readonly Color MutedText;
    private readonly Color Teal;
    private readonly Color Coral;

    private readonly TextBox _input = new();
    private readonly TextBlock _error = new();
    private readonly Func<string, string?> _validate;
    private readonly TaskCompletionSource<string?> _completion = new();
    private Window? _owner;
    private OssMainWindow? _modalOwner;

    public TextPromptOverlay(
        string title,
        string message,
        string placeholder,
        string initialValue = "",
        Func<string, string?>? validate = null)
    {
        var palette = AppThemePalette.Current;
        Surface = palette.Surface;
        HeaderSurface = palette.HeaderSurface;
        PanelSurface = palette.PanelSurface;
        BorderColor = palette.Border;
        MutedText = palette.MutedText;
        Teal = palette.Teal;
        Coral = palette.Coral;
        _validate = validate ?? (value => string.IsNullOrWhiteSpace(value) ? "请输入内容" : null);
        _input.Text = initialValue;
        _input.Placeholder(placeholder).Height(36);
        _error.Foreground = Coral;
        _error.TextWrapping = TextWrapping.Wrap;

        Background = Color.Black.WithAlpha(105);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Content = new Border()
            .Width(520)
            .CornerRadius(10)
            .Background(Surface)
            .BorderBrush(BorderColor)
            .BorderThickness(1)
            .HorizontalAlignment(HorizontalAlignment.Center)
            .VerticalAlignment(VerticalAlignment.Center)
            .Child(new DockPanel()
                .LastChildFill()
                .Children(
                    new Border()
                        .Padding(20, 15)
                        .Background(HeaderSurface)
                        .BorderBrush(BorderColor)
                        .BorderThickness(new Thickness(0, 0, 0, 1))
                        .Child(new TextBlock().Text(title).FontSize(17).Bold())
                        .DockTop(),
                    BuildFooter().DockBottom(),
                    new StackPanel()
                        .Vertical()
                        .Spacing(10)
                        .Margin(20, 18)
                        .Children(
                            new TextBlock { TextWrapping = TextWrapping.Wrap }
                                .Text(message)
                                .Foreground(MutedText),
                            _input.StretchHorizontal(),
                            _error)));
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

    private FrameworkElement BuildFooter()
        => new Border()
            .Padding(20, 12)
            .Background(PanelSurface)
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
                        .Content("确定", accessKey: false)
                        .Padding(18, 7)
                        .Background(Teal.WithAlpha(38))
                        .BorderBrush(Teal.WithAlpha(110))
                        .Foreground(Teal)
                        .OnClick(Accept)
                        .WithFeedback("确认输入", Teal.WithAlpha(38), Teal.WithAlpha(58), Teal.WithAlpha(88))));

    private void Accept()
    {
        var value = _input.Text.Trim();
        var error = _validate(value);
        if (error is not null)
        {
            _error.Text = error;
            return;
        }

        Complete(value);
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
