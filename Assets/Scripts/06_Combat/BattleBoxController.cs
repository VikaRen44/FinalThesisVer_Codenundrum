using System;
using System.Collections;
using UnityEngine;

public class BattleBoxController : MonoBehaviour
{
    [Header("Refs")]
    public RectTransform boxMask;       // BoxMask (the grey area)
    public SoulControllerUI soul;       // the red soul script
    public GameObject bulletPrefab;     // BulletUI prefab (UI Image)

    [Header("UI")]
    public GameObject panelDialogue;    // Panel_Dialogue

    [Header("Default Timing")]
    public float defaultDuration = 8f;

    [Header("Side Explode Settings")]
    public float explodeDelayMin = 0.4f;
    public float explodeDelayMax = 1.2f;
    public int explodeChildCount = 10;
    public float explodeChildSpeed = 250f;
    [Range(0f, 1f)] public float explodeChildDamageFactor = 0.7f;

    private Coroutine _attackRoutine;

    // ✅ Cache (avoids deprecated FindObjectOfType spam + avoids repeated searching)
    private EnemyAttackSystem _enemyAttackSystem;

    private void Awake()
    {
        // Just hide the box visuals at start
        if (boxMask != null)
            boxMask.gameObject.SetActive(false);

        // Make sure dialogue is visible outside of battle
        if (panelDialogue != null)
            panelDialogue.SetActive(true);

        // Cache EnemyAttackSystem once (optional – can be null)
        _enemyAttackSystem = FindEnemyAttackSystem();

        if (soul != null)
        {
            var soulRect = soul.GetComponent<RectTransform>();
            if (soulRect != null)
                soulRect.anchoredPosition = Vector2.zero;

            var ps = soul.GetComponent<PlayerSoul>();
            if (ps != null)
                ps.ResetInvulnerability();

            soul.SetControl(true);
        }
    }

    /// <summary>
    /// Starts a bullet-hell phase with given attack data.
    /// Calls onFinished when done.
    /// </summary>
    public void RunAttack(EnemyAttackData attack, PlayerStats player, Action onFinished)
    {
        Debug.Log("[BattleBoxController] RunAttack with: " +
                  (attack != null ? attack.attackName : "NULL"));

        if (_attackRoutine != null)
            StopCoroutine(_attackRoutine);

        _attackRoutine = StartCoroutine(AttackRoutine(attack, player, onFinished));
    }

    private IEnumerator AttackRoutine(EnemyAttackData attack, PlayerStats player, Action onFinished)
    {
        if (attack == null || player == null)
        {
            onFinished?.Invoke();
            yield break;
        }

        // Ensure cache is valid even if this object spawned before EnemyAttackSystem existed.
        if (_enemyAttackSystem == null)
            _enemyAttackSystem = FindEnemyAttackSystem();

        float duration = attack.duration > 0f ? attack.duration : defaultDuration;

        // HIDE DIALOGUE while the battle box is active
        if (panelDialogue != null)
            panelDialogue.SetActive(false);

        // SHOW BOX
        if (boxMask != null)
            boxMask.gameObject.SetActive(true);

        if (soul != null)
        {
            var soulRect = soul.GetComponent<RectTransform>();
            if (soulRect != null)
                soulRect.anchoredPosition = Vector2.zero;

            var soulDamage = soul.GetComponent<PlayerSoul>();
            if (soulDamage != null)
                soulDamage.ResetInvulnerability();

            var soulMovement = soul.GetComponent<SoulControllerUI>();
            if (soulMovement != null)
                soulMovement.SetControl(true);
        }

        Debug.Log("[BattleBoxController] Battle box active, starting bullets.");

        float t = 0f;
        float spawnTimer = 0f;
        bool radialSpawned = false;   // <- for TV-logo pattern

        while (t < duration)
        {
            t += Time.deltaTime;
            spawnTimer += Time.deltaTime;

            if (spawnTimer >= attack.spawnInterval)
            {
                spawnTimer -= attack.spawnInterval;

                if (attack.pattern == BulletPatternType.Radial)
                {
                    // Radial/TV pattern: spawn only once per attack
                    if (!radialSpawned)
                    {
                        SpawnBullet(attack);
                        radialSpawned = true;
                    }
                }
                else
                {
                    // All other patterns use normal spawn timing
                    SpawnBullet(attack);
                }
            }

            yield return null;
        }

        // END PHASE: stop control & hide box
        if (soul != null)
            soul.SetControl(false);

        // Destroy any remaining bullets under boxMask
        if (boxMask != null)
        {
            for (int i = boxMask.childCount - 1; i >= 0; i--)
            {
                var child = boxMask.GetChild(i);
                if (child.GetComponent<BulletUI>() != null)
                    Destroy(child.gameObject);
            }

            boxMask.gameObject.SetActive(false);
        }

        // SHOW DIALOGUE again after the attack
        if (panelDialogue != null)
            panelDialogue.SetActive(true);

        _attackRoutine = null;
        onFinished?.Invoke();
    }

    // ===== BULLET HELPERS =====

