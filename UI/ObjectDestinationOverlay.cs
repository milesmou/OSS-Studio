using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace OSSStudio.UI;

internal sealed record ObjectDestinationResult(string DirectoryPrefix, string ObjectName);

internal sealed class ObjectDestinationOverlay : ContentControl
{
    private readonly TreeView _directoryTree = new();
    private readonly TextBox _nameBox = new();
    private readonly TextBlock _selectedDirectoryText = new();
    private readonly TextBlock _treeStatusText = new();
    private readonly TextBlock _errorText = new();
    private readonly Func<string, string, string?> _validate;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<string>>> _loadChildren;
    private readonly TaskCompletionSource<ObjectDestinationResult?> _completion = new();
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly HashSet<string> _knownPrefixes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadedPrefixes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _loadingPrefixes = new(StringComparer.Ordinal);
    private readonly Color _surface;
    private readonly Color _panelSurface;
    private readonly Color _borderColor;
    private readonly Color _mutedText;
    private readonly Color _teal;
    private readonly Color _coral;
    private OssMainWindow? _owner;
    private readonly string _bucketName;
    private readonly string _rootPrefix;
    private string _selectedPrefix;

    public ObjectDestinationOverlay(
        string title,
        string bucketName,
        string rootPrefix,
        IReadOnlyList<string> folderPrefixes,
        string initialPrefix,
        string initialName,
        bool allowRename,
        string confirmText,
        Func<string, CancellationToken, Task<IReadOnlyList<string>>> loadChildren,
        Func<string, string, string?> validate)
    {
        var palette = AppThemePalette.Current;
        _surface = palette.Surface;
        _panelSurface = palette.PanelSurface;
        _borderColor = palette.Border;
        _mutedText = palette.MutedText;
        _teal = palette.Teal;
        _coral = palette.Coral;
        _validate = validate;
        _loadChildren = loadChildren;
        _bucketName = bucketName;
        _rootPrefix = rootPrefix;

        _knownPrefixes.Add(rootPrefix);
        _loadedPrefixes.Add(rootPrefix);
        foreach (var prefix in folderPrefixes)
        {
            _knownPrefixes.Add(prefix);
        }
        AddPrefixAncestors(initialPrefix);
        var nodes = BuildDirectoryNodes();
        _selectedPrefix = _knownPrefixes.Contains(initialPrefix)
            ? initialPrefix
            : rootPrefix;
        _directoryTree
            .ItemsSource(nodes)
            .OnExpanding(args =>
            {
                if (args.Item is TreeViewNode node)
                {
                    _ = LoadChildrenAsync(node);
                }
            })
            .OnSelectionChanged(item =>
            {
                if (item is TreeViewNode { Tag: string prefix })
                {
                    _selectedPrefix = prefix;
                    UpdateSelectedDirectory(bucketName);
                    _errorText.Text = string.Empty;
                }
            });
        if (FindNode(nodes, _selectedPrefix) is { } selectedNode)
        {
            _directoryTree.SelectedNode = selectedNode;
            ExpandToPrefix(nodes[0], _selectedPrefix);
        }
        if (nodes.Count > 0)
        {
            _directoryTree.Expand(nodes[0]);
            ExpandToPrefix(nodes[0], _selectedPrefix);
        }

        _nameBox.Text = initialName;
        _nameBox.IsEnabled = allowRename;
        _nameBox.Placeholder("对象名称").Height(36);
        _selectedDirectoryText.Foreground = _mutedText;
        _treeStatusText.Foreground = _mutedText;
        _treeStatusText.FontSize = 11;
        _treeStatusText.Text = "展开目录时按需加载下一层";
        _errorText.Foreground = _coral;
        _errorText.TextWrapping = TextWrapping.Wrap;
        UpdateSelectedDirectory(bucketName);

        Background = Color.Black.WithAlpha(105);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        Content = new Border()
            .Width(760)
            .Height(540)
            .CornerRadius(10)
            .Background(_surface)
            .BorderBrush(_borderColor)
            .BorderThickness(1)
            .HorizontalAlignment(HorizontalAlignment.Center)
            .VerticalAlignment(VerticalAlignment.Center)
            .Child(new DockPanel()
                .LastChildFill()
                .Children(
                    BuildHeader(title).DockTop(),
                    BuildFooter(confirmText).DockBottom(),
                    BuildBody(allowRename)));
    }

