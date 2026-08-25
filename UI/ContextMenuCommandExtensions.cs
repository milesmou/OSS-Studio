using System.Runtime.CompilerServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace OSSStudio.UI;

internal static class ContextMenuCommandExtensions
{
    private static readonly ConditionalWeakTable<ContextMenu, MenuCommandScope> MenuScopes = new();
    private static long _nextCommandId;

    public static ContextMenu ActionItem(this ContextMenu menu, string text, Action execute)
    {
        ArgumentNullException.ThrowIfNull(menu);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentNullException.ThrowIfNull(execute);

        var state = MenuScopes.GetValue(menu, static currentMenu => new MenuCommandScope(currentMenu));
        var command = new Command(
            $"oss-studio.context-menu.{Interlocked.Increment(ref _nextCommandId)}",
            text);
        state.Scope.Register(command, execute);
        menu.Item(text, command);
        return menu;
    }

    private sealed class MenuCommandScope
    {
        public MenuCommandScope(ContextMenu menu)
        {
            menu.SetCommandTarget(CommandTarget.From(Scope));
        }

        public CommandScope Scope { get; } = new();
    }
}
