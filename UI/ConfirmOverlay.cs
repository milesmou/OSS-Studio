using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace OSSStudio.UI;

public sealed class ConfirmOverlay : ContentControl
{
    private static readonly Color Surface = Color.FromHex("#F4F5F3");
    private static readonly Color BorderColor = Color.FromHex("#CFD9D9");
    private static readonly Color MutedText = Color.FromHex("#687982");
    private static readonly Color Amber = Color.FromHex("#C5903D");
    private static readonly Color Coral = Color.FromHex("#A86F67");

    private readonly TaskCompletionSource<bool> _completion = new();
    private Window? _owner;
    private OssMainWindow? _modalOwner;

    public ConfirmOverlay(string title, string message, string detail, string confirmText = "删除")
    {
        Background = Color.Black.WithAlpha(105);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        Content = new Border()
            .Width(480)
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
                        BuildFooter(confirmText).DockBottom(),
                        new StackPanel()
                            .Horizontal()
                            .Spacing(14)
                            .Margin(22, 20)
                            .Children(
                                new Border()
                                    .Width(38)
                                    .Height(38)
                                    .CornerRadius(19)
                                    .Background(Amber.WithAlpha(42))
                                    .BorderBrush(Amber.WithAlpha(120))
                                    .BorderThickness(1)
                                    .Child(new TextBlock().Text("!").FontSize(22).Bold().Foreground(Amber).Center()),
                                new StackPanel()
                                    .Vertical()
                                    .Spacing(7)
                                    .Children(
                                        new TextBlock().Text(title).FontSize(16).Bold(),
                                        new TextBlock().Text(message).TextWrapping(TextWrapping.Wrap),
                                        new TextBlock().Text(detail).FontSize(12).Foreground(MutedText).TextWrapping(TextWrapping.Wrap)))));
    }

    public Task<bool> ShowAsync(Window owner)
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

    private FrameworkElement BuildFooter(string confirmText)
    {
        return new Border()
            .Padding(18, 12)
            .Background(Color.FromHex("#EEF2F1"))
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 1, 0, 0))
            .Child(
                new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .HorizontalAlignment(HorizontalAlignment.Right)
                    .Children(
                        new Button()
                            .Content("取消", accessKey: false)
                            .StyleName(BuiltInStyles.FlatButton)
                            .Padding(16, 7)
                            .OnClick(() => Complete(false))
                            .WithFeedback("取消当前操作", Color.White.WithAlpha(0), BorderColor.WithAlpha(75), BorderColor),
                        new Button()
                            .Content(confirmText, accessKey: false)
                            .Padding(16, 7)
                            .CornerRadius(6)
                            .Background(Coral.WithAlpha(38))
                            .BorderBrush(Coral.WithAlpha(110))
                            .Foreground(Coral)
                            .OnClick(() => Complete(true))
                            .WithFeedback(
                                $"确认{confirmText}",
                                Coral.WithAlpha(38),
                                Coral.WithAlpha(58),
                                Coral.WithAlpha(88))));
    }

    private void Complete(bool result)
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
