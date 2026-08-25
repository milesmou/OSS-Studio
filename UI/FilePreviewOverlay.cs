using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace OSSStudio.UI;

internal enum FilePreviewKind
{
    Text,
    Image,
    Unsupported
}

internal sealed class FilePreviewOverlay : ContentControl
{
    private const double MinimumImageScale = 0.05;
    private const double ImageZoomStep = 1.1;

    private readonly ContentControl _body = new();
    private readonly ContentControl _footer = new();
    private readonly TextBlock _status = new();
    private readonly Func<CancellationToken, Task<string>> _loadText;
    private readonly Func<CancellationToken, Task<byte[]>> _loadImage;
    private readonly Func<string, CancellationToken, Task> _saveText;
    private readonly Action _download;
    private readonly Action _getAddress;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly TaskCompletionSource _completion = new();
    private readonly Color _surface;
    private readonly Color _panelSurface;
    private readonly Color _borderColor;
    private readonly Color _mutedText;
    private readonly Color _teal;
    private readonly Color _blue;
    private readonly string _fileName;
    private OssMainWindow? _owner;
    private MultiLineTextBox? _editor;
    private Image? _previewImage;
    private ImageSource? _previewImageSource;
    private ScrollViewer? _imageScrollViewer;
    private double _imageScale = 1;
    private double _maximumImageScale = 1;
    private bool _imageScaleInitialized;
    private string _originalText = string.Empty;

