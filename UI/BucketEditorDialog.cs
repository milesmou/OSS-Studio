using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using OSSStudio.Models;

namespace OSSStudio.UI;

public sealed class BucketEditorDialog : ContentControl
{
    private readonly Color Surface;
    private readonly Color HeaderSurface;
    private readonly Color PanelSurface;
    private readonly Color MutedText;
    private readonly Color BorderColor;
    private readonly Color Teal;
    private readonly Color Coral;

    private readonly TextBox _accessKeyIdBox = new();
    private readonly PasswordBox _accessKeySecretBox = new();
    private readonly TextBox _ossPathBox = new();
    private readonly TextBox _noteBox = new();
    private readonly CheckBox _httpsBox = new();
    private readonly CheckBox _requestPayerBox = new();
    private readonly TextBlock _errorText = new();
    private readonly ComboBox _endpointBox = new();
    private readonly TextBox _customEndpointBox = new();
    private readonly ComboBox _regionBox = new();
    private readonly List<EndpointOption> _endpointOptions;
    private readonly List<RegionOption> _regionOptions;
    private readonly Func<string, bool> _nameExists;
    private readonly string _bucketId;
    private readonly string _submitText;
    private readonly string _dialogTitle;
    private readonly TaskCompletionSource<bool> _completion = new();
    private Window? _owner;
    private OssMainWindow? _modalOwner;

