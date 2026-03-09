using UnityEngine;

public enum BulletPatternType
{
    SimpleRain,   // random from top downward
    Horizontal,   // from left/right
    Radial,       // from center outward
    SideExplode   // NEW → bullets move inside then explode
}

[CreateAssetMenu(menuName = "Battle/Enemy Attack")]
public class EnemyAttackData : ScriptableObject
{
    public string attackName = "Attack";
    [Tooltip("How long this attack lasts in seconds.")]
    public float duration = 8f;

    [Header("Bullet Settings")]
    public int bulletDamage = 5;
    public float bulletSpeed = 220f;
    public float spawnInterval = 0.35f;

    public BulletPatternType pattern = BulletPatternType.SimpleRain;
}