    public FilePreviewOverlay(
        string bucketName,
        string fileName,
        string objectKey,
        FilePreviewKind kind,
        Func<CancellationToken, Task<string>> loadText,
        Func<CancellationToken, Task<byte[]>> loadImage,
        Func<string, CancellationToken, Task> saveText,
        Action download,
        Action getAddress)
    {
        var palette = AppThemePalette.Current;
        _surface = palette.Surface;
        _panelSurface = palette.PanelSurface;
        _borderColor = palette.Border;
        _mutedText = palette.MutedText;
        _teal = palette.Teal;
        _blue = palette.Blue;
        _fileName = fileName;
        _loadText = loadText;
        _loadImage = loadImage;
        _saveText = saveText;
        _download = download;
        _getAddress = getAddress;
        _status.Foreground = _mutedText;
        _status.FontSize = 12;

        Background = Color.Black.WithAlpha(105);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;
        var fullPath = $"oss://{bucketName}/{objectKey}";
        var pathText = new TextBlock().Text(fullPath).FontSize(11).Foreground(_mutedText);
        var header = new Border()
            .Padding(18, 12)
            .Background(palette.HeaderSurface)
            .BorderBrush(_borderColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .ToolTip(fullPath)
            .Child(new StackPanel()
                .Vertical()
                .Spacing(3)
                .Children(
                    new TextBlock().Text($"预览：{fileName}").FontSize(16).Bold(),
                    pathText));

        Content = new Border()
            .Width(900)
            .Height(660)
            .CornerRadius(10)
            .Background(_surface)
            .BorderBrush(_borderColor)
            .BorderThickness(1)
            .HorizontalAlignment(HorizontalAlignment.Center)
            .VerticalAlignment(VerticalAlignment.Center)
            .Child(new DockPanel()
                .LastChildFill()
                .Children(header.DockTop(), _footer.DockBottom(), _body));

        SetCloseFooter();
        _body.Content = BuildMessage("正在加载预览…");
        InitialKind = kind;
    }

    private FilePreviewKind InitialKind { get; }

    public Task ShowAsync(OssMainWindow owner)
    {
        _owner = owner;
        owner.ShowModal(this);
        _ = InitializeAsync();
        return _completion.Task;
    }

    private async Task InitializeAsync()
    {
        switch (InitialKind)
        {
            case FilePreviewKind.Text:
                await ShowTextAsync();
                break;
            case FilePreviewKind.Image:
                await ShowImageAsync();
                break;
            default:
                ShowUnsupported();
                break;
        }
    }

    private async Task ShowTextAsync()
    {
        _body.Content = BuildMessage("正在加载文本内容…");
        try
        {
            _originalText = await _loadText(_cancellation.Token);
            _editor = new MultiLineTextBox()
                .Text(_originalText)
                .FontSize(13)
                .StretchHorizontal()
                .StretchVertical();
            _body.Content = new Border()
                .Margin(16, 12)
                .BorderBrush(_borderColor)
                .BorderThickness(1)
                .Child(_editor);
            SetTextFooter();
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowLoadError($"无法作为文本文件打开：{exception.Message}");
        }
    }

    private async Task ShowImageAsync()
    {
        _body.Content = BuildMessage("正在加载图片…");
        try
        {
            var bytes = await _loadImage(_cancellation.Token);
            var source = ImageSource.FromBytes(bytes);
            _previewImageSource = source;
            _previewImage = new Image
            {
                Source = source,
                StretchMode = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _imageScrollViewer = new ScrollViewer
            {
                Content = _previewImage,
                VerticalScroll = ScrollMode.Auto,
                HorizontalScroll = ScrollMode.Auto,
                AutoHideScrollBars = true
            };
            _imageScrollViewer.SizeChanged += _ => UpdateImageScaleForViewport();
            _imageScrollViewer.MouseWheel += ZoomImage;
            _body.Content = new Border()
                .Margin(16, 12)
                .Background(_panelSurface)
                .BorderBrush(_borderColor)
                .BorderThickness(1)
                .Padding(12)
                .Child(_imageScrollViewer);
            UpdateImageStatus();
            SetCloseFooter();
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            ShowLoadError($"图片预览失败：{exception.Message}");
        }
    }

    private void UpdateImageScaleForViewport()
    {
        if (_previewImageSource is not { PixelWidth: > 0, PixelHeight: > 0 } source ||
            _imageScrollViewer is not { ViewportWidth: > 0, ViewportHeight: > 0 } scrollViewer)
        {
            return;
        }

        var fitScale = Math.Min(
            scrollViewer.ViewportWidth / source.PixelWidth,
            scrollViewer.ViewportHeight / source.PixelHeight);
        _maximumImageScale = fitScale > 1 ? fitScale : 1;
        if (!_imageScaleInitialized)
        {
            _imageScale = Math.Min(1, fitScale);
        }
        else if (_imageScale > _maximumImageScale)
        {
            _imageScale = _maximumImageScale;
        }

        _imageScaleInitialized = true;
        ApplyImageScale();
    }

    private void ZoomImage(MouseWheelEventArgs eventArgs)
    {
        if (_previewImageSource is not { PixelWidth: > 0, PixelHeight: > 0 } ||
            _maximumImageScale <= 1 || eventArgs.Delta.Y == 0)
        {
            return;
        }

        _imageScaleInitialized = true;
        _imageScale = Math.Clamp(
            _imageScale * Math.Pow(ImageZoomStep, eventArgs.Delta.Y),
            MinimumImageScale,
            _maximumImageScale);
        ApplyImageScale();
        eventArgs.Handled = true;
    }

    private void ApplyImageScale()
    {
        if (_previewImage is null || _previewImageSource is not { PixelWidth: > 0, PixelHeight: > 0 } source)
        {
            return;
        }

        _previewImage.Width = source.PixelWidth * _imageScale;
        _previewImage.Height = source.PixelHeight * _imageScale;
        UpdateImageStatus();
    }

    private void UpdateImageStatus()
    {
        _status.Text = _previewImageSource is { PixelWidth: > 0, PixelHeight: > 0 } source
            ? $"{source.PixelWidth} × {source.PixelHeight}  ·  {_imageScale:P0}" +
              (_maximumImageScale > 1 ? "  ·  滚轮缩放" : string.Empty)
            : string.Empty;
    }

    private void ShowUnsupported()
    {
        _body.Content = new StackPanel()
            .Vertical()
            .Spacing(16)
            .HorizontalAlignment(HorizontalAlignment.Center)
            .VerticalAlignment(VerticalAlignment.Center)
            .Children(
                new TextBlock().Text("该文件类型无法预览。")
                    .FontSize(15)
                    .Foreground(_mutedText),
                new Button()
                    .Content("尝试作为文本文件打开", accessKey: false)
                    .Padding(18, 8)
                    .OnClick(() => _ = ShowTextAsync())
                    .WithFeedback(
                        "按 UTF-8 文本尝试打开此文件",
                        _teal.WithAlpha(30),
                        _teal.WithAlpha(52),
                        _teal.WithAlpha(80)));
        SetCloseFooter();
    }

    private void ShowLoadError(string message)
    {
        _body.Content = BuildMessage(message);
        SetCloseFooter();
    }

    private FrameworkElement BuildMessage(string message)
        => new TextBlock { TextWrapping = TextWrapping.Wrap }
            .Text(message)
            .Foreground(_mutedText)
            .HorizontalAlignment(HorizontalAlignment.Center)
            .VerticalAlignment(VerticalAlignment.Center);

    private void SetTextFooter()
    {
        _status.Text = "保存后将以 UTF-8 编码覆盖 OSS 上的原文件";
        _footer.Content = BuildFooter(
            new Button()
                .Content("关闭", accessKey: false)
                .StyleName(BuiltInStyles.FlatButton)
                .Padding(16, 7)
                .OnClick(Close)
                .WithFeedback("关闭预览", Color.White.WithAlpha(0), _borderColor.WithAlpha(75), _borderColor),
            new Button()
                .Content("保存", accessKey: false)
                .Padding(18, 7)
                .Background(_teal.WithAlpha(38))
                .BorderBrush(_teal.WithAlpha(110))
                .Foreground(_teal)
                .OnClick(() => _ = SaveAsync())
                .WithFeedback("保存并覆盖 OSS 文件", _teal.WithAlpha(38), _teal.WithAlpha(58), _teal.WithAlpha(88)));
    }

    private void SetCloseFooter()
    {
        _footer.Content = BuildFooter(
            new Button()
                .Content("关闭", accessKey: false)
                .StyleName(BuiltInStyles.FlatButton)
                .Padding(16, 7)
                .OnClick(Close)
                .WithFeedback("关闭预览", Color.White.WithAlpha(0), _borderColor.WithAlpha(75), _borderColor));
    }

    private FrameworkElement BuildFooter(params FrameworkElement[] buttons)
        => new Border()
            .Padding(18, 11)
            .Background(_panelSurface)
            .BorderBrush(_borderColor)
            .BorderThickness(new Thickness(0, 1, 0, 0))
            .Child(new Grid()
                .Columns("Auto,*,Auto")
                .AutoIndexing()
                .Spacing(12)
                .Children(
                    new StackPanel()
                        .Horizontal()
                        .Spacing(8)
                        .Children(
                            FooterActionButton("下载", "下载当前文件", _download, _blue),
                            FooterActionButton("获取地址", "复制不带签名参数的对象地址", _getAddress, _teal)),
                    _status.CenterVertical(),
                    new StackPanel().Horizontal().Spacing(8).Children(buttons)));

    private Button FooterActionButton(string text, string toolTip, Action action, Color color)
        => new Button()
            .Content(text, accessKey: false)
            .Padding(13, 7)
            .CornerRadius(5)
            .Background(color.WithAlpha(36))
            .BorderBrush(color.WithAlpha(115))
            .Foreground(color)
            .OnClick(action)
            .WithFeedback(
                toolTip,
                color.WithAlpha(36),
                color.WithAlpha(58),
                color.WithAlpha(88));

    private async Task SaveAsync()
    {
        if (_editor is null || string.Equals(_editor.Text, _originalText, StringComparison.Ordinal))
        {
            _status.Text = "文件内容未修改";
            return;
        }
        _status.Text = $"正在保存 {_fileName}…";
        try
        {
            await _saveText(_editor.Text, _cancellation.Token);
            _originalText = _editor.Text;
            _status.Text = "已保存到 OSS";
        }
        catch (Exception exception)
        {
            _status.Text = $"保存失败：{exception.Message}";
        }
    }

    private void Close()
    {
        _cancellation.Cancel();
        if (_owner is { } owner)
        {
            owner.HideModal(this);
        }
        _completion.TrySetResult();
    }
}
