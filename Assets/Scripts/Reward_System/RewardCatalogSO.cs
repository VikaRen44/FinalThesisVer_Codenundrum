using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Rewards/Reward Catalog", fileName = "RewardCatalog")]
public class RewardCatalogSO : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public string rewardId;
        public string displayName = "New Achievement";
        public Sprite icon;
    }

    public List<Entry> entries = new List<Entry>();

    public bool TryGet(string rewardId, out Entry entry)
    {
        entry = null;
        if (string.IsNullOrWhiteSpace(rewardId)) return false;

        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            if (e == null) continue;
            if (string.Equals(e.rewardId, rewardId, StringComparison.Ordinal))
            {
                entry = e;
                return true;
            }
        }
        return false;
    }
}