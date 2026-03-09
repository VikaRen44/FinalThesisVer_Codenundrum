using UnityEngine;

public enum SortKey
{
    LargestToSmallest,
    SmallestToLargest,
    OldestToLatest,
    LatestToOldest
}

[CreateAssetMenu(menuName = "HauntedGallery/Portrait Set")]
public class PortraitSet : ScriptableObject
{
    public SortKey sortKey = SortKey.SmallestToLargest;
    public PortraitData[] portraits;
}
