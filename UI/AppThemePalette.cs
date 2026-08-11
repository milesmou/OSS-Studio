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
                WindowBackground: Color.FromHex("#E9EEEE"),
                Surface: Color.FromHex("#F4F5F3"),
                SidebarSurface: Color.FromHex("#EEF2F1"),
                HeaderSurface: Color.FromHex("#E7ECEC"),
                PanelSurface: Color.FromHex("#EEF2F1"),
                Border: Color.FromHex("#CFD9D9"),
                RowDivider: Color.FromHex("#D6E0DE"),
                ObjectListSurface: Color.FromHex("#FFFFFF"),
                ObjectRowHover: Color.FromHex("#E6F0EE"),
                ObjectRowSelected: Color.FromHex("#C9DFDB"),
                PrimaryText: Color.FromHex("#304642"),
                MutedText: Color.FromHex("#687982"),
                Teal: Color.FromHex("#438F86"),
                Amber: Color.FromHex("#C5903D"),
                Blue: Color.FromHex("#6384AD"),
                Purple: Color.FromHex("#7D6A9D"),
                Coral: Color.FromHex("#A86F67"));

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
