using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace OSSStudio.UI;

internal sealed class TransferTasksOverlay : ContentControl
{
    private readonly Color Surface;
    private readonly Color PanelSurface;
    private readonly Color BorderColor;
    private readonly Color MutedText;
    private readonly Color Teal;
    private readonly Color Coral;
    private readonly Color Blue;

    private readonly StackPanel _taskList = new StackPanel().Vertical().Spacing(10);
    private readonly Dictionary<Guid, TaskCard> _taskCards = [];
    private readonly Action<Guid> _cancelTask;
    private readonly Action<Guid> _retryTask;
    private readonly Action _clearFinished;
    private readonly Action _closed;
    private OssMainWindow? _owner;

    public TransferTasksOverlay(Action<Guid> cancelTask, Action<Guid> retryTask, Action clearFinished, Action closed)
    {
        var palette = AppThemePalette.Current;
        Surface = palette.Surface;
        PanelSurface = palette.PanelSurface;
        BorderColor = palette.Border;
        MutedText = palette.MutedText;
        Teal = palette.Teal;
        Coral = palette.Coral;
        Blue = palette.Blue;
        _cancelTask = cancelTask;
        _retryTask = retryTask;
        _clearFinished = clearFinished;
        _closed = closed;

        Background = Color.Black.WithAlpha(105);
        HorizontalAlignment = HorizontalAlignment.Stretch;
        VerticalAlignment = VerticalAlignment.Stretch;

        var scrollViewer = new ScrollViewer
        {
            Content = _taskList,
            VerticalScroll = ScrollMode.Auto,
            HorizontalScroll = ScrollMode.Disabled
        };

        Content = new Border()
            .Width(720)
            .Height(520)
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
                        new Border().Padding(18, 14).Child(scrollViewer)));
    }

    public void Show(OssMainWindow owner, IReadOnlyList<TransferTaskInfo> tasks)
    {
        _owner = owner;
        Refresh(tasks);
        owner.ShowModal(this);
    }

    public void Refresh(IReadOnlyList<TransferTaskInfo> tasks)
    {
        while (_taskList.Children.Count > 0)
        {
            _taskList.RemoveAt(0);
        }
        _taskCards.Clear();
        if (tasks.Count == 0)
        {
            _taskList.Add(
                new Border()
                    .Padding(18, 36)
                    .CornerRadius(7)
                    .Background(PanelSurface)
                    .BorderBrush(BorderColor)
                    .BorderThickness(1)
                    .Child(
                        new StackPanel()
                            .Vertical()
                            .Spacing(7)
                            .Center()
                            .Children(
                                new TextBlock().Text("当前没有传输任务").FontSize(15).Bold(),
                                new TextBlock().Text("上传和下载任务会显示在这里").Foreground(MutedText))));
            return;
        }

        foreach (var task in tasks)
        {
            var card = BuildTaskCard(task);
            _taskCards[task.Id] = card;
            _taskList.Add(card.View);
        }
    }

    public void UpdateTask(TransferTaskInfo task)
    {
        if (_taskCards.TryGetValue(task.Id, out var card))
        {
            UpdateTaskCard(card, task);
        }
    }

    private FrameworkElement BuildHeader()
    {
        var closeButton = new Button()
            .Content("×", accessKey: false)
            .StyleName(BuiltInStyles.FlatButton)
            .Width(30)
            .Height(30)
            .Padding(0)
            .Foreground(MutedText)
            .OnClick(Close)
            .WithFeedback("关闭传输任务", Color.White.WithAlpha(0), BorderColor.WithAlpha(70), BorderColor);

        return new Border()
            .Padding(18, 13)
            .Background(PanelSurface)
            .BorderBrush(BorderColor)
            .BorderThickness(new Thickness(0, 0, 0, 1))
            .Child(
                new Grid()
                    .Columns("*,Auto")
                    .AutoIndexing()
                    .Children(
                        new StackPanel()
                            .Vertical()
                            .Spacing(3)
                            .Children(
                                new TextBlock().Text("传输任务").FontSize(17).Bold(),
                                new TextBlock().Text("查看上传和下载进度，可单独取消正在执行的任务").FontSize(12).Foreground(MutedText)),
                        closeButton));
    }

    private FrameworkElement BuildFooter()
    {
        return new Border()
            .Padding(18, 12)
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
                            .Content("清除已完成", accessKey: false)
                            .StyleName(BuiltInStyles.FlatButton)
                            .Padding(14, 7)
                            .OnClick(_clearFinished)
                            .WithFeedback("清除已完成、已取消和失败的任务", Color.White.WithAlpha(0), BorderColor.WithAlpha(70), BorderColor),
                        new Button()
                            .Content("关闭", accessKey: false)
                            .Padding(16, 7)
                            .CornerRadius(6)
                            .Background(Teal.WithAlpha(38))
                            .BorderBrush(Teal.WithAlpha(110))
                            .Foreground(Teal)
                            .OnClick(Close)
                            .WithFeedback("关闭传输任务", Teal.WithAlpha(38), Teal.WithAlpha(58), Teal.WithAlpha(88))));
    }

    private TaskCard BuildTaskCard(TransferTaskInfo task)
    {
        var operation = new TextBlock().Text(task.Operation).Bold();
        var title = new TextBlock().Text(task.Title).Bold();
        var bucketName = new TextBlock().Text($"· {task.BucketName}").Foreground(MutedText);
        var detail = new TextBlock()
            .FontSize(12)
            .Foreground(MutedText)
            .TextTrimming(TextTrimming.CharacterEllipsis)
            .Margin(0, 7, 18, 7);
        var progress = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 6
        };
        progress.Margin = new Thickness(0, 0, 18, 0);
        var actionHost = new ContentControl().CenterVertical();
        Grid.SetColumn(actionHost, 1);
        Grid.SetRowSpan(actionHost, 3);
        Grid.SetRow(detail, 1);
        Grid.SetRow(progress, 2);

        var heading = new StackPanel()
            .Horizontal()
            .Spacing(8)
            .Children(operation, title, bucketName);
        var view = new Border()
            .Padding(14, 12)
            .CornerRadius(7)
            .Background(PanelSurface)
            .BorderBrush(BorderColor)
            .BorderThickness(1)
            .Child(
                new Grid()
                    .Columns("*,Auto")
                    .Rows("Auto,Auto,Auto")
                    .Children(
                        heading,
                        actionHost,
                        detail,
                        progress));
        var card = new TaskCard(view, operation, detail, progress, actionHost);
        UpdateTaskCard(card, task);
        return card;
    }

    private void UpdateTaskCard(TaskCard card, TransferTaskInfo task)
    {
        var statusColor = GetStatusColor(task);
        card.Operation.Foreground = statusColor;
        card.Detail.Text = task.ProgressText;
        card.Progress.Value = task.ProgressPercent;
        card.Progress.IsIndeterminate = task.IsRunning && task.Total <= 0;
        card.Progress.Foreground = statusColor;

        var isCancelling = task.IsCancellationPending;
        var actionKey = task.IsRunning
            ? isCancelling ? "cancelling" : "cancel"
            : task.State == "失败" && task.RetryAsync is not null && task.CanRetry
                ? "retry"
                : task.State;
        if (card.ActionKey == "cancel" && actionKey == "cancelling" &&
            card.ActionHost.Content is Button cancelButton)
        {
            card.ActionKey = actionKey;
            cancelButton.Content("取消中…", accessKey: false);
            cancelButton.IsEnabled = false;
            return;
        }

        if (!string.Equals(card.ActionKey, actionKey, StringComparison.Ordinal))
        {
            card.ActionKey = actionKey;
            card.ActionHost.Content = BuildTaskAction(task, statusColor);
        }
    }

    private Button BuildTaskAction(TransferTaskInfo task, Color statusColor)
    {
        if (task.IsRunning)
        {
            var isCancelling = task.IsCancellationPending;
            var action = new Button()
                .Content(isCancelling ? "取消中…" : "取消", accessKey: false)
                .Padding(12, 6)
                .CornerRadius(5)
                .Background(Coral.WithAlpha(32))
                .BorderBrush(Coral.WithAlpha(100))
                .Foreground(Coral)
                .OnClick(() => _cancelTask(task.Id))
                .WithFeedback("取消此传输任务", Coral.WithAlpha(32), Coral.WithAlpha(52), Coral.WithAlpha(82));
            action.IsEnabled = !isCancelling;
            return action;
        }

        if (task.State == "失败" && task.RetryAsync is not null && task.CanRetry)
        {
            return new Button()
                .Content("重试", accessKey: false)
                .Padding(12, 6)
                .CornerRadius(5)
                .Background(Blue.WithAlpha(32))
                .BorderBrush(Blue.WithAlpha(100))
                .Foreground(Blue)
                .OnClick(() => _retryTask(task.Id))
                .WithFeedback("重新执行此传输任务", Blue.WithAlpha(32), Blue.WithAlpha(52), Blue.WithAlpha(82));
        }

        var completedAction = new Button()
            .Content(task.State, accessKey: false)
            .StyleName(BuiltInStyles.FlatButton)
            .Padding(12, 6)
            .Foreground(statusColor);
        completedAction.IsEnabled = false;
        return completedAction;
    }

    private Color GetStatusColor(TransferTaskInfo task)
        => task.State switch
        {
            "已完成" => Teal,
            "已取消" => MutedText,
            "失败" => Coral,
            _ => Blue
        };

    private void Close()
    {
        if (_owner is { } owner)
        {
            owner.HideModal(this);
            _owner = null;
        }

        _closed();
    }

    private sealed class TaskCard(
        Border view,
        TextBlock operation,
        TextBlock detail,
        ProgressBar progress,
        ContentControl actionHost)
    {
        public Border View { get; } = view;

        public TextBlock Operation { get; } = operation;

        public TextBlock Detail { get; } = detail;

        public ProgressBar Progress { get; } = progress;

        public ContentControl ActionHost { get; } = actionHost;

        public string ActionKey { get; set; } = string.Empty;
    }
}
