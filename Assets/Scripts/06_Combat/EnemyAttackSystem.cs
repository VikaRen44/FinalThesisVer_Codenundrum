using UnityEngine;

public class EnemyAttackSystem : MonoBehaviour
{
    [Header("Refs")]
    public BattleBoxController battleBox;

    [Header("Debuff & Spare")]
    public float damageMultiplier = 1f;
    public int spareThreshold = 100;

    private int _spareProgress = 0;

    // remember last attack index so we don't spam the same one
    private int _lastAttackIndex = -1;

    // ----------------- ATTACK CHOICE -----------------
    public EnemyAttackData ChooseAttack(EnemyData enemy)
    {
        if (enemy == null || enemy.attacks == null || enemy.attacks.Count == 0)
        {
            Debug.LogWarning("[EnemyAttackSystem] Enemy has no attacks.");
            return null;
        }

        int chosenIndex;

        if (enemy.attacks.Count == 1)
        {
            // only one attack, nothing to randomize
            chosenIndex = 0;
        }
        else
        {
            // pick a random index, avoiding the same one as last turn
            do
            {
                chosenIndex = Random.Range(0, enemy.attacks.Count); // max is EXCLUSIVE
            }
            while (chosenIndex == _lastAttackIndex);
        }

        _lastAttackIndex = chosenIndex;
        EnemyAttackData atk = enemy.attacks[chosenIndex];

        Debug.Log($"[EnemyAttackSystem] ChooseAttack => index {chosenIndex} : {(atk != null ? atk.attackName : "NULL")}");

        return atk;
    }

    // ----------------- RUN ATTACK -----------------
    public void PlayAttack(EnemyAttackData attack, PlayerStats player, System.Action onFinished)
    {
        Debug.Log("[EnemyAttackSystem] PlayAttack called with: " + (attack != null ? attack.attackName : "NULL"));

        if (battleBox == null)
        {
            Debug.LogError("[EnemyAttackSystem] BattleBoxController reference is missing.");
            onFinished?.Invoke();
            return;
        }

        if (attack == null)
        {
            onFinished?.Invoke();
            return;
        }

        battleBox.RunAttack(attack, player, onFinished);
    }

    // ----------------- ACT EFFECTS -----------------
    public void ApplyDebuff(int value)
    {
        float delta = value / 100f;
        damageMultiplier = Mathf.Max(0.1f, damageMultiplier - delta);
        Debug.Log($"[EnemyAttackSystem] Debuff applied. New damage multiplier = {damageMultiplier}");
    }

    public void AddSpareProgress(int value)
    {
        _spareProgress += value;
        Debug.Log($"[EnemyAttackSystem] Spare progress = {_spareProgress}/{spareThreshold}");

        if (_spareProgress >= spareThreshold)
        {
            Debug.Log("[EnemyAttackSystem] Enemy can now be spared!");
        }
    }
}
