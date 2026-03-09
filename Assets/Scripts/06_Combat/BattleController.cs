using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;

public enum BattleState { Start, PlayerTurn, ResolvingPlayerAction, EnemyTurn, Won, Lost }

public class BattleController : MonoBehaviour
{
    public BattleState state = BattleState.Start;

    [Header("Refs")]
    public EnemyData enemyData;
    public PlayerStats player;
    public BattleUI ui;
    public EnemyAttackSystem enemyAttackSystem;
    public PlayerAttackSystem playerAttackSystem;

    [Header("Enemy View (UI)")]
    public EnemyView enemyView;

    [Header("Enemy Faint Effect (UI)")]
    [Tooltip("Optional: Assign a component with faint animation (shake+fall+fade). If null, win still works without it.")]
    public EnemyFaintEffectUI enemyFaintEffect;

    [Header("Battle Dialogue")]
    public BattleDialogueUI dialogueUI;
    public BattleDialogueSequence playerTurnPrompt;

    [Header("ACT Menu")]
    public BattleActMenuUI actMenuUI;

    [Header("ITEM Menu")]
    public BattleItemMenuUI itemMenuUI;

    // ===================== GAME OVER =====================
    [Header("Game Over UI")]
    [Tooltip("Root panel/canvas for the Game Over screen (set inactive by default).")]
    public GameObject gameOverUIRoot;

    [Tooltip("Optional: If you use a pause system, you can pause time on game over.")]
    public bool pauseTimeOnGameOver = false;

    [Tooltip("Scene name for your main menu (must be in Build Settings).")]
    public string mainMenuSceneName = "01_MainMenu";

    public int EnemyHP { get; private set; }

    private bool _hasGoal;
    private BattleGoalType _goalType;
    private int _damageGoal;

    private bool _gameOverShown;

    // ✅ Gate enemy turn so it CANNOT start until we allow it
    private bool _allowEnemyTurn = false;
    private bool _queuedEnemyTurn = false;

    private void Start()
    {
        if (gameOverUIRoot != null) gameOverUIRoot.SetActive(false);

        if (BattleEntryData.hasEntry && BattleEntryData.enemyData != null)
        {
            enemyData = BattleEntryData.enemyData;
            Debug.Log($"[BattleController] Using EnemyData from entry: {enemyData.name}");
        }

        if (enemyData == null) { Debug.LogError("[BattleController] EnemyData missing."); enabled = false; return; }
        if (player == null) { Debug.LogError("[BattleController] PlayerStats missing."); enabled = false; return; }
        if (ui == null) { Debug.LogError("[BattleController] BattleUI missing."); enabled = false; return; }
        if (enemyAttackSystem == null) { Debug.LogError("[BattleController] EnemyAttackSystem missing."); enabled = false; return; }
        if (playerAttackSystem == null) { Debug.LogError("[BattleController] PlayerAttackSystem missing."); enabled = false; return; }

        EnemyHP = Mathf.Max(1, enemyData.maxHP);

        if (BattleEntryData.hasEntry)
        {
            _hasGoal = true;
            _goalType = BattleEntryData.goalType;
            _damageGoal = BattleEntryData.damageGoal;
        }
        else
        {
            _hasGoal = false;
        }

        ui.Bind(this);
        if (actMenuUI != null) actMenuUI.Bind(this);
        if (itemMenuUI != null) itemMenuUI.Bind(this);
        if (enemyView != null) enemyView.SetEnemy(enemyData);

        EnterPlayerTurn();
    }

    private void EnterPlayerTurn()
    {
        if (_gameOverShown) return;

        state = BattleState.PlayerTurn;
        ui.ShowMainMenu();

        if (dialogueUI == null) return;

        if (playerTurnPrompt != null && playerTurnPrompt.lines != null && playerTurnPrompt.lines.Count > 0)
            dialogueUI.PlaySequence(playerTurnPrompt);
        else
            dialogueUI.ShowRuntimeLine("What do you choose to do?");
    }

