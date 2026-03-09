using System;
using UnityEngine;

public class PlayerAttackSystem : MonoBehaviour
{
    [Header("QTE UI")]
    [Tooltip("Drag your QTE panel root that has AttackQTEUI on it.")]
    public AttackQTEUI qteUI;

    [Header("Damage Output")]
    [Tooltip("Damage at 0 accuracy.")]
    public int minDamage = 5;

    [Tooltip("Damage at 1 accuracy.")]
    public int maxDamage = 60;

    [Header("Optional: Speed Override")]
    [Tooltip("If true, overrides AttackQTEUI secondsToCrossBar for this attack only.")]
    public bool overrideQTESpeed = false;

    [Range(0.15f, 6f)]
    public float overrideSecondsToCrossBar = 1.2f;

    private bool _running;
    private Action<int> _onDone;

    public void StartQTE(Action<int> onDone)
    {
        if (_running) return;
        _running = true;

        _onDone = onDone;

        if (qteUI == null)
        {
            Debug.LogError("[PlayerAttackSystem] qteUI is not assigned. Cannot run QTE.");
            Finish(0);
            return;
        }

        // ✅ Do NOT touch cursorSpeedPixelsPerSecond.
        // Only override secondsToCrossBar IF you explicitly enabled override.
        if (overrideQTESpeed)
            qteUI.SetSpeedOverrideSeconds(overrideSecondsToCrossBar);
        else
            qteUI.ClearSpeedOverride();

        // Ensure QTE is visible + running
        qteUI.gameObject.SetActive(true);

        // Use float accuracy -> convert to damage
        qteUI.StartQTE(accuracy =>
        {
            int dmg = AccuracyToDamage(accuracy);
            Finish(dmg);
        });
    }

    private int AccuracyToDamage(float accuracy01)
    {
        accuracy01 = Mathf.Clamp01(accuracy01);

        // ✅ If player missed / timed out (accuracy = 0), deal 0 damage.
        if (accuracy01 <= 0f)
            return 0;

        // Otherwise scale between minDamage..maxDamage
        float dmgF = Mathf.Lerp(minDamage, maxDamage, accuracy01);
        return Mathf.RoundToInt(dmgF);
    }


    private void Finish(int damage)
    {
        if (!_running) return;
        _running = false;

        try
        {
            _onDone?.Invoke(damage);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            _onDone = null;
        }
    }
}
