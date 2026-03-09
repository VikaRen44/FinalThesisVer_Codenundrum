using UnityEngine;

public abstract class ActHandler : ScriptableObject
{
    public abstract void Execute(BattleController battle, PlayerStats player, EnemyData enemy);
}