    public BucketEditorDialog(BucketProfile? bucket, BucketCredential? credential, Func<string, bool> nameExists)
    {
        var palette = AppThemePalette.Current;
        Surface = palette.Surface;
        HeaderSurface = palette.HeaderSurface;
        PanelSurface = palette.PanelSurface;
        MutedText = palette.MutedText;
        BorderColor = palette.Border;
        Teal = palette.Teal;
        Coral = palette.Coral;
        _nameExists = nameExists;
        _bucketId = bucket?.Id ?? $"bucket-{Guid.NewGuid():N}";
        _submitText = bucket is null ? "添加" : "保存";
        _endpointOptions = CreateEndpointOptions();
        _regionOptions = CreateRegionOptions(bucket);

        _dialogTitle = bucket is null ? "添加资源桶" : "编辑资源桶";
        Background = Color.Black.WithAlpha(105);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var endpointMode = ResolveEndpointMode(bucket);
        var endpointIndex = Math.Max(0, _endpointOptions.FindIndex(item => item.Mode == endpointMode));
        var regionIndex = Math.Max(0, _regionOptions.FindIndex(item => item.Code == bucket?.RegionCode));

        _endpointBox
            .Items(_endpointOptions, item => item.DisplayName, item => item.Mode)
            .SelectedIndex(endpointIndex)
            .OnSelectionChanged(item =>
            {
                if (item is EndpointOption endpoint)
                {
                    UpdateEndpointInput(endpoint.Mode);
                }
            });

        _regionBox
            .Items(_regionOptions, item => item.DisplayName, item => item.Code)
            .MaxDropDownHeight(360)
            .SelectedIndex(regionIndex);

        _accessKeyIdBox.Text = credential?.AccessKeyId ?? string.Empty;
        _accessKeySecretBox.Password = credential?.AccessKeySecret ?? string.Empty;
        _customEndpointBox.Text = endpointMode == "Default" ? string.Empty : bucket?.Endpoint ?? string.Empty;
        _ossPathBox.Text = bucket is null
            ? string.Empty
            : $"{bucket.Name}/{bucket.Prefix}";
        _noteBox.Text = bucket?.Note ?? string.Empty;

        ConfigureCheckBox(_httpsBox, "HTTPS 加密", bucket?.UseHttps ?? true);
        ConfigureCheckBox(_requestPayerBox, "请求者付费模式", bucket?.RequestPayer ?? false);
        UpdateEndpointInput(endpointMode);

        _errorText.Foreground = Coral;
        _errorText.TextWrapping = TextWrapping.Wrap;

        Content = new Border()
            .Width(620)
            .Height(570)
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
                        BuildHeader().DockTop(),
                        BuildFooter().DockBottom(),
                        BuildBody()));
    }

    public bool Accepted { get; private set; }

    public BucketProfile? Result { get; private set; }

    public BucketCredential? Credential { get; private set; }

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

    private FrameworkElement BuildHeader()
    {
        return new Border()
            .Padding(22, 16)
            .Background(HeaderSurface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(3)
                    .Children(
                        new TextBlock().Text(_dialogTitle).FontSize(18).Bold(),
                        new TextBlock().Text("配置 Bucket 访问地址与 AK 凭据").Foreground(MutedText)));
    }

    private FrameworkElement BuildBody()
    {
        var endpointInput = new Grid()
            .Columns("Auto,*,Auto")
            .AutoIndexing()
            .Spacing(12)
            .Children(
                _endpointBox.StretchHorizontal(),
                _customEndpointBox.Placeholder("请输入 Endpoint").StretchHorizontal(),
                _httpsBox.CenterVertical());

        var ossPathInput = new Grid()
            .Columns("Auto,*")
            .AutoIndexing()
            .Children(
                new Border()
                    .Padding(10, 0)
                    .Background(HeaderSurface)
                    .BorderBrush(BorderColor)
                    .BorderThickness(new Thickness(1, 1, 0, 1))
                    .Child(new TextBlock().Text("oss://").Foreground(MutedText).CenterVertical()),
                _ossPathBox.Placeholder("bucket-name/path/").StretchHorizontal());

        var form = new StackPanel()
            .Vertical()
            .Spacing(11)
            .Margin(22, 18)
            .Children(
                FormRow("* Endpoint：", endpointInput),
                FormRow("* AccessKeyId：", _accessKeyIdBox.Placeholder("请输入 AccessKey ID")),
                FormRow("* AccessKeySecret：", _accessKeySecretBox.Placeholder("请输入 AccessKey Secret")),
                FormRow("预设 OSS 路径：", ossPathInput),
                FormRow(string.Empty, _requestPayerBox),
                FormRow("区域：", _regionBox),
                FormRow("备注：", _noteBox.Placeholder("可以为空，最多 30 个字符")),
                FormRow(string.Empty, _errorText));

        return form;
    }

    private FrameworkElement BuildFooter()
    {
        return new Border()
            .Padding(22, 13)
            .Background(PanelSurface)
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
                            .OnClick(() => CloseOverlay(false))
                            .WithFeedback("取消并关闭", Color.White.WithAlpha(0), BorderColor.WithAlpha(75), BorderColor),
                        new Button()
                            .Content(_submitText, accessKey: false)
                            .Padding(18, 7)
                            .CornerRadius(6)
                            .Background(Teal.WithAlpha(38))
                            .BorderBrush(Teal.WithAlpha(110))
                            .Foreground(Teal)
                            .OnClick(Accept)
                            .WithFeedback(
                                _submitText,
                                Teal.WithAlpha(38),
                                Teal.WithAlpha(58),
                                Teal.WithAlpha(88))));
    }

    private FrameworkElement FormRow(string label, FrameworkElement input)
    {
        input.Height = 34;
        return new Grid()
            .Columns("145,*")
            .AutoIndexing()
            .Spacing(10)
            .Children(
                new TextBlock()
                    .Text(label)
                    .Foreground(label.StartsWith('*') ? Coral : MutedText)
                    .HorizontalAlignment(HorizontalAlignment.Right)
                    .CenterVertical(),
                input.StretchHorizontal());
    }

    private static void ConfigureCheckBox(CheckBox checkBox, string text, bool isChecked)
    {
        checkBox.IsChecked = isChecked;
        checkBox.Content(text, accessKey: false);
    }

    private void Accept()
    {
        var endpointOption = _endpointBox.SelectedItem as EndpointOption;
        var region = _regionBox.SelectedItem as RegionOption;
        var accessKeyId = _accessKeyIdBox.Text.Trim();
        var accessKeySecret = _accessKeySecretBox.Password.Trim();
        var note = _noteBox.Text.Trim();

        if (!TryParseOssPath(_ossPathBox.Text, out var bucketName, out var prefix))
        {
            _errorText.Text = "请输入有效的预设 OSS 路径，例如 oss://bucket-name/path/。";
            return;
        }

        if (endpointOption is null || region is null || string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(accessKeySecret))
        {
            _errorText.Text = "请完整填写 Endpoint、AccessKeyId 和 AccessKeySecret。";
            return;
        }

        var endpoint = endpointOption.Mode == "Default"
            ? region.Endpoint
            : NormalizeEndpoint(_customEndpointBox.Text);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            _errorText.Text = endpointOption.Mode == "Cname"
                ? "使用 cname 时，请填写已绑定到 Bucket 的自定义域名。"
                : "使用自定义模式时，请填写 OSS Endpoint。";
            return;
        }

        if (_nameExists(bucketName))
        {
            _errorText.Text = "已存在同名资源桶，请填写其它 OSS 路径。";
            return;
        }

        if (note.Length > 30)
        {
            _errorText.Text = "备注最多可以填写 30 个字符。";
            return;
        }

        Result = new BucketProfile(
            _bucketId,
            bucketName,
            region?.Code ?? string.Empty,
            region?.DisplayName ?? string.Empty,
            [],
            endpoint,
            _httpsBox.IsChecked == true,
            prefix,
            _requestPayerBox.IsChecked == true,
            note,
            true,
            true,
            endpointOption.Mode);
        Credential = new BucketCredential(accessKeyId, accessKeySecret);
        Accepted = true;
        CloseOverlay(true);
    }

    private void CloseOverlay(bool accepted)
    {
        if (_modalOwner is { } modalOwner)
        {
            modalOwner.HideModal(this);
        }
        else if (_owner is { } owner && owner.OverlayLayer.Contains(this))
        {
            owner.OverlayLayer.Remove(this);
        }

        _completion.TrySetResult(accepted);
    }

    private static bool TryParseOssPath(string value, out string bucketName, out string prefix)
    {
        bucketName = string.Empty;
        prefix = string.Empty;
        var path = value.Trim();
        if (path.StartsWith("oss://", StringComparison.OrdinalIgnoreCase))
        {
            path = path[6..];
        }

        var segments = path.Split('/', 2, StringSplitOptions.None);
        bucketName = segments[0].Trim();
        prefix = segments.Length > 1 ? segments[1].Trim().TrimStart('/') : string.Empty;
        if (prefix.Length > 0 && !prefix.EndsWith('/'))
        {
            prefix += "/";
        }

        return bucketName.Length > 0;
    }

    private void UpdateEndpointInput(string mode)
    {
        var showCustomInput = mode != "Default";
        _endpointBox.Width = showCustomInput ? 140 : 280;
        _customEndpointBox.IsVisible(showCustomInput);
        _customEndpointBox.Placeholder(mode == "Cname" ? "请输入已绑定的自定义域名" : "请输入 OSS Endpoint");
    }

    private static string NormalizeEndpoint(string value)
        => value.Trim()
            .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .TrimEnd('/');

    private static string ResolveEndpointMode(BucketProfile? bucket)
    {
        if (bucket is null)
        {
            return "Default";
        }

        if (bucket.EndpointMode is "Custom" or "Cname")
        {
            return bucket.EndpointMode;
        }

        var defaultEndpoint = $"oss-{bucket.RegionCode}.aliyuncs.com";
        return string.Equals(bucket.Endpoint, defaultEndpoint, StringComparison.OrdinalIgnoreCase)
            ? "Default"
            : "Custom";
    }

    private static List<EndpointOption> CreateEndpointOptions()
        =>
        [
            new("默认（公共云）", "Default"),
            new("自定义", "Custom"),
            new("cname", "Cname")
        ];

    private static List<RegionOption> CreateRegionOptions(BucketProfile? bucket)
    {
        var options = new List<RegionOption>
        {
            new("华东 1（杭州）", "cn-hangzhou", "oss-cn-hangzhou.aliyuncs.com"),
            new("华东 2（上海）", "cn-shanghai", "oss-cn-shanghai.aliyuncs.com"),
            new("华东 5（南京-本地地域，停止服务中）", "cn-nanjing", "oss-cn-nanjing.aliyuncs.com"),
            new("华东 6（福州-本地地域，退役中）", "cn-fuzhou", "oss-cn-fuzhou.aliyuncs.com"),
            new("华中 1（武汉-本地地域）", "cn-wuhan-lr", "oss-cn-wuhan-lr.aliyuncs.com"),
            new("华北 1（青岛）", "cn-qingdao", "oss-cn-qingdao.aliyuncs.com"),
            new("华北 2（北京）", "cn-beijing", "oss-cn-beijing.aliyuncs.com"),
            new("华北 3（张家口）", "cn-zhangjiakou", "oss-cn-zhangjiakou.aliyuncs.com"),
            new("华北 5（呼和浩特）", "cn-huhehaote", "oss-cn-huhehaote.aliyuncs.com"),
            new("华北 6（乌兰察布）", "cn-wulanchabu", "oss-cn-wulanchabu.aliyuncs.com"),
            new("华南 1（深圳）", "cn-shenzhen", "oss-cn-shenzhen.aliyuncs.com"),
            new("华南 2（河源）", "cn-heyuan", "oss-cn-heyuan.aliyuncs.com"),
            new("华南 3（广州）", "cn-guangzhou", "oss-cn-guangzhou.aliyuncs.com"),
            new("西南 1（成都）", "cn-chengdu", "oss-cn-chengdu.aliyuncs.com"),
            new("西北 1（中卫）", "cn-zhongwei", "oss-cn-zhongwei.aliyuncs.com"),
            new("中国香港", "cn-hongkong", "oss-cn-hongkong.aliyuncs.com"),
            new("无地域属性（中国内地）", "rg-china-mainland", "oss-rg-china-mainland.aliyuncs.com"),
            new("日本（东京）", "ap-northeast-1", "oss-ap-northeast-1.aliyuncs.com"),
            new("韩国（首尔）", "ap-northeast-2", "oss-ap-northeast-2.aliyuncs.com"),
            new("新加坡", "ap-southeast-1", "oss-ap-southeast-1.aliyuncs.com"),
            new("马来西亚（吉隆坡）", "ap-southeast-3", "oss-ap-southeast-3.aliyuncs.com"),
            new("印度尼西亚（雅加达）", "ap-southeast-5", "oss-ap-southeast-5.aliyuncs.com"),
            new("菲律宾（马尼拉）", "ap-southeast-6", "oss-ap-southeast-6.aliyuncs.com"),
            new("泰国（曼谷）", "ap-southeast-7", "oss-ap-southeast-7.aliyuncs.com"),
            new("马来西亚（柔佛）", "ap-southeast-8", "oss-ap-southeast-8.aliyuncs.com"),
            new("德国（法兰克福）", "eu-central-1", "oss-eu-central-1.aliyuncs.com"),
            new("英国（伦敦）", "eu-west-1", "oss-eu-west-1.aliyuncs.com"),
            new("美国（硅谷）", "us-west-1", "oss-us-west-1.aliyuncs.com"),
            new("美国（弗吉尼亚）", "us-east-1", "oss-us-east-1.aliyuncs.com"),
            new("墨西哥", "na-south-1", "oss-na-south-1.aliyuncs.com"),
            new("法国（巴黎）", "eu-west-2", "oss-eu-west-2.aliyuncs.com"),
            new("阿联酋（迪拜）", "me-east-1", "oss-me-east-1.aliyuncs.com"),
            new("沙特（利雅得-合作伙伴运营）", "me-central-1", "oss-me-central-1.aliyuncs.com")
        };

        if (bucket is { RegionCode.Length: > 0 } && options.All(item => item.Code != bucket.RegionCode))
        {
            options.Add(new RegionOption(
                bucket.RegionName,
                bucket.RegionCode,
                $"oss-{bucket.RegionCode}.aliyuncs.com"));
        }

        return options;
    }

    public static string GetRegionDisplayName(string regionCode)
        => CreateRegionOptions(null).FirstOrDefault(item => item.Code == regionCode)?.DisplayName ?? regionCode;

    private sealed record EndpointOption(string DisplayName, string Mode);

    private sealed record RegionOption(string DisplayName, string Code, string Endpoint);
}
