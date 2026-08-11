using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using OSSStudio.Models;
using OSSStudio.Services;

namespace OSSStudio.UI;

public sealed class OssMainWindow : Window
{
    private static readonly Color Surface = Color.FromHex("#F4F5F3");
    private static readonly Color SidebarSurface = Color.FromHex("#EEF2F1");
    private static readonly Color HeaderSurface = Color.FromHex("#E7ECEC");
    private static readonly Color BorderColor = Color.FromHex("#CFD9D9");
    private static readonly Color RowDividerColor = Color.FromHex("#D6E0DE");
    private static readonly Color ObjectListSurface = Color.White;
    private static readonly Color ObjectRowHover = Color.FromHex("#E6F0EE");
    private static readonly Color ObjectRowSelected = Color.FromHex("#C9DFDB");
    private static readonly Color MutedText = Color.FromHex("#687982");
    private static readonly Color Teal = Color.FromHex("#438F86");
    private static readonly Color Amber = Color.FromHex("#C5903D");
    private static readonly Color Blue = Color.FromHex("#6384AD");
    private static readonly Color Purple = Color.FromHex("#7D6A9D");
    private static readonly Color Coral = Color.FromHex("#A86F67");
    private const string AppIconResourceName = "OSSStudio.Assets.OSS-Studio.ico";
    private const string AppLogoResourceName = "OSSStudio.Assets.OSS-Studio.png";
    private const long ObjectDoubleClickIntervalMilliseconds = 200;

    private readonly WorkspaceState _state;
    private readonly WorkspaceStateStore _stateStore;
    private readonly BucketCredentialStore _credentialStore = new();
    private readonly OssObjectService _ossObjectService = new();
    private readonly TabControl _tabs = new();
    private readonly TreeView _bucketTree = new();
    private readonly TextBox _searchBox = new();
    private readonly TextBlock _bucketCountText = new();
    private readonly ContextMenu _bucketContextMenu = new();
    private readonly Grid _modalLayer = new();
    private readonly Dictionary<TabItem, string> _bucketIdsByTab = [];
    private readonly Dictionary<string, TreeViewNode> _bucketNodesById = [];
    private readonly Dictionary<string, GridView> _objectViews = [];
    private readonly Dictionary<string, ObservableValue<string>> _statusTexts = [];
    private readonly Dictionary<string, ObservableValue<string>> _pathTexts = [];
    private readonly Dictionary<string, string> _currentPrefixes = [];
    private readonly Dictionary<string, List<string>> _navigationHistories = [];
    private readonly Dictionary<string, int> _navigationHistoryIndices = [];
    private readonly Dictionary<string, ObjectEntry> _selectedEntries = [];
    private readonly Dictionary<string, HashSet<string>> _checkedObjectKeys = [];
    private readonly Dictionary<string, CheckBox> _headerCheckBoxes = [];
    private readonly HashSet<string> _updatingHeaderCheckBoxes = [];
    private readonly Dictionary<Border, (string BucketId, string Key)> _rowCellAssignments = [];
    private readonly Dictionary<(string BucketId, string Key), HashSet<Border>> _rowCellsByObject = [];
    private readonly Dictionary<string, string> _hoveredObjectKeys = [];
    private readonly Dictionary<string, int> _refreshVersions = [];
    private readonly Dictionary<string, (string Key, long Timestamp)> _lastObjectClicks = [];
    private readonly Dictionary<string, BucketCredential> _sessionCredentials = [];

    private string _searchText = string.Empty;
    private bool _suppressBucketOpen;
    private bool _hasLoaded;

