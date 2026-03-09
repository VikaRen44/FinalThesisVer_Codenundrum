using UnityEngine;

public static class BattleEntryData
{
    public static bool hasEntry;                 // tells combat "I came from cutscene"
    public static BattleType battleType;
    public static BattleGoalType goalType;
    public static int damageGoal;
    public static int damageDealt;
    public static string battleId;

    // ✅ NEW: Enemy to use in Combat
    // This lets your cutscene/encounter choose exactly which EnemyData asset Combat should load.
    public static EnemyData enemyData;

    // ✅ REQUIRED for returning
    public static string returnScene;            // name of the world scene to return to
    public static string returnTag;              // must match SceneReturnApplier rule tag

    public static void Clear()
    {
        hasEntry = false;
        battleType = BattleType.Normal;
        goalType = BattleGoalType.DefeatEnemy;
        damageGoal = 0;
        damageDealt = 0;
        battleId = null;

        enemyData = null; // ✅ clear enemy reference too

        returnScene = null;
        returnTag = null;
    }

    public static void DebugLog()
    {
        string enemyName = enemyData != null ? enemyData.name : "NULL";
        Debug.Log($"[BattleEntryData] hasEntry={hasEntry} type={battleType} goal={goalType} dealt={damageDealt}/{damageGoal} id={battleId} enemy={enemyName} returnScene={returnScene} returnTag={returnTag}");
    }
}
