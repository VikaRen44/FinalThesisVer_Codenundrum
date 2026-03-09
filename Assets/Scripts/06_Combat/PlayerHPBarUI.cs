using UnityEngine;
using UnityEngine.UI;

public class PlayerHPBarUI : MonoBehaviour
{
    [Header("Refs")]
    public PlayerStats playerStats;   // drag PlayerStatsRoot here
    public Slider slider;            // your HP slider

    [Header("Slider Mode")]
    public bool useNormalizedValue = true;
    // true  -> slider.value is 0..1 (percentage)
    // false -> slider.value is 0..maxHP (absolute HP)

    private void Awake()
    {
        if (slider == null)
            slider = GetComponent<Slider>();

        if (playerStats == null)
            playerStats = FindObjectOfType<PlayerStats>();
    }

    private void OnEnable()
    {
        if (playerStats != null)
            playerStats.OnHPChanged += Refresh;

        Refresh(); // initialize UI
    }

    private void OnDisable()
    {
        if (playerStats != null)
            playerStats.OnHPChanged -= Refresh;
    }

    public void Refresh()
    {
        if (playerStats == null || slider == null) return;

        if (useNormalizedValue)
        {
            // slider min/max should be 0..1 in the Inspector
            float hpPercent = (float)playerStats.HP / playerStats.maxHP;
            slider.value = hpPercent;
        }
        else
        {
            // slider min should be 0, max = maxHP
            slider.maxValue = playerStats.maxHP;
            slider.value = playerStats.HP;
        }
    }
}