    // =========================================================
    // ✅ IMPORTANT: prevent QTE-ending click from auto-advancing
    // Release first, then require a NEW press.
    // =========================================================
    private IEnumerator WaitForAdvanceKey()
    {
        // Let 1 frame pass so input states update after QTE callback
        yield return null;

        // Require release first (prevents QTE click from counting as advance)
        while (!_gameOverShown && IsAnyAdvanceHeld())
            yield return null;

        // Now require a NEW press
        while (!_gameOverShown && !AdvancePressedThisFrame())
            yield return null;
    }

    private bool AdvancePressedThisFrame()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.eKey.wasPressedThisFrame) return true;
            if (Keyboard.current.spaceKey.wasPressedThisFrame) return true;
            if (Keyboard.current.enterKey.wasPressedThisFrame) return true;
            if (Keyboard.current.numpadEnterKey.wasPressedThisFrame) return true;
        }

        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.wasPressedThisFrame) return true;
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) return true;

        return false;
    }

    private bool IsAnyAdvanceHeld()
    {
        if (Keyboard.current != null)
        {
            if (Keyboard.current.eKey.isPressed) return true;
            if (Keyboard.current.spaceKey.isPressed) return true;
            if (Keyboard.current.enterKey.isPressed) return true;
            if (Keyboard.current.numpadEnterKey.isPressed) return true;
        }

        if (Gamepad.current != null)
        {
            if (Gamepad.current.buttonSouth.isPressed) return true;
        }

        if (Mouse.current != null && Mouse.current.leftButton.isPressed) return true;

        return false;
    }

    // ---------------- ATTACK ----------------
    public void OnPlayerChooseAttack()
    {
        if (state != BattleState.PlayerTurn) return;
        if (_gameOverShown) return;

        state = BattleState.ResolvingPlayerAction;
        ui.HideMenus();
        if (dialogueUI != null) dialogueUI.ClearText();

        // ✅ Hard block enemy turn until we explicitly allow it
        _allowEnemyTurn = false;
        _queuedEnemyTurn = false;

        playerAttackSystem.StartQTE(damage =>
        {
            if (_gameOverShown) return;
            StartCoroutine(PlayerAttackFlow(damage));
        });
    }

    private IEnumerator PlayerAttackFlow(int damage)
    {
        damage = Mathf.Max(0, damage);

        EnemyHP -= damage;

        if (BattleEntryData.hasEntry)
            BattleEntryData.damageDealt += damage;

        if (damage > 0 && enemyView != null)
            enemyView.ShowHit(damage);

        // ✅ Show damage line FIRST
        if (dialogueUI != null)
            dialogueUI.ShowRuntimeLine(damage > 0 ? $"You dealt {damage} damage!" : "MISS!");

        // ✅ Wait for NEW press (release first)
        yield return WaitForAdvanceKey();
        if (_gameOverShown) yield break;

        // Goal check (deal damage goal)
        if (_hasGoal && _goalType == BattleGoalType.DealDamageAmount)
        {
            int dealt = BattleEntryData.hasEntry ? BattleEntryData.damageDealt : 0;
            if (dealt >= _damageGoal)
            {
                Debug.Log($"[BattleController] Goal reached: {dealt}/{_damageGoal} -> exit combat");
                yield return WinRoutine();
                yield break;
            }
        }

        // Enemy defeated?
        if (EnemyHP <= 0)
        {
            yield return WinRoutine();
            yield break;
        }

        // ✅ Now allow enemy turn and start it
        _allowEnemyTurn = true;
        StartEnemyTurn();
    }

    // ---------------- ACT ----------------
    public void OpenActMenu()
    {
        if (state != BattleState.PlayerTurn) return;
        if (_gameOverShown) return;

        if (enemyData.actOptions != null && enemyData.actOptions.Count > 0)
        {
            ui.HideMenus();
            if (actMenuUI != null) actMenuUI.Show(enemyData.actOptions);
            if (dialogueUI != null) dialogueUI.ShowRuntimeLine("Choose an ACT.");
        }
        else
        {
            if (dialogueUI != null) dialogueUI.ShowRuntimeLine("No ACT options assigned.");
        }
    }

    public void OnPlayerChooseAct(ActOptionData act)
    {
        if (state != BattleState.PlayerTurn) return;
        if (_gameOverShown) return;

        state = BattleState.ResolvingPlayerAction;
        ui.HideMenus();
        if (actMenuUI != null) actMenuUI.Hide();

        StartCoroutine(ActRoutine(act));
    }

    private IEnumerator ActRoutine(ActOptionData act)
    {
        ResolveAct(act);
        yield return WaitForAdvanceKey();
        if (_gameOverShown) yield break;

        if (state == BattleState.ResolvingPlayerAction)
        {
            _allowEnemyTurn = true;
            StartEnemyTurn();
        }
    }

    // ---------------- ITEM ----------------
    public void OpenItemMenu()
    {
        if (state != BattleState.PlayerTurn) return;
        if (_gameOverShown) return;

        if (player.inventory != null && player.inventory.Count > 0)
        {
            ui.HideMenus();
            if (itemMenuUI != null) itemMenuUI.Show(player.inventory);
            if (dialogueUI != null) dialogueUI.ShowRuntimeLine("Choose an ITEM.");
        }
        else
        {
            if (dialogueUI != null) dialogueUI.ShowRuntimeLine("No items!");
        }
    }

    public void OnPlayerChooseItem(ItemData item)
    {
        if (state != BattleState.PlayerTurn) return;
        if (_gameOverShown) return;

        state = BattleState.ResolvingPlayerAction;
        ui.HideMenus();
        if (itemMenuUI != null) itemMenuUI.Hide();

        StartCoroutine(ItemRoutine(item));
    }

    public void ReturnFromSubMenu()
    {
        if (state != BattleState.PlayerTurn) return;
        if (_gameOverShown) return;

        if (actMenuUI != null) actMenuUI.Hide();
        if (itemMenuUI != null) itemMenuUI.Hide();

        if (ui != null) ui.ShowMainMenu();

        if (dialogueUI != null)
        {
            if (playerTurnPrompt != null && playerTurnPrompt.lines != null && playerTurnPrompt.lines.Count > 0)
                dialogueUI.PlaySequence(playerTurnPrompt);
            else
                dialogueUI.ShowRuntimeLine("What do you choose to do?");
        }
    }

    private IEnumerator ItemRoutine(ItemData item)
    {
        if (dialogueUI != null) dialogueUI.ClearText();

        if (item == null)
        {
            if (dialogueUI != null) dialogueUI.ShowRuntimeLine("Item was null.");
            yield return WaitForAdvanceKey();
            if (_gameOverShown) yield break;

            EnterPlayerTurn();
            yield break;
        }

        if (player.HP >= player.maxHP)
        {
            if (dialogueUI != null) dialogueUI.ShowRuntimeLine("Your HP is already full. The item had no effect.");
            yield return WaitForAdvanceKey();
            if (_gameOverShown) yield break;

            EnterPlayerTurn();
            yield break;
        }

        player.UseItem(item);
        if (dialogueUI != null) dialogueUI.ShowRuntimeLine($"You used {item.itemName}!");

        yield return WaitForAdvanceKey();
        if (_gameOverShown) yield break;

        _allowEnemyTurn = true;
        StartEnemyTurn();
    }

    // ---------------- ENEMY TURN ----------------
    private void StartEnemyTurn()
    {
        if (_gameOverShown) return;

        // ✅ Gate: if something tries to start enemy turn early, queue it
        if (!_allowEnemyTurn)
        {
            _queuedEnemyTurn = true;
            return;
        }

        _queuedEnemyTurn = false;
        _allowEnemyTurn = false;

        state = BattleState.EnemyTurn;
        ui.HideMenus();
        if (dialogueUI != null) dialogueUI.ClearText();

        var attack = enemyAttackSystem.ChooseAttack(enemyData);
        if (attack == null) { EnterPlayerTurn(); return; }

        enemyAttackSystem.PlayAttack(attack, player, () =>
        {
            if (_gameOverShown) return;

            if (player.HP <= 0) ExitCombat(false);
            else EnterPlayerTurn();
        });
    }

    // ---------------- ACT RESOLUTION ----------------
    private void ResolveAct(ActOptionData act)
    {
        if (act == null)
        {
            if (dialogueUI != null) dialogueUI.ShowRuntimeLine("ACT was null.");
            return;
        }

        if (dialogueUI != null) dialogueUI.ShowRuntimeLine(act.dialogueText);

        if (act.customHandler != null) act.customHandler.Execute(this, player, enemyData);
        else
        {
            switch (act.effectType)
            {
                case ActEffectType.HealPlayer: player.Heal(act.value); break;
                case ActEffectType.DebuffEnemy: enemyAttackSystem.ApplyDebuff(act.value); break;
                case ActEffectType.SpareProgress: enemyAttackSystem.AddSpareProgress(act.value); break;
            }
        }
    }

    // ---------------- WIN ROUTINE ----------------
    private IEnumerator WinRoutine()
    {
        if (_gameOverShown) yield break;

        state = BattleState.Won;
        ui.HideMenus();

        // Optional faint animation (shake → fall → fade)
        if (enemyFaintEffect != null)
            yield return enemyFaintEffect.PlayFaint();

        string enemyName = (enemyData != null) ? enemyData.name : "Enemy";

        if (dialogueUI != null)
            dialogueUI.ShowRuntimeLine($"{enemyName} fainted. YOU WIN!");

        yield return WaitForAdvanceKey();
        if (_gameOverShown) yield break;

        ExitCombat(true);
    }

    // ---------------- EXIT COMBAT ----------------
    private void ExitCombat(bool won)
    {
        if (_gameOverShown) return;

        state = won ? BattleState.Won : BattleState.Lost;
        ui.HideMenus();
        if (dialogueUI != null) dialogueUI.ClearText();

        if (!won)
        {
            ShowGameOver();
            return;
        }

        BattleReturnData.comingFromBattle = true;
        BattleReturnData.shouldReturnToWorld = true;

        if (BattleEntryData.hasEntry && !string.IsNullOrEmpty(BattleEntryData.returnTag))
            BattleReturnData.returnTag = BattleEntryData.returnTag;

        string returnScene =
            (BattleEntryData.hasEntry && !string.IsNullOrEmpty(BattleEntryData.returnScene))
                ? BattleEntryData.returnScene
                : BattleReturnData.worldSceneName;

        if (string.IsNullOrEmpty(returnScene))
        {
            Debug.LogError("[BattleController] No return scene set. Cannot exit.");
            return;
        }

        BattleEntryData.Clear();
        Debug.Log($"[BattleController] ExitCombat(won): loading return scene '{returnScene}', returnTag='{BattleReturnData.returnTag}'");

        BattleReturnData.ReturnToWorld(returnScene);
    }

    // ---------------- GAME OVER ----------------
    private void ShowGameOver()
    {
        _gameOverShown = true;
        state = BattleState.Lost;

        ui.HideMenus();
        if (dialogueUI != null) dialogueUI.ClearText();
        if (actMenuUI != null) actMenuUI.Hide();
        if (itemMenuUI != null) itemMenuUI.Hide();

        if (pauseTimeOnGameOver)
            Time.timeScale = 0f;

        if (gameOverUIRoot != null)
            gameOverUIRoot.SetActive(true);
        else
            Debug.LogError("[BattleController] GameOver UI Root not assigned.");
    }

    // =========================================================
    // ✅ FIXED SCENE LOADING (use SceneTransition first)
    // =========================================================
    private void LoadSceneSafe(string sceneName, bool whiteFade = false)
    {
        Time.timeScale = 1f;
        Physics.SyncTransforms();

        BattleEntryData.Clear();
        BattleReturnData.comingFromBattle = false;
        BattleReturnData.shouldReturnToWorld = false;
        BattleReturnData.returnTag = "";

        if (string.IsNullOrEmpty(sceneName))
        {
            Debug.LogError("[BattleController] LoadSceneSafe called with EMPTY sceneName.");
            return;
        }

        if (SceneTransition.Instance != null)
        {
            if (whiteFade) SceneTransition.Instance.LoadSceneWhite(sceneName);
            else SceneTransition.Instance.LoadScene(sceneName);
        }
        else
        {
            SceneManager.LoadScene(sceneName);
        }
    }

    public void ResetBattle()
    {
        string current = SceneManager.GetActiveScene().name;
        LoadSceneSafe(current, whiteFade: false);
    }

    public void ReturnToMainMenu()
    {
        if (string.IsNullOrEmpty(mainMenuSceneName))
            mainMenuSceneName = "01_MainMenu";

        LoadSceneSafe(mainMenuSceneName, whiteFade: false);
    }
}