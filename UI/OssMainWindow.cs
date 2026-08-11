using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using OSSClient.Models;
using OSSClient.Services;

namespace OSSClient.UI;

public sealed class OssMainWindow : Window
{
    private static readonly Color Surface = Color.FromHex("#F4F5F3");
    private static readonly Color SidebarSurface = Color.FromHex("#EEF2F1");
    private static readonly Color HeaderSurface = Color.FromHex("#E7ECEC");
    private static readonly Color BorderColor = Color.FromHex("#CFD9D9");
    private static readonly Color MutedText = Color.FromHex("#687982");
    private static readonly Color Teal = Color.FromHex("#438F86");
    private static readonly Color Amber = Color.FromHex("#C5903D");
    private static readonly Color Blue = Color.FromHex("#6384AD");
    private static readonly Color Purple = Color.FromHex("#7D6A9D");
    private static readonly Color Coral = Color.FromHex("#A86F67");

    private readonly WorkspaceState _state;
    private readonly WorkspaceStateStore _stateStore;
    private readonly TabControl _tabs = new();
    private readonly TreeView _bucketTree = new();
    private readonly TextBox _searchBox = new();
    private readonly Dictionary<TabItem, string> _bucketIdsByTab = [];
    private readonly Dictionary<string, TreeViewNode> _bucketNodesById = [];
    private readonly Dictionary<string, GridView> _objectViews = [];
    private readonly Dictionary<string, ObservableValue<string>> _statusTexts = [];

    private AccountProfile _activeAccount;
    private string _searchText = string.Empty;

    public OssMainWindow(WorkspaceState state, WorkspaceStateStore stateStore)
    {
        _state = state;
        _stateStore = stateStore;
        _activeAccount = FindAccount(state.ActiveAccountId) ?? state.Accounts[0];

        Title = "OSS Studio";
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

        _bucketTree
            .ItemsSource(CreateBucketNodes(_activeAccount))
            .OnSelectionChanged(OnBucketSelected);

        _tabs.OnSelectionChanged(OnTabSelectionChanged);

        Content = new DockPanel()
            .LastChildFill()
            .Children(
                BuildTopBar().DockTop(),
                new Grid()
                    .Columns("250,*")
                    .AutoIndexing()
                    .Children(BuildSidebar(), BuildWorkspace()));

        RestoreTabs();
        Closed += SaveWorkspace;
    }

    private FrameworkElement BuildTopBar()
    {
        var brand = new StackPanel()
            .Horizontal()
            .Spacing(10)
            .CenterVertical()
            .Children(
                new Border()
                    .Width(30)
                    .Height(30)
                    .CornerRadius(8)
                    .Background(Teal)
                    .Child(new TextBlock().Text("☁").Foreground(Color.White).Center()),
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
        var accountIndex = Math.Max(0, _state.Accounts.FindIndex(account => account.Id == _activeAccount.Id));
        var accountPicker = new ComboBox()
            .Items(_state.Accounts, account => account.DisplayName, account => account.Id)
            .SelectedIndex(accountIndex)
            .OnSelectionChanged(item =>
            {
                if (item is AccountProfile account)
                {
                    SwitchAccount(account);
                }
            });

        var accountCard = new Border()
            .Padding(10)
            .CornerRadius(8)
            .Background(Color.FromHex("#E8EBF0"))
            .BorderBrush(Color.FromHex("#D0D7DC"))
            .BorderThickness(1)
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        accountPicker.StretchHorizontal(),
                        new TextBlock().Text("AccessKey 由系统安全存储托管").FontSize(11).Foreground(MutedText)));

        var treeArea = new DockPanel()
            .LastChildFill()
            .Children(
                new Grid()
                    .Columns("*,Auto")
                    .AutoIndexing()
                    .Margin(4, 14, 4, 8)
                    .Children(
                        new TextBlock().Text($"资源桶（{_activeAccount.Buckets.Count}）").Foreground(MutedText).CenterVertical(),
                        FlatButton("刷新", () => SetStatus("Bucket 列表已刷新")))
                    .DockTop(),
                _bucketTree.StretchVertical());

        var footer = new StackPanel()
            .Vertical()
            .Spacing(3)
            .Children(
                SidebarButton("☆  收藏夹", () => SetStatus("收藏夹中暂无对象")),
                SidebarButton("◷  最近访问", () => SetStatus("已显示最近访问记录")),
                SidebarButton("＋  添加账号", () => SetStatus("账号编辑器将在接入凭据存储时启用")));

