namespace OSSStudio.UI;

internal sealed class TransferTaskInfo(
    string bucketId,
    string bucketName,
    string operation,
    string title,
    CancellationTokenSource cancellation)
{
    public Guid Id { get; } = Guid.NewGuid();

    public string BucketId { get; } = bucketId;

    public string BucketName { get; } = bucketName;

    public string Operation { get; } = operation;

    public string Title { get; } = title;

    public CancellationTokenSource Cancellation { get; } = cancellation;

    public int Current { get; set; }

    public int Total { get; set; }

    public string Detail { get; set; } = "正在准备…";

    public string State { get; set; } = "进行中";

    public bool IsRunning { get; set; } = true;

    public Func<Task>? RetryAsync { get; set; }

    public bool CanRetry { get; set; } = true;

    public double ProgressPercent => State == "已完成"
        ? 100
        : Total <= 0 ? 0 : Math.Clamp(Current * 100d / Total, 0, 100);

    public string ProgressText => Total <= 0 ? Detail : $"{Current}/{Total} · {Detail}";
}
