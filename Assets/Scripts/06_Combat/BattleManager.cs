using UnityEngine;
using UnityEngine.SceneManagement;

public class BattleManager : MonoBehaviour
{
    public static BattleManager Instance { get; private set; }

    [Header("Debug (read-only in play mode)")]
    public int currentDamage;

    private int goal;
    private string worldScene;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        // Read values set by your entry/trigger system
        goal = BattleReturnData.damageGoal;
        worldScene = BattleReturnData.worldSceneName;

        Debug.Log($"[BattleManager] Battle started. Goal={goal}, returnScene={worldScene}, battleId={BattleReturnData.currentBattleId}");
    }

    /// <summary>
    /// Call this whenever the player deals damage to the enemy.
    /// </summary>
    public void AddDamage(int amount)
    {
        currentDamage += amount;
        Debug.Log($"[BattleManager] Damage = {currentDamage}/{goal}");

        if (currentDamage >= goal)
            OnBattleWon();
    }

    private void OnBattleWon()
    {
        Debug.Log("[BattleManager] Battle complete.");

        // Mark return flags for world systems
        BattleReturnData.comingFromBattle = true;
        BattleReturnData.shouldReturnToWorld = true;

        if (!string.IsNullOrEmpty(worldScene))
        {
            SceneManager.LoadScene(worldScene);
        }
        else
        {
            Debug.LogError("[BattleManager] worldSceneName is empty. Cannot return.");
        }
    }
}