    public Task<ObjectDestinationResult?> ShowAsync(OssMainWindow owner)
    {
        _owner = owner;
        owner.ShowModal(this);
        return _completion.Task;
    }

    private FrameworkElement BuildHeader(string title)
        => new Border()
            .Padding(20, 14)
            .Background(_panelSurface)
            .BorderBrush(_borderColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .Child(new StackPanel()
                .Vertical()
                .Spacing(3)
                .Children(
                    new TextBlock().Text(title).FontSize(17).Bold(),
                    new TextBlock().Text("从目录树选择目标目录；修改名称即表示同时重命名。")
                        .FontSize(12)
                        .Foreground(_mutedText)));

    private FrameworkElement BuildBody(bool allowRename)
        => new Grid()
            .Columns("310,*")
            .Spacing(18)
            .Margin(20, 18)
            .AutoIndexing()
            .Children(
                new Border()
                    .Background(_panelSurface)
                    .BorderBrush(_borderColor)
                    .BorderThickness(1)
                    .CornerRadius(6)
                    .Padding(6)
                    .Child(new DockPanel()
                        .LastChildFill()
                        .Children(
                            new Border()
                                .Padding(6, 5)
                                .BorderBrush(_borderColor)
                                .BorderThickness(new Thickness(0, 1, 0, 0))
                                .Child(_treeStatusText)
                                .DockBottom(),
                            _directoryTree.StretchVertical())),
                new StackPanel()
                    .Vertical()
                    .Spacing(10)
                    .Children(
                        new TextBlock().Text("目标目录").Bold(),
                        new Border()
                            .Padding(10, 9)
                            .Background(_panelSurface)
                            .BorderBrush(_borderColor)
                            .BorderThickness(1)
                            .CornerRadius(5)
                            .Child(_selectedDirectoryText),
                        new TextBlock().Text(allowRename ? "对象名称（修改即重命名）" : "对象名称").Bold(),
                        _nameBox.StretchHorizontal(),
                        new TextBlock()
                            .Text(allowRename
                                ? "目录名和文件名只填写名称，不要包含 /。"
                                : "批量操作会保留每个对象的原名称。")
                            .FontSize(12)
                            .Foreground(_mutedText),
                        _errorText));

    private FrameworkElement BuildFooter(string confirmText)
        => new Border()
            .Padding(20, 12)
            .Background(_panelSurface)
            .BorderBrush(_borderColor)
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
                        .WithFeedback("取消并关闭", Color.White.WithAlpha(0), _borderColor.WithAlpha(75), _borderColor),
                    new Button()
                        .Content(confirmText, accessKey: false)
                        .Padding(18, 7)
                        .Background(_teal.WithAlpha(38))
                        .BorderBrush(_teal.WithAlpha(110))
                        .Foreground(_teal)
                        .OnClick(Accept)
                        .WithFeedback(confirmText, _teal.WithAlpha(38), _teal.WithAlpha(58), _teal.WithAlpha(88))));

    private void Accept()
    {
        var name = _nameBox.Text.Trim();
        var error = _validate(_selectedPrefix, name);
        if (error is not null)
        {
            _errorText.Text = error;
            return;
        }

        Complete(new ObjectDestinationResult(_selectedPrefix, name));
    }

    private void UpdateSelectedDirectory(string bucketName)
        => _selectedDirectoryText.Text = $"oss://{bucketName}/{_selectedPrefix}";

    private void Complete(ObjectDestinationResult? result)
    {
        if (_owner is { } owner)
        {
            owner.HideModal(this);
        }
        _lifetimeCancellation.Cancel();
        _completion.TrySetResult(result);
    }

    private IReadOnlyList<TreeViewNode> BuildDirectoryNodes()
    {
        var root = new FolderNode(_bucketName, _rootPrefix);
        foreach (var prefix in _knownPrefixes.Where(prefix => prefix != _rootPrefix))
        {
            var relative = prefix.StartsWith(_rootPrefix, StringComparison.Ordinal)
                ? prefix[_rootPrefix.Length..]
                : prefix;
            var segments = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var current = root;
            var currentPrefix = _rootPrefix;
            foreach (var segment in segments)
            {
                currentPrefix += segment + "/";
                current = current.GetOrAdd(segment, currentPrefix);
            }
        }

        return [ToTreeNode(root, isRoot: true)];
    }

    private TreeViewNode ToTreeNode(FolderNode folder, bool isRoot = false)
        => new(
            isRoot ? $"▰  {folder.Name}" : $"▰  {folder.Name}",
            [.. folder.Children.Values
                .OrderBy(child => child.Name, StringComparer.OrdinalIgnoreCase)
                .Select(child => ToTreeNode(child)),
             .. (_loadedPrefixes.Contains(folder.Prefix) || folder.Children.Count > 0
                ? Array.Empty<TreeViewNode>()
                : [new TreeViewNode("正在加载…")])],
            folder.Prefix);

    private async Task LoadChildrenAsync(TreeViewNode node)
    {
        if (node.Tag is not string prefix ||
            _loadedPrefixes.Contains(prefix) ||
            !_loadingPrefixes.Add(prefix))
        {
            return;
        }

        _directoryTree.IsEnabled = false;
        _treeStatusText.Text = $"正在加载 oss://{_bucketName}/{prefix}";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var children = await _loadChildren(prefix, timeout.Token);
            foreach (var child in children)
            {
                _knownPrefixes.Add(child);
            }
            _loadedPrefixes.Add(prefix);
            RebuildTree(prefix);
            _treeStatusText.Text = children.Count == 0 ? "该目录没有子目录" : $"已加载 {children.Count} 个子目录";
        }
        catch (OperationCanceledException) when (!_lifetimeCancellation.IsCancellationRequested)
        {
            _treeStatusText.Text = "加载超时，请再次展开重试";
        }
        catch (Exception exception)
        {
            _treeStatusText.Text = $"加载失败：{exception.Message}";
        }
        finally
        {
            _loadingPrefixes.Remove(prefix);
            _directoryTree.IsEnabled = true;
        }
    }

    private void RebuildTree(string expandPrefix)
    {
        var nodes = BuildDirectoryNodes();
        _directoryTree.ItemsSource(nodes);
        if (FindNode(nodes, _selectedPrefix) is { } selectedNode)
        {
            _directoryTree.SelectedNode = selectedNode;
            ExpandToPrefix(nodes[0], _selectedPrefix);
        }
        foreach (var prefix in _loadedPrefixes.Append(expandPrefix))
        {
            if (FindNode(nodes, prefix) is { } node)
            {
                ExpandToPrefix(nodes[0], prefix);
                _directoryTree.Expand(node);
            }
        }
    }

    private void AddPrefixAncestors(string prefix)
    {
        if (!prefix.StartsWith(_rootPrefix, StringComparison.Ordinal))
        {
            return;
        }
        var current = _rootPrefix;
        foreach (var segment in prefix[_rootPrefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current += segment + "/";
            _knownPrefixes.Add(current);
        }
    }

    private static TreeViewNode? FindNode(IEnumerable<TreeViewNode> nodes, string prefix)
    {
        foreach (var node in nodes)
        {
            if (node.Tag is string nodePrefix && nodePrefix == prefix)
            {
                return node;
            }
            if (FindNode(node.Children, prefix) is { } child)
            {
                return child;
            }
        }
        return null;
    }

    private bool ExpandToPrefix(TreeViewNode node, string prefix)
    {
        if (node.Tag is string nodePrefix && nodePrefix == prefix)
        {
            return true;
        }
        foreach (var child in node.Children)
        {
            if (ExpandToPrefix(child, prefix))
            {
                _directoryTree.Expand(node);
                return true;
            }
        }
        return false;
    }

    private sealed class FolderNode(string name, string prefix)
    {
        public string Name { get; } = name;
        public string Prefix { get; } = prefix;
        public Dictionary<string, FolderNode> Children { get; } = new(StringComparer.Ordinal);

        public FolderNode GetOrAdd(string name, string childPrefix)
        {
            if (!Children.TryGetValue(name, out var child))
            {
                child = new FolderNode(name, childPrefix);
                Children[name] = child;
            }
            return child;
        }
    }
}