    public OssMainWindow(WorkspaceState state, WorkspaceStateStore stateStore)
    {
        _state = state;
        _stateStore = stateStore;
        Title = "OSS Studio";
        Icon = IconSource.FromResource(typeof(OssMainWindow).Assembly, AppIconResourceName);
        WindowSize = WindowSize.Resizable(1180, 760, minWidth: 900, minHeight: 620);
        Padding = new Thickness(0);
        Background = Color.FromHex("#E9EEEE");

        _searchBox
            .Placeholder("搜索当前 Bucket 中的对象")
            .Height(34)
            .OnTextChanged(text =>
            {
                _searchText = text.Trim();
                ApplyCurrentFilter();
            });

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
                FlatButton("传输任务", () => SetStatus("传输队列目前为空")),
                FlatButton("通知", () => SetStatus("暂无新通知")),
                FlatButton("设置", () => SetStatus("设置功能将在下一阶段接入")));

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
                        _searchBox.StretchHorizontal().CenterVertical(),
                        windowActions));
    }

    private FrameworkElement BuildSidebar()
    {
        _bucketCountText.Text = $"资源桶（{_state.Buckets.Count}）";
        _bucketCountText.Foreground = MutedText;

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

        return new Border()
            .Background(SidebarSurface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 0, 1, 0))
            .Padding(12)
            .Child(
                new DockPanel()
                    .LastChildFill()
                    .Children(treeArea));
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
        _currentPrefixes.TryAdd(bucket.Id, bucket.Prefix);
        if (!_navigationHistories.ContainsKey(bucket.Id))
        {
            _navigationHistories[bucket.Id] = [bucket.Prefix];
            _navigationHistoryIndices[bucket.Id] = 0;
        }
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

        var navigationBar = new Border()
            .Padding(8, 6)
            .Background(Surface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .Child(
                new Grid()
                    .Columns("Auto,Auto,Auto,Auto,Auto,*")
                    .AutoIndexing()
                    .Children(
                        BrowserToolbarButton("←", "返回上一个访问目录", () => NavigateHistory(bucket, -1)),
                        BrowserToolbarButton("→", "前进到下一个访问目录", () => NavigateHistory(bucket, 1)),
                        BrowserToolbarButton("↑", "返回上一级目录", () => NavigateUp(bucket)),
                        BrowserToolbarButton("↻", "刷新当前目录", () => _ = RefreshBucketAsync(bucket)),
                        BrowserToolbarButton("⌂", "返回资源桶预设目录", () => _ = NavigateToPrefixAsync(bucket, bucket.Prefix)),
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
                new StackPanel()
                    .Horizontal()
                    .Spacing(4)
                    .CenterVertical()
                    .Children(
                        BrowserToolbarButton("↥ 文件", "上传一个或多个文件到当前目录", () => SetStatus(bucket.Id, "请选择要上传的文件")),
                        BrowserToolbarButton("↥ 目录", "上传本地目录到当前目录", () => SetStatus(bucket.Id, "请选择要上传的目录")),
                        BrowserToolbarButton("✚ 创建目录", "在当前路径创建新目录", () => SetStatus(bucket.Id, "新建目录操作已准备")),
                        BrowserToolbarButton("□ 全选", "选择或取消选择当前目录中的全部对象", () => ToggleAllObjects(bucket)),
                        BrowserToolbarButton("⇩ 下载", "下载当前选中的文件或目录", () => DownloadSelected(bucket.Id)),
                        BrowserToolbarButton("▣ 复制", "复制当前选中的对象", () => SetStatus(bucket.Id, "请选择要复制的对象")),
                        BrowserToolbarButton("更多 ▾", "显示更多对象操作", () => SetStatus(bucket.Id, "更多操作"))));

        var statusBar = new Border()
            .Padding(14, 7)
            .Background(Color.FromHex("#E5EAE8"))
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
        _currentPrefixes.Remove(bucketId);
        _navigationHistories.Remove(bucketId);
        _navigationHistoryIndices.Remove(bucketId);
        _selectedEntries.Remove(bucketId);
        _checkedObjectKeys.Remove(bucketId);
        _headerCheckBoxes.Remove(bucketId);
        _updatingHeaderCheckBoxes.Remove(bucketId);
        _refreshVersions.Remove(bucketId);
        _lastObjectClicks.Remove(bucketId);
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

    private void ApplyCurrentFilter()
    {
        var bucket = FindBucket(_state.ActiveBucketId);
        if (bucket is null || !_objectViews.TryGetValue(bucket.Id, out var view))
        {
            return;
        }

        IReadOnlyList<ObjectEntry> filtered = string.IsNullOrWhiteSpace(_searchText)
            ? bucket.Objects
            : bucket.Objects
                .Where(entry => entry.Name.Contains(_searchText, StringComparison.OrdinalIgnoreCase))
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
        if (!_objectViews.TryGetValue(bucket.Id, out var currentView))
        {
            return;
        }

        var refreshVersion = _refreshVersions.GetValueOrDefault(bucket.Id) + 1;
        _refreshVersions[bucket.Id] = refreshVersion;
        _lastObjectClicks.Remove(bucket.Id);
        var prefix = _currentPrefixes.GetValueOrDefault(bucket.Id, bucket.Prefix);
        _selectedEntries.Remove(bucket.Id);
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
            if (_refreshVersions.GetValueOrDefault(bucket.Id) != refreshVersion ||
                !string.Equals(_currentPrefixes.GetValueOrDefault(bucket.Id, bucket.Prefix), prefix, StringComparison.Ordinal) ||
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
            if (_state.ActiveBucketId == bucket.Id && !string.IsNullOrWhiteSpace(_searchText))
            {
                ApplyCurrentFilter();
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
            if (_refreshVersions.GetValueOrDefault(bucket.Id) != refreshVersion)
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
            if (_refreshVersions.GetValueOrDefault(bucket.Id) != refreshVersion)
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
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix)
            ? string.Empty
            : prefix.EndsWith('/') ? prefix : $"{prefix}/";
        if (recordHistory && !string.Equals(
                _currentPrefixes.GetValueOrDefault(bucket.Id),
                normalizedPrefix,
                StringComparison.Ordinal))
        {
            var history = _navigationHistories.GetValueOrDefault(bucket.Id) ?? [bucket.Prefix];
            var currentIndex = _navigationHistoryIndices.GetValueOrDefault(bucket.Id);
            if (currentIndex < history.Count - 1)
            {
                history.RemoveRange(currentIndex + 1, history.Count - currentIndex - 1);
            }

            history.Add(normalizedPrefix);
            _navigationHistories[bucket.Id] = history;
            _navigationHistoryIndices[bucket.Id] = history.Count - 1;
        }

        _currentPrefixes[bucket.Id] = normalizedPrefix;
        UpdatePathText(bucket);
        await RefreshBucketAsync(bucket);
    }

    private void NavigateHistory(BucketProfile bucket, int offset)
    {
        if (!_navigationHistories.TryGetValue(bucket.Id, out var history))
        {
            return;
        }

        var currentIndex = _navigationHistoryIndices.GetValueOrDefault(bucket.Id);
        var nextIndex = currentIndex + offset;
        if (nextIndex < 0 || nextIndex >= history.Count)
        {
            SetStatus(bucket.Id, offset < 0 ? "已经是最早的浏览记录" : "已经是最新的浏览记录");
            return;
        }

        _navigationHistoryIndices[bucket.Id] = nextIndex;
        _ = NavigateToPrefixAsync(bucket, history[nextIndex], recordHistory: false);
    }

    private void NavigateUp(BucketProfile bucket)
    {
        var rootPrefix = bucket.Prefix;
        var currentPrefix = _currentPrefixes.GetValueOrDefault(bucket.Id, rootPrefix);
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
        var currentPrefix = _currentPrefixes.GetValueOrDefault(bucket.Id, bucket.Prefix);
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
        if (_state.ActiveBucketId == bucket.Id)
        {
            _state.ActiveBucketId = _state.OpenBucketIds.FirstOrDefault() ?? string.Empty;
        }

        ReloadBucketViews();
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
        _currentPrefixes.Clear();
        _navigationHistories.Clear();
        _navigationHistoryIndices.Clear();
        _selectedEntries.Clear();
        _checkedObjectKeys.Clear();
        _headerCheckBoxes.Clear();
        _updatingHeaderCheckBoxes.Clear();
        _rowCellAssignments.Clear();
        _rowCellsByObject.Clear();
        _hoveredObjectKeys.Clear();
        _refreshVersions.Clear();
        _lastObjectClicks.Clear();
        _state.OpenBucketIds.Clear();
        _bucketTree.ItemsSource(CreateBucketNodes());
        _bucketCountText.Text = $"资源桶（{_state.Buckets.Count}）";

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

    private Button FlatButton(string text, Action action)
    {
        return new Button()
            .Content(text, accessKey: false)
            .StyleName(BuiltInStyles.FlatButton)
            .Padding(9, 5)
            .OnClick(action)
            .WithFeedback(text.TrimStart('＋', '↻', ' '), Color.White.WithAlpha(0), HeaderSurface, BorderColor);
    }

    private static Button BrowserToolbarButton(string text, string toolTip, Action action)
    {
        var normal = HeaderSurface;
        var hover = Color.FromHex("#DCE7E5");
        var pressed = Color.FromHex("#C5D9D5");
        return new Button()
            .Content(text, accessKey: false)
            .Margin(0, 0, 4, 0)
            .Padding(10, 5)
            .CornerRadius(3)
            .Background(normal)
            .BorderBrush(BorderColor)
            .Foreground(Color.FromHex("#304642"))
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
            delete: entry => _ = DeleteEntryAsync(bucketId, entry));
    }

    private void DownloadSelected(string bucketId)
    {
        var checkedEntries = GetCheckedEntries(bucketId);
        if (checkedEntries.Count > 1)
        {
            SetStatus(bucketId, $"已勾选 {checkedEntries.Count} 个对象，请选择单个对象下载");
        }
        else if (checkedEntries.FirstOrDefault() is { } checkedEntry)
        {
            _ = DownloadEntryAsync(bucketId, checkedEntry);
        }
        else if (_selectedEntries.TryGetValue(bucketId, out var entry))
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
        var checkedEntries = GetCheckedEntries(bucketId);
        if (checkedEntries.Count > 1)
        {
            SetStatus(bucketId, $"已勾选 {checkedEntries.Count} 个对象，请选择单个对象删除");
        }
        else if (checkedEntries.FirstOrDefault() is { } checkedEntry)
        {
            _ = DeleteEntryAsync(bucketId, checkedEntry);
        }
        else if (_selectedEntries.TryGetValue(bucketId, out var entry))
        {
            _ = DeleteEntryAsync(bucketId, entry);
        }
        else
        {
            SetStatus(bucketId, "请先选择要删除的文件或文件夹");
        }
    }

    private async Task DownloadEntryAsync(string bucketId, ObjectEntry entry)
    {
        var bucket = FindBucket(bucketId);
        var credential = bucket is null ? null : LoadCredential(bucket.Id);
        if (bucket is null || credential is null)
        {
            SetStatus(bucketId, "未找到资源桶或 AccessKey 配置");
            return;
        }

        try
        {
            if (entry.IsFolder)
            {
                var destination = await FileDialog.SelectFolderAsync(new FolderDialogOptions
                {
                    Owner = this,
                    Title = $"选择“{entry.Name.TrimEnd('/')}”的下载位置"
                });
                if (string.IsNullOrWhiteSpace(destination))
                {
                    return;
                }

                var progress = new Progress<(int Current, int Total, string Name)>(value =>
                    SetStatus(bucketId, $"正在下载 {value.Current}/{value.Total}：{value.Name}"));
                var count = await _ossObjectService.DownloadFolderAsync(
                    bucket,
                    credential,
                    entry.Key,
                    destination,
                    progress);
                var message = $"文件夹下载完成，共 {count} 个文件";
                SetStatus(bucketId, message);
                this.ShowToast(message);
            }
            else
            {
                var destination = await FileDialog.SaveFileAsync(new SaveFileDialogOptions
                {
                    Owner = this,
                    Title = $"下载 {entry.Name}",
                    FileName = Path.GetFileName(entry.Name),
                    OverwritePrompt = true
                });
                if (string.IsNullOrWhiteSpace(destination))
                {
                    return;
                }

                SetStatus(bucketId, $"正在下载：{entry.Name}");
                await _ossObjectService.DownloadObjectAsync(bucket, credential, entry.Key, destination);
                var message = $"下载完成：{entry.Name}";
                SetStatus(bucketId, message);
                this.ShowToast(message);
            }
        }
        catch (Exception exception)
        {
            ShowOperationError(bucketId, "下载失败", exception);
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

        try
        {
            SetStatus(bucketId, $"正在删除：{entry.Name}");
            var deletedCount = entry.IsFolder
                ? await _ossObjectService.DeleteFolderAsync(bucket, credential, entry.Key)
                : await DeleteSingleObjectAsync(bucket, credential, entry.Key);
            _selectedEntries.Remove(bucketId);
            _checkedObjectKeys.Remove(bucketId);
            var message = entry.IsFolder
                ? $"已删除文件夹中的 {deletedCount} 个对象"
                : $"已删除：{entry.Name}";
            SetStatus(bucketId, message);
            this.ShowToast(message);
            await RefreshBucketAsync(bucket);
        }
        catch (Exception exception)
        {
            ShowOperationError(bucketId, "删除失败", exception);
        }
    }

    private async Task<int> DeleteSingleObjectAsync(
        BucketProfile bucket,
        BucketCredential credential,
        string key)
    {
        await _ossObjectService.DeleteObjectAsync(bucket, credential, key);
        return 1;
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

    private async Task UploadDroppedPathsAsync(BucketProfile bucket, IReadOnlyList<string> paths)
    {
        var credential = LoadCredential(bucket.Id);
        if (credential is null)
        {
            SetStatus(bucket.Id, "未找到 AccessKey 配置");
            return;
        }

        try
        {
            var prefix = _currentPrefixes.GetValueOrDefault(bucket.Id, bucket.Prefix);
            SetStatus(bucket.Id, "正在准备拖拽上传…");
            var progress = new Progress<(int Current, int Total, string Name)>(value =>
                SetStatus(bucket.Id, $"正在上传 {value.Current}/{value.Total}：{value.Name}"));
            var count = await _ossObjectService.UploadPathsAsync(
                bucket,
                credential,
                prefix,
                paths,
                progress);
            var message = count == 0 ? "未找到可上传的文件" : $"上传完成，共 {count} 个文件";
            SetStatus(bucket.Id, message);
            this.ShowToast(message);
            await RefreshBucketAsync(bucket);
        }
        catch (Exception exception)
        {
            ShowOperationError(bucket.Id, "上传失败", exception);
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
            if (_hoveredObjectKeys.TryGetValue(assignment.BucketId, out var previousKey) &&
                previousKey != assignment.Key)
            {
                SetObjectCellsBackground(
                    assignment.BucketId,
                    previousKey,
                    GetObjectRowBackground(assignment.BucketId, previousKey, includeHover: false));
            }

            _hoveredObjectKeys[assignment.BucketId] = assignment.Key;
            SetObjectCellsBackground(
                assignment.BucketId,
                assignment.Key,
                GetObjectRowBackground(assignment.BucketId, assignment.Key));
        }
        else if (_hoveredObjectKeys.GetValueOrDefault(assignment.BucketId) == assignment.Key)
        {
            _hoveredObjectKeys.Remove(assignment.BucketId);
            SetObjectCellsBackground(
                assignment.BucketId,
                assignment.Key,
                GetObjectRowBackground(assignment.BucketId, assignment.Key));
        }
    }

    private Color GetObjectRowBackground(string bucketId, string key, bool includeHover = true)
    {
        if (_checkedObjectKeys.TryGetValue(bucketId, out var selectedKeys) && selectedKeys.Contains(key))
        {
            return ObjectRowSelected;
        }

        return includeHover && _hoveredObjectKeys.GetValueOrDefault(bucketId) == key
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
        if (_hoveredObjectKeys.Remove(bucketId, out var key))
        {
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
        => _checkedObjectKeys.TryGetValue(bucketId, out var keys) && keys.Contains(entry.Key);

    private void SetObjectChecked(string bucketId, ObjectEntry entry, bool isChecked)
    {
        if (!_checkedObjectKeys.TryGetValue(bucketId, out var keys))
        {
            keys = [];
            _checkedObjectKeys[bucketId] = keys;
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
            if (_selectedEntries.GetValueOrDefault(bucket.Id)?.Key == entry.Key)
            {
                _selectedEntries.Remove(bucket.Id);
            }
        }
        else
        {
            _selectedEntries[bucket.Id] = entry;
        }

        var selectedCount = _checkedObjectKeys.GetValueOrDefault(bucket.Id)?.Count ?? 0;
        SetStatus(bucket.Id, isChecked
            ? $"已取消选择：{entry.Name} · 当前已选 {selectedCount} 个"
            : $"已选中：{entry.Name} · 当前已选 {selectedCount} 个");
    }

    private void HandleObjectRowClick(BucketProfile bucket, ObjectEntry entry)
    {
        var now = Environment.TickCount64;
        var isDoubleClick =
            _lastObjectClicks.TryGetValue(bucket.Id, out var previousClick) &&
            string.Equals(previousClick.Key, entry.Key, StringComparison.Ordinal) &&
            now - previousClick.Timestamp is >= 0 and <= ObjectDoubleClickIntervalMilliseconds;

        if (isDoubleClick)
        {
            _lastObjectClicks.Remove(bucket.Id);
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

        _lastObjectClicks[bucket.Id] = (entry.Key, now);
        ToggleObjectSelection(bucket, entry);
    }

    private void HandleObjectViewMouseDown(GridView objectView, BucketProfile bucket, MouseEventArgs args)
    {
        if (args.Button != MouseButton.Left ||
            !objectView.TryGetCellIndexAt(args, out var rowIndex, out var columnIndex, out var isHeader) ||
            isHeader ||
            columnIndex >= 4 ||
            objectView.ItemsSource.GetItem(rowIndex) is not ObjectEntry entry)
        {
            return;
        }

        if (columnIndex == 0)
        {
            _lastObjectClicks.Remove(bucket.Id);
            ToggleObjectSelection(bucket, entry);
        }
        else
        {
            HandleObjectRowClick(bucket, entry);
        }
    }

    private void ToggleAllObjects(BucketProfile bucket)
    {
        var keys = _checkedObjectKeys.GetValueOrDefault(bucket.Id) ?? [];
        SetAllObjects(bucket, !(bucket.Objects.Count > 0 && keys.Count == bucket.Objects.Count));
    }

    private void SetAllObjects(BucketProfile bucket, bool isChecked)
    {
        var keys = isChecked
            ? bucket.Objects.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal)
            : [];

        _checkedObjectKeys[bucket.Id] = keys;
        RefreshObjectSelectionVisuals(bucket.Id);
        UpdateHeaderCheckBox(bucket.Id);
        SetStatus(bucket.Id, $"{bucket.Objects.Count} 个对象 · 已勾选 {keys.Count} 个");
    }

    private void ClearCheckedObjects(string bucketId)
    {
        _checkedObjectKeys.Remove(bucketId);
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

            var isChecked = _checkedObjectKeys.TryGetValue(bucketId, out var keys) && keys.Contains(pair.Key.Key);
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

        var selectedCount = _checkedObjectKeys.GetValueOrDefault(bucketId)?.Count ?? 0;
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
            !_checkedObjectKeys.TryGetValue(bucketId, out var keys) ||
            keys.Count == 0)
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

        public ObjectActionCell(Action<ObjectEntry> download, Action<ObjectEntry> delete)
        {
            _download = download;
            _delete = delete;
            Orientation = Orientation.Horizontal;
            Spacing = 6;
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Center;

            AddRange(
                CreateButton("下载", "下载此文件或目录", Blue, () => Invoke(_download)),
                CreateButton("删除", "删除此文件或目录", Coral, () => Invoke(_delete)));
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
