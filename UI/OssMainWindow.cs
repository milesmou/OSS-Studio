using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using OSSStudio.Models;
using OSSStudio.Services;

namespace OSSStudio.UI;

public sealed class OssMainWindow : Window
{
    private readonly Color Surface;
    private readonly Color SidebarSurface;
    private readonly Color HeaderSurface;
    private readonly Color BorderColor;
    private readonly Color RowDividerColor;
    private readonly Color ObjectListSurface;
    private readonly Color ObjectRowHover;
    private readonly Color ObjectRowSelected;
    private readonly Color MutedText;
    private readonly Color PrimaryText;
    private readonly Color Teal;
    private readonly Color Amber;
    private readonly Color Blue;
    private readonly Color Purple;
    private readonly Color Coral;
    private const string AppIconResourceName = "OSSStudio.Assets.OSS-Studio.ico";
    private const string AppLogoResourceName = "OSSStudio.Assets.OSS-Studio.png";
    private const long ObjectDoubleClickIntervalMilliseconds = 200;

    private readonly WorkspaceState _state;
    private readonly WorkspaceStateStore _stateStore;
    private readonly BucketCredentialStore _credentialStore = new();
    private readonly OssObjectService _ossObjectService;
    private readonly TabControl _tabs = new();
    private readonly TreeView _bucketTree = new();
    private readonly StackPanel _bookmarkList = new StackPanel().Vertical().Spacing(3);
    private readonly ScrollViewer _bookmarkScrollViewer = new();
    private readonly TextBlock _bucketCountText = new();
    private readonly TextBlock _bookmarkCountText = new();
    private readonly ContextMenu _bucketContextMenu = new();
    private readonly ContextMenu _bookmarkContextMenu = new();
    private ContextMenu? _objectContextMenu;
    private readonly Grid _modalLayer = new();
    private readonly Dictionary<TabItem, string> _bucketIdsByTab = [];
    private readonly Dictionary<string, TreeViewNode> _bucketNodesById = [];
    private readonly Dictionary<string, GridView> _objectViews = [];
    private readonly Dictionary<string, ObservableValue<string>> _statusTexts = [];
    private readonly Dictionary<string, ObservableValue<string>> _pathTexts = [];
    private readonly Dictionary<string, string> _searchTexts = [];
    private readonly Dictionary<string, Button> _bookmarkButtons = [];
    private readonly Dictionary<string, BucketTabState> _tabStates = [];
    private readonly Dictionary<string, CheckBox> _headerCheckBoxes = [];
    private readonly HashSet<string> _updatingHeaderCheckBoxes = [];
    private readonly Dictionary<Border, (string BucketId, string Key)> _rowCellAssignments = [];
    private readonly Dictionary<(string BucketId, string Key), HashSet<Border>> _rowCellsByObject = [];
    private readonly Dictionary<string, BucketCredential> _sessionCredentials = [];
    private readonly List<TransferTaskInfo> _transferTasks = [];

    private TransferTasksOverlay? _transferTasksOverlay;
    private DirectoryBookmark? _selectedBookmark;

    private bool _suppressBucketOpen;
    private bool _hasLoaded;

    internal bool RestartRequested { get; private set; }

    public OssMainWindow(WorkspaceState state, WorkspaceStateStore stateStore)
    {
        _state = state;
        _stateStore = stateStore;
        _ossObjectService = new OssObjectService(state.UploadConcurrency, state.DownloadConcurrency);
        AppThemePalette.Initialize(state.ThemeMode);
        var palette = AppThemePalette.Current;
        Surface = palette.Surface;
        SidebarSurface = palette.SidebarSurface;
        HeaderSurface = palette.HeaderSurface;
        BorderColor = palette.Border;
        RowDividerColor = palette.RowDivider;
        ObjectListSurface = palette.ObjectListSurface;
        ObjectRowHover = palette.ObjectRowHover;
        ObjectRowSelected = palette.ObjectRowSelected;
        MutedText = palette.MutedText;
        PrimaryText = palette.PrimaryText;
        Teal = palette.Teal;
        Amber = palette.Amber;
        Blue = palette.Blue;
        Purple = palette.Purple;
        Coral = palette.Coral;
        Title = "OSS Studio";
        Icon = IconSource.FromResource(typeof(OssMainWindow).Assembly, AppIconResourceName);
        WindowSize = WindowSize.Resizable(1180, 760, minWidth: 900, minHeight: 620);
        Padding = new Thickness(0);
        Background = palette.WindowBackground;

        _bucketContextMenu
            .Item("编辑资源桶", EditSelectedBucket)
            .Separator()
            .Item("删除资源桶", DeleteSelectedBucket);

        _bucketTree
            .ItemsSource(CreateBucketNodes())
            .OnSelectionChanged(OnBucketSelected)
            .OnMouseDown(args =>
            {
                if (args.Button != MouseButton.Right || !_bucketTree.TryGetItemIndexAt(args, out var index))
                {
                    return;
                }

                if (_bucketTree.ItemsSource.GetItem(index) is TreeViewNode node)
                {
                    _suppressBucketOpen = true;
                    _bucketTree.SelectedNode = node;
                    _suppressBucketOpen = false;
                    _bucketContextMenu.ShowAt(_bucketTree, ScreenToClient(args.ScreenPosition));
                    args.Handled = true;
                }
            });

        _bookmarkContextMenu
            .Item("打开书签", OpenSelectedBookmark)
            .Item("自定义显示名称", CustomizeSelectedBookmarkName)
            .Separator()
            .Item("删除书签", DeleteSelectedBookmark);

        _bookmarkScrollViewer.Content = _bookmarkList;
        _bookmarkScrollViewer.HorizontalScroll = ScrollMode.Disabled;
        _bookmarkScrollViewer.VerticalScroll = ScrollMode.Auto;
        RefreshBookmarkTree();

        _tabs.OnSelectionChanged(OnTabSelectionChanged);

        var mainContent = new DockPanel()
            .LastChildFill()
            .Children(
                BuildTopBar().DockTop(),
                new Grid()
                    .Columns("250,*")
                    .AutoIndexing()
                    .Children(BuildSidebar(), BuildWorkspace()));
        _modalLayer.IsHitTestVisible = false;
        Content = new Grid().Children(mainContent, _modalLayer);

        RestoreTabs();
        Loaded += OnWindowLoaded;
        Closed += SaveWorkspace;
    }

    internal void ShowModal(UIElement modal)
    {
        _modalLayer.IsHitTestVisible = true;
        _modalLayer.Add(modal);
    }

    internal void HideModal(UIElement modal)
    {
        if (_modalLayer.Children.Contains(modal))
        {
            _modalLayer.Remove(modal);
        }

        _modalLayer.IsHitTestVisible = _modalLayer.Children.Count > 0;
    }

