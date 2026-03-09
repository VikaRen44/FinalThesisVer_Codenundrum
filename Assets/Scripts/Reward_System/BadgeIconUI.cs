using UnityEngine;
using UnityEngine.UI;

public class BadgeIconUI : MonoBehaviour
{
    [Header("Reward")]
    public string rewardId = RewardIDs.Chapter1Complete;

    [Header("Sprites")]
    public Sprite unlockedSprite;
    public Sprite lockedSilhouetteSprite;

    [Header("UI")]
    public Image targetImage;

    [Header("Optional")]
    public bool autoRefreshOnEnable = true;

    private void Awake()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();
    }

    private void OnEnable()
    {
        if (autoRefreshOnEnable)
            Refresh();
    }

    public void Refresh()
    {
        if (targetImage == null) return;

        bool unlocked = (RewardStateManager.Instance != null) && RewardStateManager.Instance.IsRewardUnlocked(rewardId);

        if (unlocked)
        {
            if (unlockedSprite != null) targetImage.sprite = unlockedSprite;
            targetImage.color = Color.white;
        }
        else
        {
            if (lockedSilhouetteSprite != null) targetImage.sprite = lockedSilhouetteSprite;
            targetImage.color = Color.white;
        }
    }
}