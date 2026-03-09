using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class RandomEncounterRoom : MonoBehaviour
{
    [Header("Combat Scene")]
    [Tooltip("Your combat scene name in Build Settings.")]
    public string combatSceneName = "06_CombatSystem";

    [Header("Encounter Table")]
    [Tooltip("Enemies that can be encountered in this room.")]
    public List<EnemyData> enemies = new List<EnemyData>();

    [Tooltip("Optional weights matching enemies list. If empty or mismatched, equal chance is used.")]
    public List<float> weights = new List<float>();

    [Header("Encounter Timing")]
    [Tooltip("Random check interval range (seconds).")]
    public Vector2 checkIntervalRange = new Vector2(2.0f, 5.0f);

    [Range(0f, 1f)]
    [Tooltip("Chance to trigger an encounter each check.")]
    public float encounterChancePerCheck = 0.15f;

    [Tooltip("Cooldown after an encounter triggers (prevents immediate re-trigger).")]
    public float cooldownSeconds = 3.0f;

    [Header("Movement Requirement (Pokemon-style)")]
    [Tooltip("If true, encounters only roll while player is moving.")]
    public bool requirePlayerMoving = true;

    [Tooltip("Minimum move magnitude to count as moving (Input System move vector).")]
    public float moveMagnitudeThreshold = 0.10f;

    [Header("Return Tag (Optional)")]
    [Tooltip("If your return applier uses returnTag, set it here. Otherwise leave blank.")]
    public string returnTag = "";

    [Header("Goal (Optional)")]
    [Tooltip("Default goal type for random encounters.")]
    public BattleGoalType goalType = BattleGoalType.DefeatEnemy;

    [Tooltip("Only used if goalType = DealDamageAmount.")]
    public int damageGoal = 0;

    [Header("Restore Snapshot")]
    [Tooltip("Capture all SaveID transforms when leaving the world scene.")]
    public bool captureAllSaveIdTransforms = true;

    [Tooltip("If true, this script will try to consume BattleReturnData return frames on world scene load and restore player + SaveIDs.")]
    public bool applyReturnRestoreOnSceneLoad = true;

    // =========================================================
    // ✅ TRANSITION + CONTROL LOCK
    // =========================================================
    [Header("Transition (Blink)")]
    [Tooltip("Looks for a CanvasGroup named this (your project already uses 'TeleportTransition').")]
    public string fadeCanvasObjectName = "TeleportTransition";

    [Tooltip("Number of blinks before loading combat.")]
    [Range(1, 8)]
    public int blinkCount = 3;

    [Tooltip("How long the screen stays black per blink (unscaled seconds).")]
    [Range(0.02f, 0.35f)]
    public float blinkOn = 0.06f;

    [Tooltip("How long between blinks (unscaled seconds).")]
    [Range(0.02f, 0.35f)]
    public float blinkOff = 0.06f;

    [Tooltip("Final hold on black right before scene load (unscaled seconds).")]
    [Range(0.00f, 0.50f)]
    public float finalWhiteHold = 0.06f; // kept name for compatibility (now holds BLACK)

    [Header("Force Stop Player On Trigger")]
    [Tooltip("If true, locks PlayerMovement immediately when encounter starts (prevents movement during transition).")]
    public bool forceStopPlayerOnTrigger = true;

    [Tooltip("Extra freeze time after blink finishes (unscaled-ish safety).")]
    [Range(0.0f, 1.0f)]
    public float extraFreezeAfterBlink = 0.15f;

    // =========================================================
    // ✅ NEW: BLINK SFX
    // =========================================================
    [Header("Blink SFX")]
    [Tooltip("SFX to play when the blink transition starts (and optionally each blink).")]
    public AudioClip blinkSfx;

    [Range(0f, 1f)]
    public float blinkSfxVolume = 1f;

    [Tooltip("If true: play the SFX for EVERY blink ON. If false: play once at transition start.")]
    public bool playSfxEachBlink = false;

    [Tooltip("If true: randomize pitch slightly per play.")]
    public bool randomizeBlinkSfxPitch = false;

    [Range(0.5f, 2.0f)]
    public float blinkSfxPitchMin = 0.95f;

    [Range(0.5f, 2.0f)]
    public float blinkSfxPitchMax = 1.05f;

    [Tooltip("If assigned, uses this AudioSource (recommended if you want pitch randomization). If null, uses PlayClipAtPoint (no pitch).")]
    public AudioSource blinkSfxSource;

    // =========================================================
    // ✅ SAFETY (fix: encounters triggering outside the room)
    // =========================================================
    [Header("Safety (Anti-Phantom Encounters)")]
    [Tooltip("If true, encounters can ONLY roll if player's position is inside this trigger collider bounds. Prevents 'battle triggers in other rooms'.")]
    public bool requirePlayerInsideThisTriggerBounds = true;

    [Tooltip("Extra margin for bounds check (helps if bounds are tight).")]
    public float boundsMargin = 0.05f;

    [Tooltip("How often to run sanity checks while inside (seconds).")]
    [Range(0.05f, 1.0f)]
    public float sanityCheckInterval = 0.15f;

    [Header("Debug")]
    public bool debugLogs = true;

    // runtime
    private Transform _player;
    private PlayerMovement _playerMove;
    private bool _playerInside;
    private Coroutine _loop;
    private float _nextAllowedTime;

    // track overlaps (handles multiple colliders on player)
    private readonly HashSet<Collider> _playerOverlaps = new HashSet<Collider>();

    private static bool s_loadingBattle; // global safety against double triggers

    // ✅ cached trigger collider (room zone)
    private Collider _zoneCollider;
    private float _nextSanityTime;

    private void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    private void Awake()
    {
        _zoneCollider = GetComponent<Collider>();
        if (_zoneCollider != null) _zoneCollider.isTrigger = true;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;

        if (applyReturnRestoreOnSceneLoad)
            StartCoroutine(TryApplyReturnRestoreRoutine());
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        StopLoop();
        _playerInside = false;
        _player = null;
        _playerMove = null;
        _playerOverlaps.Clear();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // When back in a WORLD scene, allow encounters again
        if (!string.Equals(scene.name, combatSceneName, StringComparison.OrdinalIgnoreCase))
            ResetGlobalLoadingFlag();

        if (applyReturnRestoreOnSceneLoad)
            StartCoroutine(TryApplyReturnRestoreRoutine());
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        _playerOverlaps.Add(other);

        if (_player == null)
        {
            _player = other.transform;

            // If this collider is a child, try to find a PlayerMovement on root
            var root = other.transform.root;
            var pmRoot = root != null ? root.GetComponentInChildren<PlayerMovement>(true) : null;
            _playerMove = pmRoot != null ? pmRoot : other.GetComponent<PlayerMovement>();
        }

        _playerInside = _playerOverlaps.Count > 0;
        _nextSanityTime = Time.time + sanityCheckInterval;

        if (debugLogs) Debug.Log($"[RandomEncounterRoom] Player entered zone '{name}'. overlaps={_playerOverlaps.Count}");

        StartLoop();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        _playerOverlaps.Remove(other);
        _playerInside = _playerOverlaps.Count > 0;

        if (debugLogs) Debug.Log($"[RandomEncounterRoom] Player exit zone '{name}'. overlaps={_playerOverlaps.Count}");

        if (!_playerInside)
        {
            _player = null;
            _playerMove = null;
            StopLoop();
        }
    }

    private void StartLoop()
    {
        if (_loop != null) return;
        _loop = StartCoroutine(EncounterLoop());
    }

    private void StopLoop()
    {
        if (_loop != null) StopCoroutine(_loop);
        _loop = null;
    }

    // =========================================================
    // ✅ HARD SAFETY: remove stale overlaps + verify player is inside THIS trigger bounds
    // =========================================================
    private void SanityCheckInsideState()
    {
        if (_playerOverlaps.Count > 0)
        {
            List<Collider> dead = null;
            foreach (var c in _playerOverlaps)
            {
                if (c == null || !c.gameObject.activeInHierarchy)
                {
                    if (dead == null) dead = new List<Collider>();
                    dead.Add(c);
                }
            }
            if (dead != null)
            {
                for (int i = 0; i < dead.Count; i++)
                    _playerOverlaps.Remove(dead[i]);
            }
        }

        _playerInside = _playerOverlaps.Count > 0;

        if (!_playerInside)
        {
            if (debugLogs) Debug.Log($"[RandomEncounterRoom] SanityCheck: no overlaps -> stopping room '{name}'.");
            _player = null;
            _playerMove = null;
            StopLoop();
            return;
        }

        if (requirePlayerInsideThisTriggerBounds && _zoneCollider != null && _player != null)
        {
            var b = _zoneCollider.bounds;
            b.Expand(boundsMargin * 2f);

            if (!b.Contains(_player.position))
            {
                if (debugLogs)
                    Debug.LogWarning($"[RandomEncounterRoom] SanityCheck: player not inside trigger bounds anymore -> clearing state for '{name}'. Prevent phantom encounters.");

                _playerOverlaps.Clear();
                _playerInside = false;
                _player = null;
                _playerMove = null;
                StopLoop();
            }
        }
    }

    private IEnumerator EncounterLoop()
    {
        yield return new WaitForSeconds(0.2f);

        while (true)
        {
            if (!_playerInside || _playerOverlaps.Count <= 0 || _player == null)
                yield break;

            if (Time.time >= _nextSanityTime)
            {
                _nextSanityTime = Time.time + sanityCheckInterval;
                SanityCheckInsideState();

                if (!_playerInside || _playerOverlaps.Count <= 0 || _player == null)
                    yield break;
            }

            float wait = UnityEngine.Random.Range(
                Mathf.Max(0.1f, checkIntervalRange.x),
                Mathf.Max(checkIntervalRange.x, checkIntervalRange.y)
            );

            yield return new WaitForSeconds(wait);

            if (!_playerInside || _playerOverlaps.Count <= 0 || _player == null)
                yield break;

            SanityCheckInsideState();
            if (!_playerInside || _playerOverlaps.Count <= 0 || _player == null)
                yield break;

            if (s_loadingBattle) continue;
            if (Time.time < _nextAllowedTime) continue;

            if (requirePlayerMoving && !IsPlayerMoving())
                continue;

            if (UnityEngine.Random.value > encounterChancePerCheck)
                continue;

            TryStartEncounter();
        }
    }

    private bool IsPlayerMoving()
    {
        if (_playerMove != null && _playerMove.moveAction != null)
        {
            try
            {
                Vector2 mv = _playerMove.moveAction.action.ReadValue<Vector2>();
                return mv.magnitude >= moveMagnitudeThreshold;
            }
            catch { }
        }

        return true;
    }

    private void TryStartEncounter()
    {
        SanityCheckInsideState();
        if (!_playerInside || _playerOverlaps.Count <= 0 || _player == null) return;

        if (s_loadingBattle) return;

        if (enemies == null || enemies.Count == 0)
        {
            Debug.LogError("[RandomEncounterRoom] No enemies assigned in this zone.");
            _nextAllowedTime = Time.time + cooldownSeconds;
            return;
        }

        EnemyData chosen = ChooseEnemyWeighted();
        if (chosen == null)
        {
            Debug.LogError("[RandomEncounterRoom] Failed to choose an enemy (list contained null?).");
            _nextAllowedTime = Time.time + cooldownSeconds;
            return;
        }

        string worldScene = SceneManager.GetActiveScene().name;

        if (debugLogs)
            Debug.Log($"[RandomEncounterRoom] ENCOUNTER! Enemy='{chosen.name}' -> loading '{combatSceneName}' return='{worldScene}'.");

        s_loadingBattle = true;
        _nextAllowedTime = Time.time + cooldownSeconds;

        if (forceStopPlayerOnTrigger && _playerMove != null)
        {
            try
            {
                _playerMove.AddLock(this);
                _playerMove.canMove = false;

                float blinkTotal =
                    (blinkCount * (blinkOn + blinkOff)) + Mathf.Max(0f, finalWhiteHold) + Mathf.Max(0f, extraFreezeAfterBlink);

                _playerMove.FreezeForSeconds(Mathf.Max(0.05f, blinkTotal));
            }
            catch { }
        }

        // =========================================================
        // ✅ CRITICAL FIX:
        // Push the return frame keyed to the WORLD scene (the scene you are returning to),
        // NOT the combat scene name.
        // =========================================================
        BattleReturnData.PushReturnFrame(worldScene, _player, captureAllSaveIdTransforms);

        // Optional extra safety (uses your in-memory return pose system too; harmless if unused)
        try { LoadCharacter.SaveReturnPoseNow(_player, worldScene); } catch { }

        BattleReturnData.worldSceneName = worldScene;
        BattleReturnData.shouldReturnToWorld = true;
        BattleReturnData.comingFromBattle = false;

        BattleEntryData.hasEntry = true;
        BattleEntryData.enemyData = chosen;
        BattleEntryData.goalType = goalType;
        BattleEntryData.damageGoal = (goalType == BattleGoalType.DealDamageAmount) ? Mathf.Max(1, damageGoal) : 0;
        BattleEntryData.damageDealt = 0;

        BattleEntryData.battleId = null;
        BattleEntryData.returnScene = worldScene;
        BattleEntryData.returnTag = string.IsNullOrEmpty(returnTag) ? null : returnTag;

        StartCoroutine(BlinkAndLoadCombat());
    }

    // =========================================================
    // ✅ BLINK TRANSITION + SFX  (UPDATED: BLACK FLASH)
    // =========================================================
    private IEnumerator BlinkAndLoadCombat()
    {
        // ✅ Play SFX at transition start (once)
        if (!playSfxEachBlink)
            PlayBlinkSfxOnce();

        CanvasGroup cg = FindFadeCanvasGroup();

        if (cg != null)
        {
            var g = cg.GetComponentInChildren<Graphic>(true);
            Color old = default;
            bool hadGraphic = (g != null);

            if (hadGraphic)
            {
                old = g.color;
                // ✅ BLACK flash (keep original alpha)
                g.color = new Color(0f, 0f, 0f, old.a);
            }

            cg.gameObject.SetActive(true);
            cg.blocksRaycasts = true;
            cg.interactable = true;

            cg.alpha = 0f;

            int n = Mathf.Max(1, blinkCount);

            float onT = Mathf.Max(0.01f, blinkOn);
            float offT = Mathf.Max(0.01f, blinkOff);

            // ✅ Smooth timing (no snappy on/off)
            float fadeInDur = Mathf.Max(0.01f, onT * 0.65f);
            float holdBlack = Mathf.Max(0f, onT - fadeInDur);

            float fadeOutDur = Mathf.Max(0.01f, offT * 0.65f);
            float holdClear = Mathf.Max(0f, offT - fadeOutDur);

            for (int i = 0; i < n; i++)
            {
                // ✅ Each blink ON (optional SFX)
                if (playSfxEachBlink)
                    PlayBlinkSfxOnce();

                // Fade IN to black
                yield return FadeCanvasGroup(cg, 0f, 1f, fadeInDur);

                // Hold black briefly
                if (holdBlack > 0f)
                    yield return new WaitForSecondsRealtime(holdBlack);

                // Fade OUT back to transparent
                yield return FadeCanvasGroup(cg, 1f, 0f, fadeOutDur);

                // Hold transparent briefly
                if (holdClear > 0f)
                    yield return new WaitForSecondsRealtime(holdClear);
            }

            // Final smooth fade to black before loading combat
            yield return FadeCanvasGroup(cg, cg.alpha, 1f, Mathf.Max(0.01f, blinkOn * 0.65f));

            if (finalWhiteHold > 0f)
                yield return new WaitForSecondsRealtime(finalWhiteHold);

            if (hadGraphic) g.color = old;
        }

        SceneManager.LoadScene(combatSceneName);
    }

    // ✅ Helper: smooth alpha fade (unscaled time so it works even if timescale changes)
    private IEnumerator FadeCanvasGroup(CanvasGroup cg, float from, float to, float duration)
    {
        if (cg == null) yield break;

        duration = Mathf.Max(0.01f, duration);
        float t = 0f;

        cg.alpha = from;

        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.Clamp01(t / duration);
            cg.alpha = Mathf.Lerp(from, to, u);
            yield return null;
        }

        cg.alpha = to;
    }

    private void PlayBlinkSfxOnce()
    {
        if (!blinkSfx) return;

        float vol = Mathf.Clamp01(blinkSfxVolume);

        // If you assigned a source, we can do pitch randomization
        if (blinkSfxSource != null)
        {
            float oldPitch = blinkSfxSource.pitch;

            if (randomizeBlinkSfxPitch)
                blinkSfxSource.pitch = UnityEngine.Random.Range(
                    Mathf.Min(blinkSfxPitchMin, blinkSfxPitchMax),
                    Mathf.Max(blinkSfxPitchMin, blinkSfxPitchMax)
                );
            else
                blinkSfxSource.pitch = 1f;

            blinkSfxSource.PlayOneShot(blinkSfx, vol);

            // restore pitch so you don't affect other sounds using the same source
            blinkSfxSource.pitch = oldPitch;
            return;
        }

        // Fallback: guaranteed to play even if scene switches quickly (no pitch control)
        Vector3 pos = (_player != null) ? _player.position : (Camera.main ? Camera.main.transform.position : Vector3.zero);
        AudioSource.PlayClipAtPoint(blinkSfx, pos, vol);
    }

    private EnemyData ChooseEnemyWeighted()
    {
        if (weights == null || weights.Count != enemies.Count)
        {
            int idx = UnityEngine.Random.Range(0, enemies.Count);
            return enemies[idx];
        }

        float total = 0f;
        for (int i = 0; i < weights.Count; i++)
            total += Mathf.Max(0f, weights[i]);

        if (total <= 0.0001f)
        {
            int idx = UnityEngine.Random.Range(0, enemies.Count);
            return enemies[idx];
        }

        float r = UnityEngine.Random.Range(0f, total);
        float acc = 0f;

        for (int i = 0; i < enemies.Count; i++)
        {
            acc += Mathf.Max(0f, weights[i]);
            if (r <= acc)
                return enemies[i];
        }

        return enemies[enemies.Count - 1];
    }

    // =========================================================
    // RETURN RESTORE (kept working)
    // =========================================================
    private IEnumerator TryApplyReturnRestoreRoutine()
    {
        if (string.Equals(SceneManager.GetActiveScene().name, combatSceneName, StringComparison.OrdinalIgnoreCase))
            yield break;

        yield return null;
        yield return null;
        yield return new WaitForFixedUpdate();

        if (!BattleReturnData.HasPendingReturn())
            yield break;

        if (!BattleReturnData.TryConsumeReturnForCurrentScene(out var playerPos, out var playerRot, out var objs))
            yield break;

        var pgo = GameObject.FindGameObjectWithTag("Player");
        if (pgo != null)
        {
            var pt = pgo.transform;

            var pm = pgo.GetComponent<PlayerMovement>();
            if (pm != null) pm.FreezeForSeconds(0.35f);

            var cc = pgo.GetComponent<CharacterController>();
            if (cc != null)
            {
                bool was = cc.enabled;
                cc.enabled = false;
                pt.SetPositionAndRotation(playerPos, playerRot);
                cc.enabled = was;
            }
            else
            {
                pt.SetPositionAndRotation(playerPos, playerRot);
            }

            if (pm != null) pm.ForceSnapToGroundNow();
        }

        if (objs != null && objs.Count > 0)
        {
            var all = UnityEngine.Object.FindObjectsByType<SaveID>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var map = new Dictionary<string, SaveID>(all.Length);
            foreach (var sid in all)
            {
                if (sid == null || string.IsNullOrEmpty(sid.ID)) continue;
                if (!map.ContainsKey(sid.ID)) map.Add(sid.ID, sid);
            }

            for (int i = 0; i < objs.Count; i++)
            {
                var (id, pos, rot) = objs[i];
                if (string.IsNullOrEmpty(id)) continue;

                if (map.TryGetValue(id, out var sid) && sid != null)
                {
                    var t = sid.transform;
                    t.SetPositionAndRotation(pos, rot);
                }
            }
        }
    }

    private CanvasGroup FindFadeCanvasGroup()
    {
        if (string.IsNullOrEmpty(fadeCanvasObjectName)) return null;

        var groups = UnityEngine.Object.FindObjectsOfType<CanvasGroup>(true);
        foreach (var cg in groups)
        {
            if (!cg) continue;
            if (cg.gameObject.name == fadeCanvasObjectName || cg.transform.name == fadeCanvasObjectName)
                return cg;
        }
        return null;
    }

    public static void ResetGlobalLoadingFlag()
    {
        s_loadingBattle = false;
    }
}