    internal void RestoreAndActivate()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }
        Activate();
    }

    private FrameworkElement BuildTopBar()
    {
        var brand = new StackPanel()
            .Horizontal()
            .Spacing(10)
            .CenterVertical()
            .Children(
                new Image()
                    .SourceResource(typeof(OssMainWindow).Assembly, AppLogoResourceName)
                    .Width(32)
                    .Height(32)
                    .StretchMode(Stretch.Uniform),
                new TextBlock().Text("OSS Studio").FontSize(16).Bold().CenterVertical());

        var windowActions = new StackPanel()
            .Horizontal()
            .Spacing(6)
            .CenterVertical()
            .Children(
                FlatButton("传输任务", ShowTransferTasks),
                FlatButton("设置", ShowSettings));

        return new Border()
            .Height(54)
            .Padding(16, 9)
            .Background(HeaderSurface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .Child(
                new Grid()
                    .Columns("Auto,*,Auto")
                    .AutoIndexing()
                    .Spacing(18)
                    .Children(
                        brand,
                        new Border(),
                        windowActions));
    }

    private FrameworkElement BuildSidebar()
    {
        _bucketCountText.Text = $"资源桶（{_state.Buckets.Count}）";
        _bucketCountText.Foreground = MutedText;
        _bookmarkCountText.Text = $"书签（{_state.Bookmarks.Count}）";
        _bookmarkCountText.Foreground = MutedText;

        var treeArea = new DockPanel()
            .LastChildFill()
            .Children(
                new Grid()
                    .Columns("*,Auto,Auto")
                    .AutoIndexing()
                    .Spacing(4)
                    .Margin(4, 0, 4, 8)
                    .Children(
                        _bucketCountText.CenterVertical(),
                        FlatButton("＋ 添加", AddBucket),
                        FlatButton("刷新", RefreshActiveBucket))
                    .DockTop(),
                _bucketTree.StretchVertical());

        var bookmarkArea = new DockPanel()
            .LastChildFill()
            .Children(
                new Grid()
                    .Margin(4, 10, 4, 8)
                    .Children(_bookmarkCountText.CenterVertical())
                    .DockTop(),
                new Border()
                    .CornerRadius(3)
                    .Background(Surface)
                    .BorderBrush(BorderColor)
                    .BorderThickness(1)
                    .Child(_bookmarkScrollViewer.StretchVertical()));

        return new Border()
            .Background(SidebarSurface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 0, 1, 0))
            .Padding(12)
            .Child(
                new Grid()
                    .Rows("*,*")
                    .AutoIndexing()
                    .Children(treeArea, bookmarkArea));
    }

    private FrameworkElement BuildWorkspace()
    {
        return new Border()
            .Background(Surface)
            .Child(_tabs.StretchHorizontal().StretchVertical());
    }

    private void RestoreTabs()
    {
        foreach (var bucketId in _state.OpenBucketIds.ToArray())
        {
            var bucket = FindBucket(bucketId);
            if (bucket is not null)
            {
                AddBucketTab(bucket, select: false);
            }
        }

        if (_tabs.Tabs.Count == 0)
        {
            var first = _state.Buckets.FirstOrDefault();
            if (first is not null)
            {
                AddBucketTab(first, select: true);
            }
            return;
        }

        var selectedIndex = _tabs.Tabs
            .Select((tab, index) => new { tab, index })
            .FirstOrDefault(pair => _bucketIdsByTab[pair.tab] == _state.ActiveBucketId)?.index ?? 0;

        _tabs.SelectedIndex = selectedIndex;
    }

    private void OnBucketSelected(object? item)
    {
        if (!_suppressBucketOpen && item is TreeViewNode { Tag: BucketProfile bucket })
        {
            OpenBucket(bucket);
        }
    }

    private void OpenSelectedBookmark()
    {
        if (_selectedBookmark is { } bookmark)
        {
            OpenBookmark(bookmark);
        }
    }

    private void OpenBookmark(DirectoryBookmark bookmark)
    {
        var bucket = FindBucket(bookmark.BucketId);
        if (bucket is null)
        {
            _state.Bookmarks.RemoveAll(item => item.Id == bookmark.Id);
            RefreshBookmarkTree();
            SaveWorkspace();
            return;
        }

        OpenBucket(bucket);
        _ = NavigateToPrefixAsync(bucket, bookmark.Prefix);
    }

    private void AddBookmark(BucketProfile bucket, string prefix)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : prefix.Trim().TrimStart('/').TrimEnd('/') + "/";
        if (_state.Bookmarks.Any(bookmark =>
                bookmark.BucketId == bucket.Id &&
                string.Equals(bookmark.Prefix, normalizedPrefix, StringComparison.Ordinal)))
        {
            SetStatus(bucket.Id, "当前目录已在书签中");
            return;
        }

        _state.Bookmarks.Add(new DirectoryBookmark(Guid.NewGuid().ToString("N"), bucket.Id, normalizedPrefix));
        RefreshBookmarkTree();
        SaveWorkspace();
        var message = $"已添加书签：oss://{bucket.Name}/{normalizedPrefix}";
        SetStatus(bucket.Id, message);
        this.ShowToast(message);
    }

    private void ToggleCurrentBookmark(BucketProfile bucket)
    {
        bucket = FindBucket(bucket.Id) ?? bucket;
        var prefix = GetTabState(bucket).CurrentPrefix;
        var existing = _state.Bookmarks.FirstOrDefault(bookmark =>
            bookmark.BucketId == bucket.Id &&
            string.Equals(bookmark.Prefix, prefix, StringComparison.Ordinal));
        if (existing is null)
        {
            AddBookmark(bucket, prefix);
            return;
        }

        _state.Bookmarks.Remove(existing);
        if (_selectedBookmark?.Id == existing.Id)
        {
            _selectedBookmark = null;
        }
        RefreshBookmarkTree();
        SaveWorkspace();
        var message = $"已取消收藏：oss://{bucket.Name}/{prefix}";
        SetStatus(bucket.Id, message);
        this.ShowToast(message);
    }

    private void UpdateAllBookmarkButtons()
    {
        foreach (var bucketId in _bookmarkButtons.Keys.ToArray())
        {
            if (FindBucket(bucketId) is { } bucket)
            {
                UpdateBookmarkButton(bucket);
            }
        }
    }

    private void UpdateBookmarkButton(BucketProfile bucket)
    {
        if (!_bookmarkButtons.TryGetValue(bucket.Id, out var button))
        {
            return;
        }

        var prefix = GetTabState(bucket).CurrentPrefix;
        var isBookmarked = _state.Bookmarks.Any(bookmark =>
            bookmark.BucketId == bucket.Id &&
            string.Equals(bookmark.Prefix, prefix, StringComparison.Ordinal));
        button.Content(isBookmarked ? "★ 已收藏" : "☆ 未收藏", accessKey: false);
        button.Foreground = isBookmarked ? Amber : PrimaryText;
    }

    private void DeleteSelectedBookmark()
    {
        if (_selectedBookmark is not { } bookmark)
        {
            return;
        }

        _state.Bookmarks.RemoveAll(item => item.Id == bookmark.Id);
        _selectedBookmark = null;
        RefreshBookmarkTree();
        SaveWorkspace();
        SetStatus("已删除书签");
    }

    private async void CustomizeSelectedBookmarkName()
    {
        if (_selectedBookmark is not { } bookmark)
        {
            return;
        }

        var prompt = new TextPromptOverlay(
            "自定义书签显示名称",
            "输入书签名称；留空会恢复为目录名称。",
            "书签显示名称",
            bookmark.DisplayName,
            validate: _ => null);
        var displayName = await prompt.ShowAsync(this);
        if (displayName is null)
        {
            return;
        }

        var index = _state.Bookmarks.FindIndex(item => item.Id == bookmark.Id);
        if (index < 0)
        {
            return;
        }

        var updated = bookmark with { DisplayName = displayName.Trim() };
        _state.Bookmarks[index] = updated;
        _selectedBookmark = updated;
        RefreshBookmarkTree();
        SaveWorkspace();
        SetStatus("已更新书签显示名称");
    }

    private void RefreshBookmarkTree()
    {
        while (_bookmarkList.Children.Count > 0)
        {
            _bookmarkList.RemoveAt(0);
        }

        foreach (var bookmark in _state.Bookmarks)
        {
            var bucket = FindBucket(bookmark.BucketId);
            if (bucket is null)
            {
                continue;
            }

            var normalBackground = Surface;
            var text = new TextBlock()
                .Text(GetBookmarkDisplayText(bookmark, bucket))
                .TextTrimming(TextTrimming.CharacterEllipsis)
                .CenterVertical();
            var button = new Button()
                .Content(text)
                .StyleName(BuiltInStyles.FlatButton)
                .Padding(8, 6)
                .CornerRadius(0)
                .Background(normalBackground)
                .HorizontalAlignment(HorizontalAlignment.Stretch)
                .OnClick(() =>
                {
                    _selectedBookmark = bookmark;
                    OpenBookmark(bookmark);
                })
                .WithFeedback("打开书签", normalBackground, ObjectRowHover, ObjectRowSelected);
            button.OnMouseDown(args =>
            {
                if (args.Button != MouseButton.Right)
                {
                    return;
                }

                _selectedBookmark = bookmark;
                _bookmarkContextMenu.ShowAt(_bookmarkScrollViewer, ScreenToClient(args.ScreenPosition));
                args.Handled = true;
            });
            _bookmarkList.Add(button);
        }

        _bookmarkCountText.Text = $"书签（{_state.Bookmarks.Count}）";
        UpdateAllBookmarkButtons();
    }

    private void OpenBucket(BucketProfile bucket)
    {
        var existing = _bucketIdsByTab.FirstOrDefault(pair => pair.Value == bucket.Id).Key;
        if (existing is not null)
        {
            _tabs.SelectedIndex = FindTabIndex(existing);
            return;
        }

        AddBucketTab(bucket, select: true);
        SaveWorkspace();
    }

    private void AddBucketTab(BucketProfile bucket, bool select)
    {
        _tabStates.TryAdd(bucket.Id, new BucketTabState(bucket.Prefix));
        var closeButton = new Button()
            .Content("×", accessKey: false)
            .StyleName(BuiltInStyles.FlatButton)
            .Width(24)
            .Height(24)
            .Padding(0)
            .Foreground(MutedText)
            .OnClick(() => CloseBucket(bucket.Id))
            .WithFeedback("关闭当前资源桶标签页", Surface, HeaderSurface, BorderColor);

        var header = new StackPanel()
            .Horizontal()
            .Spacing(8)
            .CenterVertical()
            .Children(
                new TextBlock().Text($"▰  {bucket.Name}").CenterVertical(),
                closeButton);

        var tab = new TabItem
        {
            Header = header,
            HeaderText = bucket.Name,
            Content = BuildBucketPage(bucket)
        };

        _bucketIdsByTab[tab] = bucket.Id;
        _tabs.AddTab(tab);

        if (!_state.OpenBucketIds.Contains(bucket.Id))
        {
            _state.OpenBucketIds.Add(bucket.Id);
        }

        if (select)
        {
            _tabs.SelectedIndex = _tabs.Tabs.Count - 1;
        }

        if (_hasLoaded)
        {
            _ = RefreshBucketAsync(bucket);
        }
    }

    private FrameworkElement BuildBucketPage(BucketProfile bucket)
    {
        var status = new ObservableValue<string>($"{bucket.Objects.Count} 个对象 · 已选择 0 个");
        _statusTexts[bucket.Id] = status;
        var pathText = new ObservableValue<string>(GetDisplayPath(bucket));
        _pathTexts[bucket.Id] = pathText;
        var searchBox = new TextBox
        {
            Text = _searchTexts.GetValueOrDefault(bucket.Id, string.Empty)
        };
        searchBox
            .Placeholder("搜索当前目录")
            .Height(30)
            .OnTextChanged(text =>
            {
                _searchTexts[bucket.Id] = text.Trim();
                ApplyCurrentFilter(bucket.Id);
            });

        const double nameColumnWidth = 420d;
        var objectView = new GridView()
            .RowHeight(38)
            .HeaderHeight(38)
            .ZebraStriping(false)
            .ShowGridLines(false)
            .Background(ObjectListSurface)
            .ItemsSource(bucket.Objects)
            .Columns(CreateObjectColumns(bucket.Id, nameColumnWidth));
        objectView.CellPadding = new Thickness(0);
        objectView.OnMouseDown(args => HandleObjectViewMouseDown(objectView, bucket, args));
        _objectViews[bucket.Id] = objectView;

        var headerCheckBox = new CheckBox
        {
            IsThreeState = true,
            IsChecked = false
        };
        headerCheckBox.CheckedChanged += isChecked =>
        {
            if (!_updatingHeaderCheckBoxes.Contains(bucket.Id) && isChecked.HasValue)
            {
                SetAllObjects(bucket, isChecked.Value);
            }
        };
        _headerCheckBoxes[bucket.Id] = headerCheckBox;
        var headerSelector = new Border()
            .Height(38)
            .Background(HeaderSurface)
            .BorderBrush(RowDividerColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .HorizontalAlignment(HorizontalAlignment.Stretch)
            .VerticalAlignment(VerticalAlignment.Top)
            .Child(
                new Grid()
                    .Columns("42,420,120,175,150")
                    .AutoIndexing()
                    .Children(
                        headerCheckBox.Center(),
                        CreateHeaderText("名称"),
                        CreateHeaderText("大小"),
                        CreateHeaderText("最后修改时间"),
                        CreateHeaderText("操作")));

        var dropSurface = new Border()
            .Background(ObjectListSurface)
            .BorderBrush(Color.White.WithAlpha(0))
            .BorderThickness(2)
            .Child(new Grid().Children(objectView, headerSelector));
        ConfigureDropUpload(dropSurface, bucket);

        var bookmarkButton = BrowserToolbarButton(
            "☆ 未收藏",
            "收藏或取消收藏当前 OSS 目录",
            () => ToggleCurrentBookmark(bucket));
        _bookmarkButtons[bucket.Id] = bookmarkButton;
        UpdateBookmarkButton(bucket);

        var navigationBar = new Border()
            .Padding(8, 6)
            .Background(Surface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .Child(
                new Grid()
                    .Columns("Auto,Auto,Auto,Auto,Auto,Auto,*")
                    .AutoIndexing()
                    .Children(
                        BrowserToolbarButton("←", "返回上一个访问目录", () => NavigateHistory(bucket, -1)),
                        BrowserToolbarButton("→", "前进到下一个访问目录", () => NavigateHistory(bucket, 1)),
                        BrowserToolbarButton("↑", "返回上一级目录", () => NavigateUp(bucket)),
                        BrowserToolbarButton("↻", "刷新当前目录", () => _ = RefreshBucketAsync(bucket)),
                        BrowserToolbarButton("⌂", "返回资源桶预设目录", () => _ = NavigateToPrefixAsync(bucket, bucket.Prefix)),
                        bookmarkButton,
                        new Border()
                            .Margin(4, 0, 0, 0)
                            .Padding(10, 5)
                            .CornerRadius(3)
                            .Background(HeaderSurface)
                            .BorderBrush(BorderColor)
                            .BorderThickness(1)
                            .Child(new TextBlock().BindText(pathText).CenterVertical())));

        var commandBar = new Border()
            .Padding(8, 5)
            .Background(Surface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .Child(
                new Grid()
                    .Columns("Auto,*,220")
                    .AutoIndexing()
                    .Children(
                new StackPanel()
                    .Horizontal()
                    .Spacing(4)
                    .CenterVertical()
                    .Children(
                        BrowserToolbarButton("↥ 文件", "上传一个或多个文件到当前目录", () => _ = ChooseFilesToUploadAsync(bucket)),
                        BrowserToolbarButton("↥ 目录", "上传本地目录到当前目录", () => _ = ChooseFolderToUploadAsync(bucket)),
                        BrowserToolbarButton("✚ 创建目录", "在当前路径创建新目录", () => _ = CreateFolderAsync(bucket)),
                        BrowserToolbarButton("□ 全选", "选择或取消选择当前目录中的全部对象", () => ToggleAllObjects(bucket)),
                        BrowserToolbarButton("⇩ 下载", "下载当前选中的文件或目录", () => DownloadSelected(bucket.Id)),
                        BrowserToolbarButton("▣ 复制", "复制当前选中的对象到其它 OSS 目录", () => _ = CopySelectedAsync(bucket.Id))),
                new Border(),
                searchBox.Margin(8, 0, 0, 0).StretchHorizontal().CenterVertical()));

        var statusBar = new Border()
            .Padding(14, 7)
            .Background(HeaderSurface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 1, 0, 0))
            .Child(
                new Grid()
                    .Columns("*,Auto")
                    .AutoIndexing()
                    .Children(
                        new TextBlock().BindText(status).Foreground(MutedText),
                        new TextBlock().Text($"{bucket.RegionName} · HTTPS").Foreground(MutedText)));

        return new DockPanel()
            .LastChildFill()
            .Children(
                navigationBar.DockTop(),
                commandBar.DockTop(),
                statusBar.DockBottom(),
                dropSurface);
    }

    private void CloseBucket(string bucketId)
    {
        var pair = _bucketIdsByTab.FirstOrDefault(item => item.Value == bucketId);
        if (pair.Key is null)
        {
            return;
        }

        var index = FindTabIndex(pair.Key);
        _tabs.RemoveTabAt(index);
        _bucketIdsByTab.Remove(pair.Key);
        _objectViews.Remove(bucketId);
        _statusTexts.Remove(bucketId);
        _pathTexts.Remove(bucketId);
        _searchTexts.Remove(bucketId);
        _bookmarkButtons.Remove(bucketId);
        if (_tabStates.Remove(bucketId, out var tabState))
        {
            tabState.TransferCancellation?.Cancel();
        }
        _headerCheckBoxes.Remove(bucketId);
        _updatingHeaderCheckBoxes.Remove(bucketId);
        ClearRowCellBindings(bucketId);
        ClearObjectHover(bucketId);
        _state.OpenBucketIds.Remove(bucketId);

        if (_tabs.SelectedTab is { } selected && _bucketIdsByTab.TryGetValue(selected, out var selectedBucketId))
        {
            _state.ActiveBucketId = selectedBucketId;
        }
        else
        {
            _state.ActiveBucketId = string.Empty;
        }

        SaveWorkspace();
    }

    private void OnTabSelectionChanged(object? item)
    {
        if (item is TabItem tab && _bucketIdsByTab.TryGetValue(tab, out var bucketId))
        {
            _state.ActiveBucketId = bucketId;
            if (_bucketNodesById.TryGetValue(bucketId, out var node) && !ReferenceEquals(_bucketTree.SelectedItem, node))
            {
                _bucketTree.SelectedItem = node;
            }
            ApplyCurrentFilter();
            SaveWorkspace();
        }
    }

    private void ApplyCurrentFilter(string? bucketId = null)
    {
        bucketId ??= _state.ActiveBucketId;
        var bucket = FindBucket(bucketId);
        if (bucket is null || !_objectViews.TryGetValue(bucket.Id, out var view))
        {
            return;
        }

        var searchText = _searchTexts.GetValueOrDefault(bucket.Id, string.Empty);
        IReadOnlyList<ObjectEntry> filtered = string.IsNullOrWhiteSpace(searchText)
            ? bucket.Objects
            : bucket.Objects
                .Where(entry => entry.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        view.ItemsSource(filtered);
        SetStatus(bucket.Id, $"显示 {filtered.Count} / {bucket.Objects.Count} 个对象");
    }

    private async void OnWindowLoaded()
    {
        _hasLoaded = true;
        foreach (var bucketId in _state.OpenBucketIds.ToArray())
        {
            if (FindBucket(bucketId) is { } bucket)
            {
                await RefreshBucketAsync(bucket);
            }
        }
    }

    private async void RefreshActiveBucket()
    {
        if (FindBucket(_state.ActiveBucketId) is { } bucket)
        {
            await RefreshBucketAsync(bucket);
        }
    }

    private async Task RefreshBucketAsync(BucketProfile bucket)
    {
        bucket = FindBucket(bucket.Id) ?? bucket;
        if (!_objectViews.TryGetValue(bucket.Id, out var currentView))
        {
            return;
        }

        var tabState = GetTabState(bucket);
        var refreshVersion = ++tabState.RefreshVersion;
        tabState.LastObjectClick = null;
        var prefix = tabState.CurrentPrefix;
        tabState.SelectedEntry = null;
        ClearCheckedObjects(bucket.Id);
        ClearObjectHover(bucket.Id);
        ClearRowCellBindings(bucket.Id);
        currentView.ItemsSource(Array.Empty<ObjectEntry>());
        UpdateHeaderCheckBox(bucket.Id);
        SetStatus(bucket.Id, "正在从 OSS 加载对象列表…");
        try
        {
            var credential = LoadCredential(bucket.Id);
            if (credential is null)
            {
                SetStatus(bucket.Id, "未找到 AccessKey，请右键资源桶重新编辑凭据");
                return;
            }

            var result = await _ossObjectService.ListObjectsAsync(bucket, credential, prefix);
            if (tabState.RefreshVersion != refreshVersion ||
                !string.Equals(tabState.CurrentPrefix, prefix, StringComparison.Ordinal) ||
                !_objectViews.TryGetValue(bucket.Id, out var view) ||
                FindBucket(bucket.Id) is null)
            {
                return;
            }

            bucket.Objects.Clear();
            bucket.Objects.AddRange(result.Objects);
            if (result.EndpointCorrected)
            {
                var bucketIndex = _state.Buckets.FindIndex(item => item.Id == bucket.Id);
                if (bucketIndex >= 0)
                {
                    _state.Buckets[bucketIndex] = bucket with
                    {
                        Endpoint = result.Endpoint,
                        RegionCode = result.RegionCode,
                        RegionName = BucketEditorDialog.GetRegionDisplayName(result.RegionCode)
                    };
                    SaveWorkspace();
                }
            }
            if (!string.IsNullOrWhiteSpace(_searchTexts.GetValueOrDefault(bucket.Id)))
            {
                ApplyCurrentFilter(bucket.Id);
            }
            else
            {
                view.ItemsSource(bucket.Objects);
                var loadedMessage = result.Objects.Count == 0 ? "当前目录为空" : $"已加载 {result.Objects.Count} 个对象";
                SetStatus(bucket.Id, result.EndpointCorrected
                    ? $"{loadedMessage} · 已自动修正区域为 {result.RegionCode}"
                    : loadedMessage);
            }
        }
        catch (Exception exception) when (GetServiceException(exception) is { } serviceException)
        {
            if (tabState.RefreshVersion != refreshVersion)
            {
                return;
            }

            var message = serviceException.StatusCode switch
            {
                403 => "没有列举权限，请确认 RAM 用户拥有 oss:ListObjects 权限",
                404 => "Bucket 不存在，请检查名称、区域和 Endpoint",
                _ => $"OSS 返回错误：{serviceException.ErrorCode} {serviceException.ErrorMessage}".Trim()
            };
            SetStatus(bucket.Id, message);
            this.ShowToast(message);
        }
        catch (Exception exception)
        {
            if (tabState.RefreshVersion != refreshVersion)
            {
                return;
            }

            var message = $"加载失败：{exception.Message}";
            SetStatus(bucket.Id, message);
            this.ShowToast(message);
        }
    }

    private async Task NavigateToPrefixAsync(BucketProfile bucket, string prefix, bool recordHistory = true)
    {
        var tabState = GetTabState(bucket);
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : prefix.EndsWith('/') ? prefix : $"{prefix}/";
        if (recordHistory && !string.Equals(
                tabState.CurrentPrefix,
                normalizedPrefix,
                StringComparison.Ordinal))
        {
            var history = tabState.NavigationHistory;
            var currentIndex = tabState.NavigationHistoryIndex;
            if (currentIndex < history.Count - 1)
            {
                history.RemoveRange(currentIndex + 1, history.Count - currentIndex - 1);
            }

            history.Add(normalizedPrefix);
            tabState.NavigationHistoryIndex = history.Count - 1;
        }

        tabState.CurrentPrefix = normalizedPrefix;
        UpdatePathText(bucket);
        UpdateBookmarkButton(bucket);
        await RefreshBucketAsync(bucket);
    }

    private void NavigateHistory(BucketProfile bucket, int offset)
    {
        if (!_tabStates.TryGetValue(bucket.Id, out var tabState))
        {
            return;
        }

        var history = tabState.NavigationHistory;
        var currentIndex = tabState.NavigationHistoryIndex;
        var nextIndex = currentIndex + offset;
        if (nextIndex < 0 || nextIndex >= history.Count)
        {
            SetStatus(bucket.Id, offset < 0 ? "已经是最早的浏览记录" : "已经是最新的浏览记录");
            return;
        }

        tabState.NavigationHistoryIndex = nextIndex;
        _ = NavigateToPrefixAsync(bucket, history[nextIndex], recordHistory: false);
    }

    private void NavigateUp(BucketProfile bucket)
    {
        var rootPrefix = bucket.Prefix;
        var currentPrefix = GetTabState(bucket).CurrentPrefix;
        if (string.Equals(currentPrefix, rootPrefix, StringComparison.Ordinal))
        {
            SetStatus(bucket.Id, "已经位于资源桶预设目录");
            return;
        }

        var relative = currentPrefix.StartsWith(rootPrefix, StringComparison.Ordinal)
            ? currentPrefix[rootPrefix.Length..].TrimEnd('/')
            : string.Empty;
        var separatorIndex = relative.LastIndexOf('/');
        var parentPrefix = separatorIndex >= 0
            ? rootPrefix + relative[..(separatorIndex + 1)]
            : rootPrefix;
        _ = NavigateToPrefixAsync(bucket, parentPrefix);
    }

    private void UpdatePathText(BucketProfile bucket)
    {
        if (_pathTexts.TryGetValue(bucket.Id, out var pathText))
        {
            pathText.Value = GetDisplayPath(bucket);
        }
    }

    private string GetDisplayPath(BucketProfile bucket)
    {
        var currentPrefix = GetTabState(bucket).CurrentPrefix;
        return $"oss://{bucket.Name}/{currentPrefix}";
    }

    private static AlibabaCloud.OSS.V2.ServiceException? GetServiceException(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is AlibabaCloud.OSS.V2.ServiceException serviceException)
            {
                return serviceException;
            }
        }

        return null;
    }

    private async void AddBucket()
    {
        var dialog = new BucketEditorDialog(
            bucket: null,
            credential: null,
            nameExists: name => _state.Buckets.Any(bucket =>
                string.Equals(bucket.Name, name, StringComparison.OrdinalIgnoreCase)));
        await dialog.ShowAsync(this);

        if (!dialog.Accepted || dialog.Result is not { } bucket || dialog.Credential is not { } credential)
        {
            return;
        }

        if (!TrySaveCredential(bucket, credential))
        {
            return;
        }

        _state.Buckets.Add(bucket);
        _bucketTree.ItemsSource(CreateBucketNodes());
        _bucketCountText.Text = $"资源桶（{_state.Buckets.Count}）";
        OpenBucket(bucket);
        _bucketTree.SelectedNode = _bucketNodesById[bucket.Id];
        SetStatus(bucket.Id, $"已添加资源桶：{bucket.Name}");
        SaveWorkspace();
    }

    private async void EditSelectedBucket()
    {
        var bucket = GetSelectedBucket();
        if (bucket is null)
        {
            return;
        }

        var dialog = new BucketEditorDialog(
            bucket,
            LoadCredential(bucket.Id),
            name => _state.Buckets.Any(item =>
                item.Id != bucket.Id && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase)));
        await dialog.ShowAsync(this);

        if (!dialog.Accepted || dialog.Result is not { } result || dialog.Credential is not { } credential)
        {
            return;
        }

        if (!TrySaveCredential(result, credential))
        {
            return;
        }

        var index = _state.Buckets.FindIndex(item => item.Id == bucket.Id);
        if (index < 0)
        {
            return;
        }

        _state.Buckets[index] = result with { Objects = bucket.Objects };
        ReloadBucketViews();
        SetStatus(bucket.Id, $"已更新资源桶：{result.Name}");
        SaveWorkspace();
    }

    private async void DeleteSelectedBucket()
    {
        var bucket = GetSelectedBucket();
        if (bucket is null)
        {
            return;
        }

        var confirmation = new ConfirmOverlay(
            "删除资源桶",
            $"确定删除资源桶“{bucket.Name}”吗？",
            "这会删除本地配置和保存的 AccessKey，不会删除 OSS 上的真实 Bucket。");
        if (!await confirmation.ShowAsync(this))
        {
            return;
        }

        try
        {
            _credentialStore.Delete(bucket.Id);
            _sessionCredentials.Remove(bucket.Id);
        }
        catch (Exception exception)
        {
            MessageBox.Notify(exception.Message, PromptIconKind.Error, "删除凭据失败", this);
            return;
        }

        _state.Buckets.RemoveAll(item => item.Id == bucket.Id);
        _state.OpenBucketIds.Remove(bucket.Id);
        _state.Bookmarks.RemoveAll(bookmark => bookmark.BucketId == bucket.Id);
        _searchTexts.Remove(bucket.Id);
        if (_state.ActiveBucketId == bucket.Id)
        {
            _state.ActiveBucketId = _state.OpenBucketIds.FirstOrDefault() ?? string.Empty;
        }

        ReloadBucketViews();
        RefreshBookmarkTree();
        SaveWorkspace();
    }

    private void ReloadBucketViews()
    {
        var openBucketIds = _state.OpenBucketIds
            .Where(id => FindBucket(id) is not null)
            .Distinct()
            .ToArray();
        var activeBucketId = FindBucket(_state.ActiveBucketId)?.Id ?? openBucketIds.FirstOrDefault() ?? string.Empty;

        while (_tabs.Tabs.Count > 0)
        {
            _tabs.RemoveTabAt(0);
        }

        _bucketIdsByTab.Clear();
        _objectViews.Clear();
        _statusTexts.Clear();
        _pathTexts.Clear();
        _bookmarkButtons.Clear();
        foreach (var tabState in _tabStates.Values)
        {
            tabState.TransferCancellation?.Cancel();
        }
        _tabStates.Clear();
        _headerCheckBoxes.Clear();
        _updatingHeaderCheckBoxes.Clear();
        _rowCellAssignments.Clear();
        _rowCellsByObject.Clear();
        _state.OpenBucketIds.Clear();
        _bucketTree.ItemsSource(CreateBucketNodes());
        _bucketCountText.Text = $"资源桶（{_state.Buckets.Count}）";
        RefreshBookmarkTree();

        foreach (var bucketId in openBucketIds)
        {
            if (FindBucket(bucketId) is { } bucket)
            {
                AddBucketTab(bucket, select: false);
            }
        }

        if (_tabs.Tabs.Count == 0 && _state.Buckets.FirstOrDefault() is { } first)
        {
            AddBucketTab(first, select: true);
            activeBucketId = first.Id;
        }

        var activeTab = _bucketIdsByTab.FirstOrDefault(pair => pair.Value == activeBucketId).Key;
        if (activeTab is not null)
        {
            _tabs.SelectedIndex = FindTabIndex(activeTab);
        }
        else if (_tabs.Tabs.Count > 0)
        {
            _tabs.SelectedIndex = 0;
        }
    }

    private BucketCredential? LoadCredential(string bucketId)
        => _sessionCredentials.TryGetValue(bucketId, out var credential)
            ? credential
            : _credentialStore.Load(bucketId);

    private bool TrySaveCredential(BucketProfile bucket, BucketCredential credential)
    {
        try
        {
            if (bucket.KeepLogin && bucket.RememberSecret)
            {
                _credentialStore.Save(bucket.Id, credential);
                _sessionCredentials.Remove(bucket.Id);
            }
            else
            {
                _credentialStore.Delete(bucket.Id);
                _sessionCredentials[bucket.Id] = credential;
            }

            return true;
        }
        catch (Exception exception)
        {
            MessageBox.Notify(exception.Message, PromptIconKind.Error, "保存凭据失败", this);
            return false;
        }
    }

    private BucketProfile? GetSelectedBucket()
        => (_bucketTree.SelectedNode?.Tag as BucketProfile) is { } selected
            ? FindBucket(selected.Id)
            : null;

    private IReadOnlyList<TreeViewNode> CreateBucketNodes()
    {
        _bucketNodesById.Clear();
        return _state.Buckets.Select(bucket =>
        {
            var node = new TreeViewNode($"▰  {bucket.Name}", tag: bucket);
            _bucketNodesById[bucket.Id] = node;
            return node;
        }).ToArray();
    }

    private static string GetBookmarkDisplayText(DirectoryBookmark bookmark, BucketProfile bucket)
    {
        var directoryName = bookmark.Prefix.Length == 0
            ? "根目录"
            : bookmark.Prefix.TrimEnd('/').Split('/').Last();
        var displayName = string.IsNullOrWhiteSpace(bookmark.DisplayName)
            ? directoryName
            : bookmark.DisplayName.Trim();
        return $"{displayName} · {bucket.Name}";
    }

    private Button FlatButton(string text, Action action)
    {
        return new Button()
            .Content(text, accessKey: false)
            .StyleName(BuiltInStyles.FlatButton)
            .Padding(9, 5)
            .OnClick(action)
            .WithFeedback(text.TrimStart('＋', '↻', ' '), Color.White.WithAlpha(0), HeaderSurface, BorderColor);
    }

    private Button BrowserToolbarButton(string text, string toolTip, Action action)
    {
        var normal = HeaderSurface;
        var hover = ObjectRowHover;
        var pressed = ObjectRowSelected;
        return new Button()
            .Content(text, accessKey: false)
            .Margin(0, 0, 4, 0)
            .Padding(10, 5)
            .CornerRadius(3)
            .Background(normal)
            .BorderBrush(BorderColor)
            .Foreground(PrimaryText)
            .OnClick(action)
            .WithFeedback(toolTip, normal, hover, pressed);
    }

    private static Button AccentButton(string text, Color color, Action action)
    {
        return new Button()
            .Content(text, accessKey: false)
            .Padding(12, 6)
            .CornerRadius(6)
            .Background(color.WithAlpha(38))
            .BorderBrush(color.WithAlpha(110))
            .Foreground(color)
            .OnClick(action)
            .WithFeedback(text, color.WithAlpha(38), color.WithAlpha(58), color.WithAlpha(88));
    }

    private ObjectActionCell CreateObjectActionCell(string bucketId)
    {
        return new ObjectActionCell(
            download: entry => _ = DownloadEntryAsync(bucketId, entry),
            delete: entry => _ = DeleteEntryAsync(bucketId, entry),
            downloadColor: Blue,
            deleteColor: Coral);
    }

    private void DownloadSelected(string bucketId)
    {
        var entries = GetActionEntries(bucketId);
        if (entries.Count > 1)
        {
            _ = DownloadEntriesAsync(bucketId, entries);
        }
        else if (entries.FirstOrDefault() is { } entry)
        {
            _ = DownloadEntryAsync(bucketId, entry);
        }
        else
        {
            SetStatus(bucketId, "请先选择要下载的文件或文件夹");
        }
    }

    private void DeleteSelected(string bucketId)
    {
        var entries = GetActionEntries(bucketId);
        if (entries.Count > 1)
        {
            _ = DeleteEntriesAsync(bucketId, entries);
        }
        else if (entries.FirstOrDefault() is { } entry)
        {
            _ = DeleteEntryAsync(bucketId, entry);
        }
        else
        {
            SetStatus(bucketId, "请先选择要删除的文件或文件夹");
        }
    }

    private async Task DownloadEntryAsync(string bucketId, ObjectEntry entry, string? retryDestination = null)
    {
        var bucket = FindBucket(bucketId);
        var credential = bucket is null ? null : LoadCredential(bucket.Id);
        if (bucket is null || credential is null)
        {
            SetStatus(bucketId, "未找到资源桶或 AccessKey 配置");
            return;
        }

        CancellationTokenSource? cancellation = null;
        TransferTaskInfo? transferTask = null;
        try
        {
            if (entry.IsFolder)
            {
                var destination = retryDestination;
                if (string.IsNullOrWhiteSpace(destination))
                {
                    destination = await FileDialog.SelectFolderAsync(new FolderDialogOptions
                    {
                        Owner = this,
                        Title = $"选择“{entry.Name.TrimEnd('/')}”的下载位置"
                    });
                }
                if (string.IsNullOrWhiteSpace(destination))
                {
                    return;
                }

                transferTask = TryBeginFileTransfer(bucket, "下载", entry.Name.TrimEnd('/'));
                if (transferTask is null)
                {
                    return;
                }
                transferTask.RetryAsync = () => DownloadEntryAsync(bucketId, entry, destination);
                cancellation = transferTask.Cancellation;
                var progress = new Progress<(int Current, int Total, string Name)>(value =>
                {
                    if (!transferTask.IsRunning)
                    {
                        return;
                    }

                    SetStatus(bucketId, $"正在下载 {value.Current}/{value.Total}：{value.Name}");
                    UpdateTransferTask(transferTask, value.Current, value.Total, value.Name);
                });
                var count = await _ossObjectService.DownloadFolderAsync(
                    bucket,
                    credential,
                    entry.Key,
                    destination,
                    progress,
                    cancellation.Token);
                var message = $"文件夹下载完成，共 {count} 个文件";
                FinishTransferTask(transferTask, "已完成", message, count, count);
                SetStatus(bucketId, message);
                this.ShowToast(message);
            }
            else
            {
                var destination = retryDestination;
                if (string.IsNullOrWhiteSpace(destination))
                {
                    destination = await FileDialog.SaveFileAsync(new SaveFileDialogOptions
                    {
                        Owner = this,
                        Title = $"下载 {entry.Name}",
                        FileName = Path.GetFileName(entry.Name),
                        OverwritePrompt = true
                    });
                }
                if (string.IsNullOrWhiteSpace(destination))
                {
                    return;
                }

                transferTask = TryBeginFileTransfer(bucket, "下载", entry.Name);
                if (transferTask is null)
                {
                    return;
                }
                transferTask.RetryAsync = () => DownloadEntryAsync(bucketId, entry, destination);
                cancellation = transferTask.Cancellation;
                SetStatus(bucketId, $"正在下载：{entry.Name}");
                UpdateTransferTask(transferTask, 0, 1, entry.Name);
                await _ossObjectService.DownloadObjectAsync(bucket, credential, entry.Key, destination, cancellation.Token);
                var message = $"下载完成：{entry.Name}";
                FinishTransferTask(transferTask, "已完成", message, 1, 1);
                SetStatus(bucketId, message);
                this.ShowToast(message);
            }
        }
        catch (OperationCanceledException)
        {
            if (transferTask is not null)
            {
                FinishTransferTask(transferTask, "已取消", "下载已取消");
            }
            SetStatus(bucketId, "下载已取消");
        }
        catch (Exception exception)
        {
            if (transferTask is not null)
            {
                FinishTransferTask(transferTask, "失败", exception.Message);
            }
            ShowOperationError(bucketId, "下载失败", exception);
        }
        finally
        {
            if (cancellation is not null)
            {
                EndTransfer(bucketId, cancellation);
            }
        }
    }

    private async Task DeleteEntryAsync(string bucketId, ObjectEntry entry)
    {
        var bucket = FindBucket(bucketId);
        var credential = bucket is null ? null : LoadCredential(bucket.Id);
        if (bucket is null || credential is null)
        {
            SetStatus(bucketId, "未找到资源桶或 AccessKey 配置");
            return;
        }

        var itemType = entry.IsFolder ? "文件夹" : "文件";
        var detail = entry.IsFolder
            ? "该文件夹前缀下的所有 OSS 对象都会被递归删除，此操作不可撤销。"
            : "该 OSS 对象将被删除，此操作不可撤销。";
        var confirmation = new ConfirmOverlay(
            $"删除{itemType}",
            $"确定删除{itemType}“{entry.Name}”吗？",
            detail);
        if (!await confirmation.ShowAsync(this))
        {
            return;
        }

        var cancellation = TryBeginTransfer(bucketId, "删除");
        if (cancellation is null)
        {
            return;
        }

        try
        {
            SetStatus(bucketId, $"正在删除：{entry.Name}");
            var deletedCount = entry.IsFolder
                ? await _ossObjectService.DeleteFolderAsync(bucket, credential, entry.Key, cancellation.Token)
                : await DeleteSingleObjectAsync(bucket, credential, entry.Key, cancellation.Token);
            if (_tabStates.TryGetValue(bucketId, out var tabState))
            {
                tabState.SelectedEntry = null;
                tabState.CheckedObjectKeys.Clear();
            }
            if (entry.IsFolder)
            {
                RemoveBookmarksUnderPrefix(bucketId, entry.Key);
            }
            var message = entry.IsFolder
                ? $"已删除文件夹中的 {deletedCount} 个对象"
                : $"已删除：{entry.Name}";
            SetStatus(bucketId, message);
            this.ShowToast(message);
            await RefreshBucketAsync(bucket);
        }
        catch (OperationCanceledException)
        {
            SetStatus(bucketId, "删除已取消");
        }
        catch (Exception exception)
        {
            ShowOperationError(bucketId, "删除失败", exception);
        }
        finally
        {
            EndTransfer(bucketId, cancellation);
        }
    }

    private async Task<int> DeleteSingleObjectAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string key,
        CancellationToken cancellationToken = default)
    {
        await _ossObjectService.DeleteObjectAsync(bucket, credential, key, cancellationToken);
        return 1;
    }

    private IReadOnlyList<ObjectEntry> GetActionEntries(string bucketId)
    {
        var checkedEntries = GetCheckedEntries(bucketId);
        if (checkedEntries.Count > 0)
        {
            return checkedEntries;
        }

        return _tabStates.GetValueOrDefault(bucketId)?.SelectedEntry is { } entry ? [entry] : [];
    }

    private async Task DownloadEntriesAsync(
        string bucketId,
        IReadOnlyList<ObjectEntry> entries,
        string? retryDestination = null)
    {
        var bucket = FindBucket(bucketId);
        var credential = bucket is null ? null : LoadCredential(bucket.Id);
        if (bucket is null || credential is null)
        {
            SetStatus(bucketId, "未找到资源桶或 AccessKey 配置");
            return;
        }

        var destination = retryDestination;
        if (string.IsNullOrWhiteSpace(destination))
        {
            destination = await FileDialog.SelectFolderAsync(new FolderDialogOptions
            {
                Owner = this,
                Title = $"选择 {entries.Count} 个对象的下载位置"
            });
        }
        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        var transferTask = TryBeginFileTransfer(bucket, "下载", $"{entries.Count} 个对象");
        if (transferTask is null)
        {
            return;
        }
        transferTask.RetryAsync = () => DownloadEntriesAsync(bucketId, entries, destination);
        var cancellation = transferTask.Cancellation;

        try
        {
            var progress = new Progress<(int Current, int Total, string Name)>(value =>
            {
                if (!transferTask.IsRunning)
                {
                    return;
                }

                SetStatus(bucketId, $"正在下载 {value.Current}/{value.Total}：{value.Name}");
                UpdateTransferTask(transferTask, value.Current, value.Total, value.Name);
            });
            var count = await _ossObjectService.DownloadEntriesAsync(
                bucket, credential, entries, destination, progress, cancellation.Token);
            var message = $"批量下载完成，共处理 {count} 个对象";
            FinishTransferTask(transferTask, "已完成", message, count, count);
            SetStatus(bucketId, message);
            this.ShowToast(message);
        }
        catch (OperationCanceledException)
        {
            FinishTransferTask(transferTask, "已取消", "批量下载已取消");
            SetStatus(bucketId, "批量下载已取消");
        }
        catch (Exception exception)
        {
            FinishTransferTask(transferTask, "失败", exception.Message);
            ShowOperationError(bucketId, "批量下载失败", exception);
        }
        finally
        {
            EndTransfer(bucketId, cancellation);
        }
    }

    private async Task DeleteEntriesAsync(string bucketId, IReadOnlyList<ObjectEntry> entries)
    {
        var bucket = FindBucket(bucketId);
        var credential = bucket is null ? null : LoadCredential(bucket.Id);
        if (bucket is null || credential is null)
        {
            SetStatus(bucketId, "未找到资源桶或 AccessKey 配置");
            return;
        }

        var confirmation = new ConfirmOverlay(
            "批量删除对象",
            $"确定删除选中的 {entries.Count} 个文件或目录吗？",
            "目录会按对象前缀递归删除，全部操作均不可撤销。");
        if (!await confirmation.ShowAsync(this))
        {
            return;
        }

        var cancellation = TryBeginTransfer(bucketId, "批量删除");
        if (cancellation is null)
        {
            return;
        }

        try
        {
            var progress = new Progress<(int Current, int Total, string Name)>(value =>
                SetStatus(bucketId, $"正在删除：{value.Name}"));
            var count = await _ossObjectService.DeleteEntriesAsync(
                bucket, credential, entries, progress, cancellation.Token);
            GetTabState(bucket).ClearInteractionState();
            foreach (var folder in entries.Where(item => item.IsFolder))
            {
                RemoveBookmarksUnderPrefix(bucketId, folder.Key, save: false);
            }
            RefreshBookmarkTree();
            SaveWorkspace();
            var message = $"批量删除完成，共删除 {count} 个 OSS 对象";
            SetStatus(bucketId, message);
            this.ShowToast(message);
            await RefreshBucketAsync(bucket);
        }
        catch (OperationCanceledException)
        {
            SetStatus(bucketId, "批量删除已取消，已完成的删除无法恢复");
        }
        catch (Exception exception)
        {
            ShowOperationError(bucketId, "批量删除失败", exception);
        }
        finally
        {
            EndTransfer(bucketId, cancellation);
        }
    }

    private async Task ChooseFilesToUploadAsync(BucketProfile bucket)
    {
        var paths = await FileDialog.OpenFilesAsync(new OpenFileDialogOptions
        {
            Owner = this,
            Title = "选择要上传的文件",
            Multiselect = true
        });
        if (paths is { Length: > 0 })
        {
            await UploadDroppedPathsAsync(bucket, paths);
        }
    }

    private async Task ChooseFolderToUploadAsync(BucketProfile bucket)
    {
        var path = await FileDialog.SelectFolderAsync(new FolderDialogOptions
        {
            Owner = this,
            Title = "选择要上传的目录"
        });
        if (!string.IsNullOrWhiteSpace(path))
        {
            await UploadDroppedPathsAsync(bucket, [path]);
        }
    }

    private async Task CreateFolderAsync(BucketProfile bucket)
    {
        bucket = FindBucket(bucket.Id) ?? bucket;
        var prompt = new TextPromptOverlay(
            "创建 OSS 目录",
            $"将在 {GetDisplayPath(bucket)} 下创建目录。",
            "请输入目录名称",
            validate: value => string.IsNullOrWhiteSpace(value)
                ? "目录名称不能为空"
                : value.IndexOfAny(['/', '\\']) >= 0
                    ? "目录名称不能包含斜杠"
                    : null);
        var name = await prompt.ShowAsync(this);
        if (name is null)
        {
            return;
        }

        var credential = LoadCredential(bucket.Id);
        if (credential is null)
        {
            SetStatus(bucket.Id, "未找到 AccessKey 配置");
            return;
        }

        var cancellation = TryBeginTransfer(bucket.Id, "创建目录");
        if (cancellation is null)
        {
            return;
        }

        try
        {
            var key = GetTabState(bucket).CurrentPrefix + name.Trim() + "/";
            await _ossObjectService.CreateFolderAsync(bucket, credential, key, cancellation.Token);
            SetStatus(bucket.Id, $"已创建目录：{name}");
            await RefreshBucketAsync(bucket);
        }
        catch (OperationCanceledException)
        {
            SetStatus(bucket.Id, "创建目录已取消");
        }
        catch (Exception exception)
        {
            ShowOperationError(bucket.Id, "创建目录失败", exception);
        }
        finally
        {
            EndTransfer(bucket.Id, cancellation);
        }
    }

    private async Task CopySelectedAsync(string bucketId, IReadOnlyList<ObjectEntry>? selectedEntries = null)
    {
        var entries = selectedEntries ?? GetActionEntries(bucketId);
        var bucket = FindBucket(bucketId);
        if (entries.Count == 0 || bucket is null)
        {
            SetStatus(bucketId, "请先选择要复制的对象");
            return;
        }

        var singleEntry = entries.Count == 1 ? entries[0] : null;
        var prompt = singleEntry is not null
            ? new TextPromptOverlay(
                "复制并重命名 OSS 对象",
                "输入同一 Bucket 内的完整目标路径。修改最后一段即可重命名对象。",
                "目标 OSS 路径",
                GetTabState(bucket).CurrentPrefix + singleEntry.Name.TrimEnd('/'),
                value => TryNormalizeTargetKey(value, bucket, singleEntry.IsFolder, out _, out var error) ? null : error)
            : new TextPromptOverlay(
                "复制 OSS 对象",
                "输入同一 Bucket 内的目标目录，例如 archive/2026/。批量复制时保留原对象名称。",
                "目标 OSS 目录",
                GetTabState(bucket).CurrentPrefix,
                value => TryNormalizeTargetPrefix(value, bucket, out _, out var error) ? null : error);
        var destination = await prompt.ShowAsync(this);
        if (destination is null)
        {
            return;
        }

        string targetPath;
        if (singleEntry is not null)
        {
            if (!TryNormalizeTargetKey(destination, bucket, singleEntry.IsFolder, out targetPath, out _))
            {
                return;
            }
        }
        else if (!TryNormalizeTargetPrefix(destination, bucket, out targetPath, out _))
        {
            return;
        }

        var credential = LoadCredential(bucket.Id);
        if (credential is null)
        {
            SetStatus(bucketId, "未找到 AccessKey 配置");
            return;
        }

        var cancellation = TryBeginTransfer(bucketId, "复制");
        if (cancellation is null)
        {
            return;
        }

        try
        {
            var progress = new Progress<(int Current, int Total, string Name)>(value =>
                SetStatus(bucketId, $"正在复制 {value.Current}/{value.Total}：{value.Name}"));
            var count = singleEntry is not null
                ? await _ossObjectService.CopyEntryAsync(
                    bucket, credential, singleEntry, targetPath, progress, cancellation.Token)
                : await _ossObjectService.CopyEntriesAsync(
                    bucket, credential, entries, targetPath, progress, cancellation.Token);
            var message = singleEntry is not null
                ? $"复制完成：{targetPath}"
                : $"复制完成，共复制 {count} 个 OSS 对象";
            SetStatus(bucketId, message);
            this.ShowToast(message);
            await RefreshBucketAsync(bucket);
        }
        catch (OperationCanceledException)
        {
            SetStatus(bucketId, "复制已取消，已完成的对象仍会保留");
        }
        catch (Exception exception)
        {
            ShowOperationError(bucketId, "复制失败", exception);
        }
        finally
        {
            EndTransfer(bucketId, cancellation);
        }
    }

    private async Task MoveEntryAsync(string bucketId, ObjectEntry entry)
    {
        var bucket = FindBucket(bucketId);
        if (bucket is null)
        {
            return;
        }

        var prompt = new TextPromptOverlay(
            "移动 OSS 对象",
            "输入同一 Bucket 内的完整目标路径，最后一段为移动后的对象名称。",
            "目标 OSS 路径",
            entry.Key.TrimEnd('/'),
            value => TryNormalizeMoveTarget(value, bucket, entry, out _, out var error) ? null : error);
        var destination = await prompt.ShowAsync(this);
        if (destination is null ||
            !TryNormalizeMoveTarget(destination, bucket, entry, out var targetKey, out _))
        {
            return;
        }

        await MoveEntryToAsync(bucket, entry, targetKey, "移动");
    }

    private async Task RenameEntryAsync(string bucketId, ObjectEntry entry)
    {
        var bucket = FindBucket(bucketId);
        if (bucket is null)
        {
            return;
        }

        var prompt = new TextPromptOverlay(
            "重命名 OSS 对象",
            $"输入“{entry.Name.TrimEnd('/')}”的新名称。",
            "新对象名称",
            entry.Name.TrimEnd('/'),
            value => string.IsNullOrWhiteSpace(value)
                ? "对象名称不能为空"
                : value.IndexOfAny(['/', '\\']) >= 0
                    ? "对象名称不能包含斜杠"
                    : value.Trim() is "." or ".."
                        ? "对象名称不能是 . 或 .."
                    : null);
        var name = await prompt.ShowAsync(this);
        if (name is null)
        {
            return;
        }

        var sourceKey = entry.Key.TrimEnd('/');
        var separator = sourceKey.LastIndexOf('/');
        var parentPrefix = separator >= 0 ? sourceKey[..(separator + 1)] : string.Empty;
        var targetKey = parentPrefix + name.Trim() + (entry.IsFolder ? "/" : string.Empty);
        if (string.Equals(entry.Key, targetKey, StringComparison.Ordinal))
        {
            SetStatus(bucketId, "新名称与原名称相同");
            return;
        }

        await MoveEntryToAsync(bucket, entry, targetKey, "重命名");
    }

    private async Task MoveEntryToAsync(
        BucketProfile bucket,
        ObjectEntry entry,
        string targetKey,
        string operation)
    {
        var itemType = entry.IsFolder ? "目录" : "文件";
        var confirmation = new ConfirmOverlay(
            $"确认{operation}{itemType}",
            $"确定将“{entry.Key}”{operation}为“{targetKey}”吗？",
            "OSS 没有原生移动操作；应用会先完整复制对象，复制成功后再删除源对象。",
            operation);
        if (!await confirmation.ShowAsync(this))
        {
            return;
        }

        var credential = LoadCredential(bucket.Id);
        if (credential is null)
        {
            SetStatus(bucket.Id, "未找到 AccessKey 配置");
            return;
        }

        var cancellation = TryBeginTransfer(bucket.Id, operation);
        if (cancellation is null)
        {
            return;
        }

        try
        {
            var progress = new Progress<(int Current, int Total, string Name)>(value =>
                SetStatus(bucket.Id, $"正在{operation} {value.Current}/{value.Total}：{value.Name}"));
            await _ossObjectService.CopyEntryAsync(
                bucket, credential, entry, targetKey, progress, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (entry.IsFolder)
            {
                await _ossObjectService.DeleteFolderAsync(bucket, credential, entry.Key, cancellation.Token);
            }
            else
            {
                await _ossObjectService.DeleteObjectAsync(bucket, credential, entry.Key, cancellation.Token);
            }

            if (entry.IsFolder)
            {
                RemapBookmarks(bucket.Id, entry.Key, targetKey);
            }

            var message = $"{operation}完成：{targetKey}";
            SetStatus(bucket.Id, message);
            this.ShowToast(message);
            await RefreshBucketAsync(bucket);
        }
        catch (OperationCanceledException)
        {
            SetStatus(bucket.Id, $"{operation}已取消；已复制的目标对象可能仍会保留");
        }
        catch (Exception exception)
        {
            ShowOperationError(bucket.Id, $"{operation}失败", exception);
        }
        finally
        {
            EndTransfer(bucket.Id, cancellation);
        }
    }

    private void CopyObjectAddress(string bucketId, ObjectEntry entry)
    {
        var bucket = FindBucket(bucketId);
        var credential = bucket is null ? null : LoadCredential(bucket.Id);
        if (bucket is null || credential is null || entry.IsFolder)
        {
            SetStatus(bucketId, "未找到资源桶、文件或 AccessKey 配置");
            return;
        }

        try
        {
            var address = _ossObjectService.GetObjectAddress(
                bucket,
                credential,
                entry.Key,
                DateTime.UtcNow.AddHours(1));
            if (!WindowsClipboard.TrySetText(address))
            {
                throw new InvalidOperationException("无法写入 Windows 剪贴板，请稍后重试");
            }

            var message = "已复制对象地址，链接有效期为 1 小时";
            SetStatus(bucketId, message);
            this.ShowToast(message);
        }
        catch (Exception exception)
        {
            ShowOperationError(bucketId, "获取地址失败", exception);
        }
    }

    private void RemoveBookmarksUnderPrefix(string bucketId, string prefix, bool save = true)
    {
        var removed = _state.Bookmarks.RemoveAll(bookmark =>
            bookmark.BucketId == bucketId &&
            bookmark.Prefix.StartsWith(prefix, StringComparison.Ordinal));
        if (removed == 0)
        {
            return;
        }

        RefreshBookmarkTree();
        if (save)
        {
            SaveWorkspace();
        }
    }

    private void RemapBookmarks(string bucketId, string sourcePrefix, string targetPrefix)
    {
        targetPrefix = targetPrefix.TrimEnd('/') + "/";
        var changed = false;
        for (var index = 0; index < _state.Bookmarks.Count; index++)
        {
            var bookmark = _state.Bookmarks[index];
            if (bookmark.BucketId != bucketId ||
                !bookmark.Prefix.StartsWith(sourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            _state.Bookmarks[index] = bookmark with
            {
                Prefix = targetPrefix + bookmark.Prefix[sourcePrefix.Length..]
            };
            changed = true;
        }

        if (changed)
        {
            RefreshBookmarkTree();
            SaveWorkspace();
        }
    }

    private CancellationTokenSource? TryBeginTransfer(string bucketId, string operation)
    {
        if (!_tabStates.TryGetValue(bucketId, out var tabState))
        {
            return null;
        }

        if (tabState.IsTransferRunning)
        {
            SetStatus(bucketId, "当前资源桶已有任务正在执行，可在“传输任务”中取消");
            return null;
        }

        var cancellation = new CancellationTokenSource();
        tabState.TransferCancellation = cancellation;
        SetStatus(bucketId, $"正在准备{operation}…");
        return cancellation;
    }

    private TransferTaskInfo? TryBeginFileTransfer(BucketProfile bucket, string operation, string title)
    {
        var cancellation = TryBeginTransfer(bucket.Id, operation);
        if (cancellation is null)
        {
            return null;
        }

        var task = new TransferTaskInfo(bucket.Id, bucket.Name, operation, title, cancellation);
        _transferTasks.Insert(0, task);
        while (_transferTasks.Count > 30 && _transferTasks.LastOrDefault(item => !item.IsRunning) is { } oldest)
        {
            _transferTasks.Remove(oldest);
        }
        if (_transferTasksOverlay is null)
        {
            ShowTransferTasks();
        }
        else
        {
            RefreshTransferTasksOverlay();
        }
        return task;
    }

    private void UpdateTransferTask(TransferTaskInfo task, int current, int total, string detail)
    {
        if (!task.IsRunning)
        {
            return;
        }

        task.Current = current;
        task.Total = total;
        task.Detail = detail;
        RefreshTransferTasksOverlay();
    }

    private void FinishTransferTask(
        TransferTaskInfo task,
        string state,
        string detail,
        int? current = null,
        int? total = null)
    {
        task.State = state;
        task.Detail = detail;
        task.IsRunning = false;
        if (current.HasValue)
        {
            task.Current = current.Value;
        }
        if (total.HasValue)
        {
            task.Total = total.Value;
        }
        RefreshTransferTasksOverlay();
    }

    private void EndTransfer(string bucketId, CancellationTokenSource cancellation)
    {
        if (_tabStates.TryGetValue(bucketId, out var tabState) &&
            ReferenceEquals(tabState.TransferCancellation, cancellation))
        {
            tabState.TransferCancellation = null;
        }

        cancellation.Dispose();
    }

    private void CancelTransferTask(Guid taskId)
    {
        var task = _transferTasks.FirstOrDefault(item => item.Id == taskId);
        if (task is null || !task.IsRunning || task.Cancellation.IsCancellationRequested)
        {
            return;
        }

        task.Cancellation.Cancel();
        task.State = "取消中";
        task.Detail = "正在取消任务…";
        SetStatus(task.BucketId, $"正在取消{task.Operation}任务…");
        RefreshTransferTasksOverlay();
    }

    private void RetryTransferTask(Guid taskId)
    {
        var task = _transferTasks.FirstOrDefault(item => item.Id == taskId);
        if (task?.RetryAsync is null || !task.CanRetry)
        {
            return;
        }

        if (!_tabStates.TryGetValue(task.BucketId, out var tabState))
        {
            task.Detail = "请先打开对应的资源桶标签页，再重试此任务";
            RefreshTransferTasksOverlay();
            return;
        }

        if (tabState.IsTransferRunning)
        {
            SetStatus(task.BucketId, "当前资源桶已有任务正在执行，请稍后重试");
            return;
        }

        if (FindBucket(task.BucketId) is null || LoadCredential(task.BucketId) is null)
        {
            task.Detail = "资源桶或 AccessKey 配置不可用，暂时无法重试";
            RefreshTransferTasksOverlay();
            return;
        }

        task.CanRetry = false;
        task.State = "已重试";
        task.Detail = "已创建新的重试任务";
        RefreshTransferTasksOverlay();
        _ = task.RetryAsync();
    }

    private void ShowTransferTasks()
    {
        if (_transferTasksOverlay is not null)
        {
            return;
        }

        _transferTasksOverlay = new TransferTasksOverlay(
            CancelTransferTask,
            RetryTransferTask,
            ClearFinishedTransferTasks,
            () => _transferTasksOverlay = null);
        _transferTasksOverlay.Show(this, _transferTasks);
    }

    private async void ShowSettings()
    {
        var overlay = new SettingsOverlay(
            _state.UploadConcurrency,
            _state.DownloadConcurrency,
            _state.ThemeMode);
        var result = await overlay.ShowAsync(this);
        if (result is null)
        {
            return;
        }

        var themeChanged = !string.Equals(_state.ThemeMode, result.ThemeMode, StringComparison.Ordinal);
        _state.UploadConcurrency = Math.Clamp(result.UploadConcurrency, 1, 8);
        _state.DownloadConcurrency = Math.Clamp(result.DownloadConcurrency, 1, 8);
        _state.ThemeMode = result.ThemeMode;
        _ossObjectService.UploadConcurrency = _state.UploadConcurrency;
        _ossObjectService.DownloadConcurrency = _state.DownloadConcurrency;
        SaveWorkspace();

        var message = $"设置已保存：上传并发 {_state.UploadConcurrency}，下载并发 {_state.DownloadConcurrency}";
        if (themeChanged)
        {
            var runningTransferDetail = _transferTasks.Any(task => task.IsRunning)
                ? "当前仍有传输任务，立即重启会中断这些任务；选择“稍后”可继续使用当前窗口。"
                : "选择“稍后”可继续使用当前窗口，新的主题会在下次启动时生效。";
            var restartOverlay = new ConfirmOverlay(
                "重启应用",
                "主题设置已保存，是否立即重启以应用新主题？",
                runningTransferDetail,
                confirmText: "立即重启",
                cancelText: "稍后");
            if (await restartOverlay.ShowAsync(this))
            {
                RestartRequested = true;
                Close();
                return;
            }

            message += "；主题将在下次启动时生效";
        }
        SetStatus(message);
        this.ShowToast(message);
    }

    private void ClearFinishedTransferTasks()
    {
        _transferTasks.RemoveAll(task => !task.IsRunning);
        RefreshTransferTasksOverlay();
    }

    private void RefreshTransferTasksOverlay()
    {
        _transferTasksOverlay?.Refresh(_transferTasks);
    }

    private static bool TryNormalizeTargetPrefix(
        string value,
        BucketProfile bucket,
        out string prefix,
        out string error)
    {
        prefix = value.Trim();
        error = string.Empty;
        if (prefix.StartsWith("oss://", StringComparison.OrdinalIgnoreCase))
        {
            prefix = prefix[6..];
            var separator = prefix.IndexOf('/');
            var bucketName = separator >= 0 ? prefix[..separator] : prefix;
            if (!string.Equals(bucketName, bucket.Name, StringComparison.OrdinalIgnoreCase))
            {
                error = "目前仅支持复制到同一 Bucket";
                return false;
            }

            prefix = separator >= 0 ? prefix[(separator + 1)..] : string.Empty;
        }

        prefix = prefix.Trim().TrimStart('/');
        if (prefix.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            error = "目标目录不能包含 . 或 .. 路径段";
            return false;
        }

        if (prefix.Length > 0 && !prefix.EndsWith('/'))
        {
            prefix += "/";
        }

        return true;
    }

    private static bool TryNormalizeTargetKey(
        string value,
        BucketProfile bucket,
        bool isFolder,
        out string key,
        out string error)
    {
        key = value.Trim();
        error = string.Empty;
        if (key.StartsWith("oss://", StringComparison.OrdinalIgnoreCase))
        {
            key = key[6..];
            var separator = key.IndexOf('/');
            var bucketName = separator >= 0 ? key[..separator] : key;
            if (!string.Equals(bucketName, bucket.Name, StringComparison.OrdinalIgnoreCase))
            {
                error = "目前仅支持复制到同一 Bucket";
                return false;
            }

            key = separator >= 0 ? key[(separator + 1)..] : string.Empty;
        }

        key = key.Trim().TrimStart('/');
        if (key.Length == 0)
        {
            error = "目标 OSS 路径不能为空";
            return false;
        }
        if (key.Contains('\\'))
        {
            error = "目标 OSS 路径请使用 / 分隔目录";
            return false;
        }
        if (key.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(segment => segment is "." or ".."))
        {
            error = "目标路径不能包含 . 或 .. 路径段";
            return false;
        }

        key = isFolder ? key.TrimEnd('/') + "/" : key.TrimEnd('/');
        if (key.Length == 0)
        {
            error = "对象名称不能为空";
            return false;
        }
        return true;
    }

    private static bool TryNormalizeMoveTarget(
        string value,
        BucketProfile bucket,
        ObjectEntry entry,
        out string targetKey,
        out string error)
    {
        if (!TryNormalizeTargetKey(value, bucket, entry.IsFolder, out targetKey, out error))
        {
            return false;
        }
        if (string.Equals(entry.Key, targetKey, StringComparison.Ordinal))
        {
            error = "目标路径与源对象相同";
            return false;
        }
        if (entry.IsFolder && targetKey.StartsWith(entry.Key, StringComparison.Ordinal))
        {
            error = "不能将目录移动到自身内部";
            return false;
        }
        return true;
    }

    private void ShowOperationError(string bucketId, string operation, Exception exception)
    {
        var serviceException = GetServiceException(exception);
        var message = serviceException is null
            ? $"{operation}：{exception.Message}"
            : $"{operation}：{serviceException.ErrorCode} {serviceException.ErrorMessage}".Trim();
        SetStatus(bucketId, message);
        this.ShowToast(message);
    }

    private async Task OpenTextEditorAsync(BucketProfile bucket, ObjectEntry entry)
    {
        bucket = FindBucket(bucket.Id) ?? bucket;
        if (!IsTextFile(entry.Name))
        {
            SetStatus(bucket.Id, "该文件类型不支持文本预览");
            return;
        }

        const int maximumBytes = 2 * 1024 * 1024;
        if (entry.SizeBytes > maximumBytes)
        {
            SetStatus(bucket.Id, "文本文件超过 2 MB，无法在线编辑");
            return;
        }

        var credential = LoadCredential(bucket.Id);
        if (credential is null)
        {
            SetStatus(bucket.Id, "未找到 AccessKey 配置");
            return;
        }

        try
        {
            SetStatus(bucket.Id, $"正在加载：{entry.Name}");
            var original = await _ossObjectService.GetTextObjectAsync(
                bucket,
                credential,
                entry.Key,
                maximumBytes);
            var editor = new TextEditorOverlay(bucket.Name, entry.Name, entry.Key, original);
            var edited = await editor.ShowAsync(this);
            if (edited is null || string.Equals(edited, original, StringComparison.Ordinal))
            {
                SetStatus(bucket.Id, edited is null ? "已取消编辑" : "文件内容未修改");
                return;
            }

            SetStatus(bucket.Id, $"正在保存：{entry.Name}");
            await _ossObjectService.PutTextObjectAsync(bucket, credential, entry.Key, edited);
            SetStatus(bucket.Id, $"已保存：{entry.Name}");
            this.ShowToast($"已保存到 OSS：{entry.Name}");
            await RefreshBucketAsync(bucket);
        }
        catch (Exception exception)
        {
            ShowOperationError(bucket.Id, "打开文本文件失败", exception);
        }
    }

    private void ConfigureDropUpload(Border dropSurface, BucketProfile bucket)
    {
        var normalBorder = Color.White.WithAlpha(0);
        var normalBackground = ObjectListSurface;
        var activeBorder = Teal.WithAlpha(165);
        dropSurface.AllowDrop = true;
        dropSurface.DragEnter += args =>
        {
            if (!TryGetDroppedPaths(args, out _))
            {
                return;
            }

            args.Accepted = true;
            args.Effect = DragDropEffects.Copy;
            args.Handled = true;
            dropSurface.BorderBrush = activeBorder;
            dropSurface.Background = Teal.WithAlpha(12);
            SetStatus(bucket.Id, "释放鼠标以上传文件或文件夹到当前目录");
        };
        dropSurface.DragOver += args =>
        {
            if (TryGetDroppedPaths(args, out _))
            {
                args.Accepted = true;
                args.Effect = DragDropEffects.Copy;
                args.Handled = true;
            }
        };
        dropSurface.DragLeave += _ =>
        {
            dropSurface.BorderBrush = normalBorder;
            dropSurface.Background = normalBackground;
        };
        dropSurface.Drop += args =>
        {
            dropSurface.BorderBrush = normalBorder;
            dropSurface.Background = normalBackground;
            if (!TryGetDroppedPaths(args, out var paths))
            {
                return;
            }

            args.Accepted = true;
            args.Effect = DragDropEffects.Copy;
            args.Handled = true;
            _ = UploadDroppedPathsAsync(bucket, paths);
        };
    }

    private static bool TryGetDroppedPaths(DragEventArgs args, out IReadOnlyList<string> paths)
    {
        if (args.Data.TryGetData<IReadOnlyList<string>>(StandardDataFormats.StorageItems, out var items) &&
            items is { Count: > 0 })
        {
            paths = items
                .Where(path => File.Exists(path) || Directory.Exists(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return paths.Count > 0;
        }

        paths = [];
        return false;
    }

    private async Task UploadDroppedPathsAsync(
        BucketProfile bucket,
        IReadOnlyList<string> paths,
        string? retryPrefix = null)
    {
        bucket = FindBucket(bucket.Id) ?? bucket;
        var credential = LoadCredential(bucket.Id);
        if (credential is null)
        {
            SetStatus(bucket.Id, "未找到 AccessKey 配置");
            return;
        }

        var prefix = retryPrefix ?? GetTabState(bucket).CurrentPrefix;
        var title = paths.Count == 1
            ? Path.GetFileName(paths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : $"{paths.Count} 个本地项目";
        var transferTask = TryBeginFileTransfer(bucket, "上传", title);
        if (transferTask is null)
        {
            return;
        }
        transferTask.RetryAsync = () => UploadDroppedPathsAsync(bucket, paths, prefix);
        var cancellation = transferTask.Cancellation;

        try
        {
            SetStatus(bucket.Id, "正在准备拖拽上传…");
            var progress = new Progress<(int Current, int Total, string Name)>(value =>
            {
                if (!transferTask.IsRunning)
                {
                    return;
                }

                SetStatus(bucket.Id, $"正在上传 {value.Current}/{value.Total}：{value.Name}");
                UpdateTransferTask(transferTask, value.Current, value.Total, value.Name);
            });
            var count = await _ossObjectService.UploadPathsAsync(
                bucket,
                credential,
                prefix,
                paths,
                progress,
                cancellation.Token);
            var message = count == 0 ? "未找到可上传的文件" : $"上传完成，共 {count} 个文件";
            FinishTransferTask(transferTask, "已完成", message, count, count);
            SetStatus(bucket.Id, message);
            this.ShowToast(message);
            await RefreshBucketAsync(bucket);
        }
        catch (OperationCanceledException)
        {
            FinishTransferTask(transferTask, "已取消", "上传已取消，已完成的文件仍会保留");
            SetStatus(bucket.Id, "上传已取消，已完成的文件仍会保留");
        }
        catch (Exception exception)
        {
            FinishTransferTask(transferTask, "失败", exception.Message);
            ShowOperationError(bucket.Id, "上传失败", exception);
        }
        finally
        {
            EndTransfer(bucket.Id, cancellation);
        }
    }

    private static bool IsTextFile(string name)
    {
        var fileName = Path.GetFileName(name).ToLowerInvariant();
        if (fileName is ".env" or ".gitignore" or ".gitattributes" or "dockerfile" or "makefile")
        {
            return true;
        }

        return Path.GetExtension(fileName) is
            ".txt" or ".md" or ".markdown" or ".json" or ".xml" or ".yaml" or ".yml" or
            ".csv" or ".tsv" or ".html" or ".htm" or ".css" or ".scss" or ".less" or
            ".js" or ".mjs" or ".cjs" or ".ts" or ".tsx" or ".jsx" or ".vue" or ".svelte" or
            ".cs" or ".java" or ".py" or ".go" or ".rs" or ".php" or ".rb" or ".sql" or
            ".sh" or ".ps1" or ".bat" or ".cmd" or ".ini" or ".conf" or ".config" or
            ".properties" or ".log" or ".graphql" or ".gql" or ".toml" or ".svg";
    }

    private GridViewColumn<ObjectEntry>[] CreateObjectColumns(string bucketId, double nameWidth)
    {
        return
        [
            new GridViewColumn<ObjectEntry>()
                .Header("")
                .Width(42)
                .IsResizable(false)
                .Template(
                    build: _ => CreateRowCell(
                        bucketId,
                        new ObjectSelectionCell(),
                        horizontalPadding: 0),
                    bind: (Border cell, ObjectEntry entry) =>
                    {
                        BindRowCell(cell, bucketId, entry.Key);
                        ((ObjectSelectionCell)cell.Child!).SetChecked(IsObjectChecked(bucketId, entry));
                    }),
            CreateTextObjectColumn(bucketId, "名称", nameWidth, entry => entry.DisplayName),
            CreateTextObjectColumn(bucketId, "大小", 120, entry => entry.Size),
            CreateTextObjectColumn(bucketId, "最后修改时间", 175, entry => entry.DisplayModified),
            new GridViewColumn<ObjectEntry>()
                .Header("操作")
                .Width(150)
                .IsResizable(false)
                .Template(
                    build: _ => CreateRowCell(bucketId, CreateObjectActionCell(bucketId), horizontalPadding: 12),
                    bind: (Border cell, ObjectEntry entry) =>
                    {
                        BindRowCell(cell, bucketId, entry.Key);
                        ((ObjectActionCell)cell.Child!).Entry = entry;
                    })
        ];
    }

    private GridViewColumn<ObjectEntry> CreateTextObjectColumn(
        string bucketId,
        string header,
        double width,
        Func<ObjectEntry, string> textSelector)
    {
        return new GridViewColumn<ObjectEntry>()
            .Header(header)
            .Width(width)
            .IsResizable(false)
            .Template(
                build: _ => CreateRowCell(
                    bucketId,
                    new TextBlock().CenterVertical(),
                    horizontalPadding: 10),
                bind: (Border cell, ObjectEntry entry) =>
                {
                    BindRowCell(cell, bucketId, entry.Key);
                    ((TextBlock)cell.Child!).Text = textSelector(entry);
                });
    }

    private static TextBlock CreateHeaderText(string text)
    {
        return new TextBlock()
            .Text(text)
            .Margin(10, 0)
            .CenterVertical();
    }

    private Border CreateRowCell(
        string bucketId,
        FrameworkElement content,
        double horizontalPadding)
    {
        var cell = new Border()
            .Padding(horizontalPadding, 0)
            .Background(ObjectListSurface)
            .BorderBrush(RowDividerColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .Child(content);
        cell
            .OnMouseEnter(() => SetRowCellHover(cell, true))
            .OnMouseLeave(() => SetRowCellHover(cell, false));
        return cell;
    }

    private void BindRowCell(Border cell, string bucketId, string key)
    {
        if (_rowCellAssignments.TryGetValue(cell, out var previous) &&
            _rowCellsByObject.TryGetValue(previous, out var previousCells))
        {
            previousCells.Remove(cell);
            if (previousCells.Count == 0)
            {
                _rowCellsByObject.Remove(previous);
            }
        }

        var assignment = (bucketId, key);
        _rowCellAssignments[cell] = assignment;
        if (!_rowCellsByObject.TryGetValue(assignment, out var cells))
        {
            cells = [];
            _rowCellsByObject[assignment] = cells;
        }

        cells.Add(cell);
        cell.Background = GetObjectRowBackground(bucketId, key);
    }

    private void SetRowCellHover(Border sourceCell, bool isHovered)
    {
        if (!_rowCellAssignments.TryGetValue(sourceCell, out var assignment))
        {
            return;
        }

        if (isHovered)
        {
            var tabState = _tabStates.GetValueOrDefault(assignment.BucketId);
            var previousKey = tabState?.HoveredObjectKey;
            if (previousKey is not null && previousKey != assignment.Key)
            {
                SetObjectCellsBackground(
                    assignment.BucketId,
                    previousKey,
                    GetObjectRowBackground(assignment.BucketId, previousKey, includeHover: false));
            }

            if (tabState is not null)
            {
                tabState.HoveredObjectKey = assignment.Key;
            }
            SetObjectCellsBackground(
                assignment.BucketId,
                assignment.Key,
                GetObjectRowBackground(assignment.BucketId, assignment.Key));
        }
        else if (_tabStates.GetValueOrDefault(assignment.BucketId)?.HoveredObjectKey == assignment.Key)
        {
            _tabStates.GetValueOrDefault(assignment.BucketId)!.HoveredObjectKey = null;
            SetObjectCellsBackground(
                assignment.BucketId,
                assignment.Key,
                GetObjectRowBackground(assignment.BucketId, assignment.Key));
        }
    }

    private Color GetObjectRowBackground(string bucketId, string key, bool includeHover = true)
    {
        if (_tabStates.GetValueOrDefault(bucketId)?.CheckedObjectKeys.Contains(key) == true)
        {
            return ObjectRowSelected;
        }

        return includeHover && _tabStates.GetValueOrDefault(bucketId)?.HoveredObjectKey == key
            ? ObjectRowHover
            : ObjectListSurface;
    }

    private void SetObjectCellsBackground(string bucketId, string key, Color color)
    {
        if (_rowCellsByObject.TryGetValue((bucketId, key), out var cells))
        {
            foreach (var cell in cells)
            {
                cell.Background = color;
            }
        }
    }

    private void ClearObjectHover(string bucketId)
    {
        if (_tabStates.GetValueOrDefault(bucketId)?.HoveredObjectKey is { } key)
        {
            _tabStates[bucketId].HoveredObjectKey = null;
            SetObjectCellsBackground(bucketId, key, GetObjectRowBackground(bucketId, key));
        }
    }

    private void ClearRowCellBindings(string bucketId)
    {
        foreach (var cell in _rowCellAssignments
                     .Where(pair => pair.Value.BucketId == bucketId)
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            _rowCellAssignments.Remove(cell);
        }

        foreach (var assignment in _rowCellsByObject.Keys
                     .Where(key => key.BucketId == bucketId)
                     .ToArray())
        {
            _rowCellsByObject.Remove(assignment);
        }
    }

    private bool IsObjectChecked(string bucketId, ObjectEntry entry)
        => _tabStates.GetValueOrDefault(bucketId)?.CheckedObjectKeys.Contains(entry.Key) == true;

    private void SetObjectChecked(string bucketId, ObjectEntry entry, bool isChecked)
    {
        var keys = _tabStates.GetValueOrDefault(bucketId)?.CheckedObjectKeys;
        if (keys is null)
        {
            return;
        }

        if (isChecked)
        {
            keys.Add(entry.Key);
        }
        else
        {
            keys.Remove(entry.Key);
        }

        RefreshObjectSelectionVisuals(bucketId);
        SetStatus(bucketId, $"{FindBucket(bucketId)?.Objects.Count ?? 0} 个对象 · 已勾选 {keys.Count} 个");
        UpdateHeaderCheckBox(bucketId);
    }

    private void ToggleObjectSelection(BucketProfile bucket, ObjectEntry entry)
    {
        var isChecked = IsObjectChecked(bucket.Id, entry);
        SetObjectChecked(bucket.Id, entry, !isChecked);
        if (isChecked)
        {
            if (GetTabState(bucket).SelectedEntry?.Key == entry.Key)
            {
                GetTabState(bucket).SelectedEntry = null;
            }
        }
        else
        {
            GetTabState(bucket).SelectedEntry = entry;
        }

        var selectedCount = GetTabState(bucket).CheckedObjectKeys.Count;
        SetStatus(bucket.Id, isChecked
            ? $"已取消选择：{entry.Name} · 当前已选 {selectedCount} 个"
            : $"已选中：{entry.Name} · 当前已选 {selectedCount} 个");
    }

    private void HandleObjectRowClick(BucketProfile bucket, ObjectEntry entry)
    {
        var now = Environment.TickCount64;
        var isDoubleClick =
            GetTabState(bucket).LastObjectClick is { } previousClick &&
            string.Equals(previousClick.Key, entry.Key, StringComparison.Ordinal) &&
            now - previousClick.Timestamp is >= 0 and <= ObjectDoubleClickIntervalMilliseconds;

        if (isDoubleClick)
        {
            GetTabState(bucket).LastObjectClick = null;
            if (entry.IsFolder)
            {
                _ = NavigateToPrefixAsync(bucket, entry.Key);
            }
            else
            {
                _ = OpenTextEditorAsync(bucket, entry);
            }

            return;
        }

        GetTabState(bucket).LastObjectClick = (entry.Key, now);
        ToggleObjectSelection(bucket, entry);
    }

    private void HandleObjectViewMouseDown(GridView objectView, BucketProfile bucket, MouseEventArgs args)
    {
        if (!objectView.TryGetCellIndexAt(args, out var rowIndex, out var columnIndex, out var isHeader) ||
            isHeader ||
            objectView.ItemsSource.GetItem(rowIndex) is not ObjectEntry entry)
        {
            return;
        }

        if (args.Button == MouseButton.Right)
        {
            var tabState = GetTabState(bucket);
            tabState.SelectedEntry = entry;
            tabState.LastObjectClick = null;
            ShowObjectContextMenu(objectView, bucket, entry, args);
            args.Handled = true;
            return;
        }

        if (args.Button != MouseButton.Left || columnIndex >= 4)
        {
            return;
        }

        if (columnIndex == 0)
        {
            GetTabState(bucket).LastObjectClick = null;
            ToggleObjectSelection(bucket, entry);
        }
        else
        {
            HandleObjectRowClick(bucket, entry);
        }
    }

    private void ShowObjectContextMenu(
        GridView objectView,
        BucketProfile bucket,
        ObjectEntry entry,
        MouseEventArgs args)
    {
        var menu = new ContextMenu()
            .Item("下载", () => _ = DownloadEntryAsync(bucket.Id, entry))
            .Item("复制", () => _ = CopySelectedAsync(bucket.Id, [entry]))
            .Item("移动", () => _ = MoveEntryAsync(bucket.Id, entry))
            .Item("重命名", () => _ = RenameEntryAsync(bucket.Id, entry));
        if (!entry.IsFolder)
        {
            menu.Separator().Item("获取地址", () => CopyObjectAddress(bucket.Id, entry));
        }
        else
        {
            menu.Separator().Item("添加到书签", () => AddBookmark(bucket, entry.Key));
        }

        _objectContextMenu = menu;
        menu.ShowAt(objectView, ScreenToClient(args.ScreenPosition));
    }

    private void ToggleAllObjects(BucketProfile bucket)
    {
        var keys = GetTabState(bucket).CheckedObjectKeys;
        SetAllObjects(bucket, !(bucket.Objects.Count > 0 && keys.Count == bucket.Objects.Count));
    }

    private void SetAllObjects(BucketProfile bucket, bool isChecked)
    {
        var keys = isChecked
            ? bucket.Objects.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal)
            : [];

        GetTabState(bucket).CheckedObjectKeys = keys;
        RefreshObjectSelectionVisuals(bucket.Id);
        UpdateHeaderCheckBox(bucket.Id);
        SetStatus(bucket.Id, $"{bucket.Objects.Count} 个对象 · 已勾选 {keys.Count} 个");
    }

    private void ClearCheckedObjects(string bucketId)
    {
        if (_tabStates.TryGetValue(bucketId, out var tabState))
        {
            tabState.CheckedObjectKeys.Clear();
        }
        RefreshObjectSelectionVisuals(bucketId);
        UpdateHeaderCheckBox(bucketId);
    }

    private void RefreshObjectSelectionVisuals(string bucketId)
    {
        foreach (var pair in _rowCellsByObject)
        {
            if (pair.Key.BucketId != bucketId)
            {
                continue;
            }

            var isChecked = _tabStates.GetValueOrDefault(bucketId)?.CheckedObjectKeys.Contains(pair.Key.Key) == true;
            var background = GetObjectRowBackground(bucketId, pair.Key.Key);
            foreach (var cell in pair.Value)
            {
                cell.Background = background;
                if (cell.Child is ObjectSelectionCell selectionCell)
                {
                    selectionCell.SetChecked(isChecked);
                }
            }
        }
    }

    private void UpdateHeaderCheckBox(string bucketId)
    {
        if (!_headerCheckBoxes.TryGetValue(bucketId, out var checkBox) || FindBucket(bucketId) is not { } bucket)
        {
            return;
        }

        var selectedCount = _tabStates.GetValueOrDefault(bucketId)?.CheckedObjectKeys.Count ?? 0;
        _updatingHeaderCheckBoxes.Add(bucketId);
        try
        {
            checkBox.IsChecked = selectedCount == 0
                ? false
                : selectedCount == bucket.Objects.Count
                    ? true
                    : null;
        }
        finally
        {
            _updatingHeaderCheckBoxes.Remove(bucketId);
        }
    }

    private IReadOnlyList<ObjectEntry> GetCheckedEntries(string bucketId)
    {
        if (FindBucket(bucketId) is not { } bucket ||
            _tabStates.GetValueOrDefault(bucketId)?.CheckedObjectKeys is not { Count: > 0 } keys)
        {
            return [];
        }

        return bucket.Objects.Where(entry => keys.Contains(entry.Key)).ToArray();
    }

    private void SetStatus(string message)
    {
        SetStatus(_state.ActiveBucketId, message);
    }

    private void SetStatus(string bucketId, string message)
    {
        if (_statusTexts.TryGetValue(bucketId, out var status))
        {
            status.Value = message;
        }
    }

    private BucketProfile? FindBucket(string bucketId)
        => _state.Buckets.FirstOrDefault(bucket => bucket.Id == bucketId);

    private BucketTabState GetTabState(BucketProfile bucket)
    {
        if (!_tabStates.TryGetValue(bucket.Id, out var tabState))
        {
            tabState = new BucketTabState(bucket.Prefix);
            _tabStates[bucket.Id] = tabState;
        }

        return tabState;
    }

    private int FindTabIndex(TabItem tab)
    {
        for (var index = 0; index < _tabs.Tabs.Count; index++)
        {
            if (ReferenceEquals(_tabs.Tabs[index], tab))
            {
                return index;
            }
        }

        return -1;
    }

    private void SaveWorkspace()
        => _stateStore.Save(_state);

    private sealed class ObjectActionCell : StackPanel
    {
        private readonly Action<ObjectEntry> _download;
        private readonly Action<ObjectEntry> _delete;

        public ObjectActionCell(
            Action<ObjectEntry> download,
            Action<ObjectEntry> delete,
            Color downloadColor,
            Color deleteColor)
        {
            _download = download;
            _delete = delete;
            Orientation = Orientation.Horizontal;
            Spacing = 6;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Center;

            AddRange(
                CreateButton("下载", "下载此文件或目录", downloadColor, () => Invoke(_download)),
                CreateButton("删除", "删除此文件或目录", deleteColor, () => Invoke(_delete)));
        }

        public ObjectEntry? Entry { get; set; }

        private void Invoke(Action<ObjectEntry> action)
        {
            if (Entry is { } entry)
            {
                action(entry);
            }
        }

        private static Button CreateButton(string text, string toolTip, Color color, Action action)
        {
            var normal = color.WithAlpha(32);
            var hover = color.WithAlpha(52);
            var pressed = color.WithAlpha(82);
            return new Button()
                .Content(text, accessKey: false)
                .Padding(9, 4)
                .CornerRadius(5)
                .Background(normal)
                .BorderBrush(color.WithAlpha(105))
                .Foreground(color)
                .OnClick(action)
                .WithFeedback(toolTip, normal, hover, pressed);
        }
    }

    private sealed class ObjectSelectionCell : StackPanel
    {
        private readonly CheckBox _checkBox = new();

        public ObjectSelectionCell()
        {
            HorizontalAlignment = HorizontalAlignment.Center;
            VerticalAlignment = VerticalAlignment.Center;
            IsHitTestVisible = false;
            Add(_checkBox);
        }

        public void SetChecked(bool isChecked)
        {
            _checkBox.IsChecked = isChecked;
        }
    }
}
