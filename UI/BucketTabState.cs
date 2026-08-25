using OSSStudio.Models;

namespace OSSStudio.UI;

internal sealed class BucketTabState(string rootPrefix)
{
    public string CurrentPrefix { get; set; } = rootPrefix;

    public List<string> NavigationHistory { get; } = [rootPrefix];

    public int NavigationHistoryIndex { get; set; }

    public ObjectEntry? SelectedEntry { get; set; }

    public HashSet<string> CheckedObjectKeys { get; set; } = new(StringComparer.Ordinal);

    public string? HoveredObjectKey { get; set; }

    public int RefreshVersion { get; set; }

    public CancellationTokenSource? TransferCancellation { get; set; }

    public bool IsTransferRunning => TransferCancellation is not null;

    public void ClearInteractionState()
    {
        SelectedEntry = null;
        CheckedObjectKeys.Clear();
        HoveredObjectKey = null;
    }
}
