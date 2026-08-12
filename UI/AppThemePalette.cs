using Aprillz.MewUI;
using Microsoft.Win32;

namespace OSSStudio.UI;

internal sealed record ThemePalette(
    Color WindowBackground,
    Color Surface,
    Color SidebarSurface,
    Color HeaderSurface,
    Color PanelSurface,
    Color Border,
    Color RowDivider,
    Color ObjectListSurface,
    Color ObjectRowHover,
    Color ObjectRowSelected,
    Color PrimaryText,
    Color MutedText,
    Color Teal,
    Color Amber,
    Color Blue,
    Color Purple,
    Color Coral);

internal static class AppThemePalette
{
    public static ThemePalette Current { get; private set; } = Create(useDarkPalette: false);

    public static void Initialize(string themeMode)
        => Current = Create(UsesDarkPalette(themeMode));

    private static ThemePalette Create(bool useDarkPalette)
        => useDarkPalette
            ? new ThemePalette(
                WindowBackground: Color.FromHex("#182022"),
                Surface: Color.FromHex("#222C2E"),
                SidebarSurface: Color.FromHex("#202A2C"),
                HeaderSurface: Color.FromHex("#273235"),
                PanelSurface: Color.FromHex("#1F292B"),
                Border: Color.FromHex("#445356"),
                RowDivider: Color.FromHex("#344245"),
                ObjectListSurface: Color.FromHex("#1B2426"),
                ObjectRowHover: Color.FromHex("#2A3939"),
                ObjectRowSelected: Color.FromHex("#31504B"),
                PrimaryText: Color.FromHex("#E2EBE9"),
                MutedText: Color.FromHex("#A8B7B9"),
                Teal: Color.FromHex("#72C2B7"),
                Amber: Color.FromHex("#E0B36A"),
                Blue: Color.FromHex("#8FB1DA"),
                Purple: Color.FromHex("#B09DCE"),
                Coral: Color.FromHex("#D7978E"))
            : new ThemePalette(
                WindowBackground: Color.FromHex("#EFF6F4"),
                Surface: Color.FromHex("#F7FAF9"),
                SidebarSurface: Color.FromHex("#E3EEEB"),
                HeaderSurface: Color.FromHex("#E7F1EF"),
                PanelSurface: Color.FromHex("#E8F2EF"),
                Border: Color.FromHex("#C9DDD8"),
                RowDivider: Color.FromHex("#D6E5E1"),
                ObjectListSurface: Color.FromHex("#F8FAF9"),
                ObjectRowHover: Color.FromHex("#DFEEEA"),
                ObjectRowSelected: Color.FromHex("#C5E0DA"),
                PrimaryText: Color.FromHex("#243632"),
                MutedText: Color.FromHex("#617874"),
                Teal: Color.FromHex("#39847E"),
                Amber: Color.FromHex("#B2874B"),
                Blue: Color.FromHex("#5F789D"),
                Purple: Color.FromHex("#796F96"),
                Coral: Color.FromHex("#B66F64"));

    private static bool UsesDarkPalette(string themeMode)
    {
        if (themeMode == "Dark")
        {
            return true;
        }
        if (themeMode != "System" || !OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                1) is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }
}