    private BulletUI CreateBullet(
        Vector2 anchoredPos,
        Vector2 direction,
        int damage,
        float speed,
        bool bounce = false,
        float scale = 0f   // 0 = keep prefab scale, >0 = override
    )
    {
        if (bulletPrefab == null || boxMask == null || soul == null) return null;

        var bulletGO = Instantiate(bulletPrefab, boxMask);
        var bulletRect = bulletGO.GetComponent<RectTransform>();
        var bullet = bulletGO.GetComponent<BulletUI>();

        if (bullet == null)
        {
            bullet = bulletGO.GetComponentInChildren<BulletUI>();
            if (bullet == null)
            {
                Debug.LogError("[BattleBoxController] Bullet prefab has no BulletUI!");
                Destroy(bulletGO);
                return null;
            }
        }

        bullet.movementArea = boxMask;
        bullet.soulRect = soul.GetComponent<RectTransform>();
        bullet.soul = soul.GetComponent<PlayerSoul>();
        bullet.damage = damage;
        bullet.speed = speed;
        bullet.direction = direction;
        bullet.bounceInsideArea = bounce;

        if (bulletRect != null)
        {
            bulletRect.anchoredPosition = anchoredPos;

            if (scale > 0f)
                bulletRect.localScale = Vector3.one * scale;
        }

        return bullet;
    }

    private void SpawnBullet(EnemyAttackData attack)
    {
        if (attack == null) return;
        if (bulletPrefab == null || boxMask == null || soul == null) return;

        // damage & speed (with optional debuff)
        float multiplier = (_enemyAttackSystem != null) ? _enemyAttackSystem.damageMultiplier : 1f;
        int baseDamage = Mathf.RoundToInt(attack.bulletDamage * multiplier);
        float baseSpeed = attack.bulletSpeed;

        Rect area = boxMask.rect;

        switch (attack.pattern)
        {
            case BulletPatternType.SimpleRain:
            default:
                {
                    float x = UnityEngine.Random.Range(area.xMin, area.xMax);
                    float y = area.yMax + 40f;
                    Vector2 pos = new Vector2(x, y);
                    Vector2 dir = Vector2.down;
                    CreateBullet(pos, dir, baseDamage, baseSpeed);
                    break;
                }

            case BulletPatternType.Horizontal:
                {
                    bool fromLeft = UnityEngine.Random.value < 0.5f;
                    float yH = UnityEngine.Random.Range(area.yMin, area.yMax);
                    float xH = fromLeft ? area.xMin - 40f : area.xMax + 40f;
                    Vector2 pos = new Vector2(xH, yH);
                    Vector2 dir = fromLeft ? Vector2.right : Vector2.left;
                    CreateBullet(pos, dir, baseDamage, baseSpeed);
                    break;
                }

            case BulletPatternType.Radial:
                {
                    float margin = 30f;
                    Vector2 pos1 = new Vector2(area.xMin + margin, area.yMin + margin); // bottom-left
                    Vector2 pos2 = new Vector2(area.xMax - margin, area.yMax - margin); // top-right

                    Vector2 dir1 = new Vector2(1f, 1f).normalized;
                    Vector2 dir2 = new Vector2(-1f, -1f).normalized;

                    float tvSpeed = attack.bulletSpeed * 1.5f;
                    float tvScale = 1.6f;   // only these ones are big

                    CreateBullet(pos1, dir1, baseDamage, tvSpeed, bounce: true, scale: tvScale);
                    CreateBullet(pos2, dir2, baseDamage, tvSpeed, bounce: true, scale: tvScale);
                    break;
                }

            case BulletPatternType.SideExplode:
                {
                    bool fromLeft = UnityEngine.Random.value < 0.5f;
                    float ySe = UnityEngine.Random.Range(area.yMin, area.yMax);
                    float xSe = fromLeft ? area.xMin - 40f : area.xMax + 40f;
                    Vector2 pos = new Vector2(xSe, ySe);
                    Vector2 dir = fromLeft ? Vector2.right : Vector2.left;

                    BulletUI exploder = CreateBullet(pos, dir, baseDamage, baseSpeed);

                    if (exploder != null)
                    {
                        RectTransform exploderRect = exploder.GetComponent<RectTransform>();
                        StartCoroutine(ExplodeBulletRoutine(exploderRect, baseDamage));
                    }
                    break;
                }
        }
    }

    private IEnumerator ExplodeBulletRoutine(RectTransform bulletRect, int baseDamage)
    {
        if (bulletRect == null) yield break;

        float delay = UnityEngine.Random.Range(explodeDelayMin, explodeDelayMax);
        float t = 0f;

        while (t < delay)
        {
            if (bulletRect == null) yield break;
            t += Time.deltaTime;
            yield return null;
        }

        if (bulletRect == null) yield break;

        Vector2 origin = bulletRect.anchoredPosition;

        int count = Mathf.Max(3, explodeChildCount);
        int gapStart = UnityEngine.Random.Range(0, count);

        int childDamage = Mathf.RoundToInt(baseDamage * explodeChildDamageFactor);

        for (int i = 0; i < count; i++)
        {
            if (i == gapStart || i == (gapStart + 1) % count)
                continue;

            float angleDeg = (360f / count) * i;
            float angleRad = angleDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)).normalized;

            CreateBullet(origin, dir, childDamage, explodeChildSpeed);
        }

        if (bulletRect != null)
            Destroy(bulletRect.gameObject);
    }

    // -----------------------------------------
    // ✅ Unity-version-safe finder
    // -----------------------------------------
    private EnemyAttackSystem FindEnemyAttackSystem()
    {
#if UNITY_2023_1_OR_NEWER
        return FindFirstObjectByType<EnemyAttackSystem>();
#else
        return FindObjectOfType<EnemyAttackSystem>();
#endif
    }
}
