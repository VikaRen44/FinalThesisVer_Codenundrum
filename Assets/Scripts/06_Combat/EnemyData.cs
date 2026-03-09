using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Battle/Enemy")]
public class EnemyData : ScriptableObject
{
    public string enemyName;
    public int maxHP;
    public Sprite portrait;

    public List<EnemyAttackData> attacks; // bullet-hell patterns
    public List<ActOptionData> actOptions; // custom ACT moves
}