        return new Border()
            .Background(SidebarSurface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 0, 1, 0))
            .Padding(12)
            .Child(
                new DockPanel()
                    .LastChildFill()
                    .Children(
                        accountCard.DockTop(),
                        footer.DockBottom(),
                        treeArea));
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
            var first = _activeAccount.Buckets.FirstOrDefault();
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
        if (item is TreeViewNode { Tag: BucketProfile bucket })
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
        var closeButton = new Button()
            .Content("×", accessKey: false)
            .StyleName(BuiltInStyles.FlatButton)
            .Width(24)
            .Height(24)
            .Padding(0)
            .Foreground(MutedText)
            .OnClick(() => CloseBucket(bucket.Id));

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
    }

    private FrameworkElement BuildBucketPage(BucketProfile bucket)
    {
        var status = new ObservableValue<string>($"{bucket.Objects.Count} 个对象 · 已选择 0 个");
        _statusTexts[bucket.Id] = status;

        var objectView = new GridView()
            .RowHeight(46)
            .HeaderHeight(38)
            .ZebraStriping(false)
            .ShowGridLines(false)
            .ItemsSource(bucket.Objects)
            .Columns(
                new GridViewColumn<ObjectEntry>().Header("名称").Width(390).Text(entry => entry.DisplayName),
                new GridViewColumn<ObjectEntry>().Header("大小").Width(120).Text(entry => entry.Size),
                new GridViewColumn<ObjectEntry>().Header("存储类型").Width(150).Text(entry => entry.StorageClass),
                new GridViewColumn<ObjectEntry>().Header("最后修改时间").Width(190).Text(entry => entry.DisplayModified))
            .OnSelectionChanged(item =>
            {
                status.Value = item is ObjectEntry entry
                    ? $"{bucket.Objects.Count} 个对象 · 已选择：{entry.Name}"
                    : $"{bucket.Objects.Count} 个对象 · 已选择 0 个";
            });

        _objectViews[bucket.Id] = objectView;

        var toolbar = new Border()
            .Padding(14, 10)
            .Background(Surface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .Child(
                new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        AccentButton("上传", Teal, () => SetStatus(bucket.Id, "请选择要上传的文件")),
                        AccentButton("新建目录", Amber, () => SetStatus(bucket.Id, "新建目录操作已准备")),
                        AccentButton("下载", Blue, () => SetStatus(bucket.Id, "请选择要下载的对象")),
                        AccentButton("生成 URL", Purple, () => SetStatus(bucket.Id, "请选择对象后生成签名 URL")),
                        AccentButton("删除", Coral, () => SetStatus(bucket.Id, "删除操作需要先选择对象"))));

        var breadcrumb = new Border()
            .Padding(14, 10)
            .Background(Surface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .Child(
                new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .CenterVertical()
                    .Children(
                        FlatButton("←", () => SetStatus(bucket.Id, "已经是最早的浏览记录")),
                        FlatButton("↑", () => SetStatus(bucket.Id, "已经位于 Bucket 根目录")),
                        new TextBlock().Text(bucket.Name).Bold().Foreground(Teal).CenterVertical(),
                        new TextBlock().Text("›").Foreground(MutedText).CenterVertical(),
                        new TextBlock().Text("根目录").CenterVertical()));

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
                toolbar.DockTop(),
                breadcrumb.DockTop(),
                statusBar.DockBottom(),
                objectView);
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

    private void SwitchAccount(AccountProfile account)
    {
        _activeAccount = account;
        _state.ActiveAccountId = account.Id;
        _bucketTree.ItemsSource(CreateBucketNodes(account));
        SetStatus($"已切换到 {account.DisplayName}");
        SaveWorkspace();
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

    private IReadOnlyList<TreeViewNode> CreateBucketNodes(AccountProfile account)
    {
        _bucketNodesById.Clear();
        return account.Buckets.Select(bucket =>
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
            .OnClick(action);
    }

    private Button SidebarButton(string text, Action action)
    {
        return new Button()
            .Content(text, accessKey: false)
            .StyleName(BuiltInStyles.FlatButton)
            .Padding(9, 7)
            .StretchHorizontal()
            .OnClick(action);
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
            .OnClick(action);
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

    private AccountProfile? FindAccount(string accountId)
        => _state.Accounts.FirstOrDefault(account => account.Id == accountId);

    private BucketProfile? FindBucket(string bucketId)
        => _state.Accounts.SelectMany(account => account.Buckets).FirstOrDefault(bucket => bucket.Id == bucketId);

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
}
