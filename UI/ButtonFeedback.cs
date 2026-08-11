using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace OSSStudio.UI;

internal static class ButtonFeedback
{
    public static Button WithFeedback(
        this Button button,
        string toolTip,
        Color normal,
        Color hover,
        Color pressed)
    {
        button.ToolTip = new TextBlock().Text(toolTip).FontSize(12);
        button
            .OnMouseEnter(() => button.Background = hover)
            .OnMouseDown(_ => button.Background = pressed)
            .OnMouseUp(_ => button.Background = hover)
            .OnMouseLeave(() => button.Background = normal);
        return button;
    }
}
