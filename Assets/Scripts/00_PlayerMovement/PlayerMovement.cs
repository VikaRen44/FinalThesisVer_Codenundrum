using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float speed = 5f;
    public float sprintSpeed = 7f;
    public bool canMove = true;

    [Header("Sticky Ground Settings")]
    public LayerMask groundMask = ~0;
    public float gravity = -80f;
    public float groundedGravity = -8f;
    public float stickMultiplier = 3f;
    public float snapDistance = 0.6f;
    public float maxSnapPull = 12f;

    [Header("Input Actions (.inputactions)")]
    public InputActionReference moveAction;
    public InputActionReference sprintAction;

    [Header("Input Actions (Buttons) - OPTIONAL")]
    public InputActionReference interactAction;
    public InputActionReference openSettingsAction;

    [Header("Button Behavior - OPTIONAL")]
    public bool allowOpenSettingsWhenLocked = true;
    public bool allowInteractWhenLocked = false;

    [Header("Events - OPTIONAL")]
    public UnityEvent onInteractPressed;

    [Header("Facing / Visual")]
    public Transform visualRoot;
    public float visualYawOffset = -90f;
    public bool rotateRoot = true;
    public float movementYawOffset = 0f;
    public float turnSpeed = 12f;

    [Header("Stability")]
    public float inputDeadZone = 0.15f;
    public float rotateDeadZone = 0.05f;

    [Header("Animator (optional)")]
    public Animator animator;
    public string speedParam = "Speed";
    public string sprintParam = "IsSprinting";
    public string groundedParam = "IsGrounded";
    public float speedDampTime = 0.08f;

    [Header("Controller Safety (IMPORTANT)")]
    [Tooltip("If ON, the script will automatically re-enable the CharacterController if something disables it.")]
    public bool forceEnableController = true;

    [Tooltip("If ON, prints a warning (once) when controller is inactive/disabled.")]
    public bool logWhenControllerInactive = true;

    [Header("Optional External Systems (Safe)")]
    public bool lockWhileDialogueOrCutscene = true;
    public bool enableSettingsHotkey = true;
    public bool logMissingOptionalSystems = false;

    // ✅ NEW: Return/Load Stability Helpers
    [Header("Return/Load Stability (NEW)")]
    [Tooltip("If true, ForceSnapToGroundNow uses groundMask from this script.")]
    public bool allowGroundSnapHelpers = true;

    [Tooltip("Extra height above player to raycast from when snapping to ground.")]
    public float snapRayExtraHeight = 2.5f;

    [Tooltip("Small clearance above the ground when snapping (prevents micro-clipping).")]
    public float snapGroundClearance = 0.05f;

    // ---- private ----
    private CharacterController controller;
    private float verticalVel;
    private float _currentYaw;
    private int hSpeed, hSprint, hGrounded;

    private const float kIdleEpsilon = 0.005f;

    private readonly HashSet<int> _locks = new HashSet<int>();
    private bool _warnedInactiveController = false;

    // ✅ NEW: temporary freeze used by return systems
    private float _forcedFreezeUntilUnscaled = -1f;

    // -------- Reflection cache --------
    private bool _optionalCacheBuilt = false;

    private object _simpleDialogueInstance;
    private PropertyInfo _simpleDialogueInstanceProp;
    private PropertyInfo _simpleDialogueIsPlayingProp;

    private object _cutsceneInstance;
    private PropertyInfo _cutsceneInstanceProp;
    private PropertyInfo _cutsceneIsPlayingProp;

    private object _settingsInstance;
    private PropertyInfo _settingsInstanceProp;
    private MethodInfo _settingsToggleMethod;

    private int TokenId(object owner)
    {
        if (owner == null) return 0;
        if (owner is UnityEngine.Object uo) return uo.GetInstanceID();
        return owner.GetHashCode();
    }

    public void AddLock(object owner)
    {
        int id = TokenId(owner);
        if (id == 0) id = GetInstanceID();
        _locks.Add(id);
    }

    public void RemoveLock(object owner)
    {
        int id = TokenId(owner);
        if (id == 0) id = GetInstanceID();
        _locks.Remove(id);
    }

    public bool IsLockedByAnyone() => _locks.Count > 0;

    // ✅ NEW: Freeze movement for return/load safety
    public void FreezeForSeconds(float seconds)
    {
        _forcedFreezeUntilUnscaled = Time.unscaledTime + Mathf.Max(0f, seconds);
    }

    // ✅ NEW: callable from return/load systems to prevent clipping under map
    public bool ForceSnapToGroundNow()
    {
        if (!allowGroundSnapHelpers) return false;

        if (controller == null) controller = GetComponent<CharacterController>();
        if (controller == null) return false;

        // If the object is not active, don't try
        if (!gameObject.activeInHierarchy) return false;

        // Raycast from above the player downwards
        float castUp = Mathf.Max(0.5f, (controller.height * 0.5f) + snapRayExtraHeight);
        Vector3 origin = transform.position + Vector3.up * castUp;

        float maxDist = castUp + Mathf.Max(2f, controller.height + 3f);

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDist, groundMask, QueryTriggerInteraction.Ignore))
        {
            // Place CC so its bottom sits on the hit point (+ clearance)
            // bottomWorldY = posY + centerY - height/2
            // want bottomWorldY = hitY + clearance
            // => posY = hitY + clearance - centerY + height/2
            float targetY = hit.point.y + snapGroundClearance - controller.center.y + (controller.height * 0.5f);

            // Disable controller, set, enable (prevents CC pushing)
            bool wasEnabled = controller.enabled;
            controller.enabled = false;

            var p = transform.position;
            p.y = targetY;
            transform.position = p;

            controller.enabled = wasEnabled;

            // Reset vertical velocity so it doesn't instantly slam down
            verticalVel = groundedGravity;

            return true;
        }

        return false;
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        if (controller != null) controller.minMoveDistance = 0f;

        if (!animator && visualRoot) animator = visualRoot.GetComponentInChildren<Animator>(true);
        if (!animator) animator = GetComponentInChildren<Animator>(true);
        if (animator) animator.applyRootMotion = false;

        RefreshAnimatorHashes();

        _currentYaw = transform.rotation.eulerAngles.y;
        if (visualRoot) visualRoot.localRotation = Quaternion.Euler(0f, visualYawOffset, 0f);

        BuildOptionalSystemCache();
    }

    private void OnEnable()
    {
        if (moveAction) moveAction.action.Enable();
        if (sprintAction) sprintAction.action.Enable();
        if (interactAction) interactAction.action.Enable();
        if (openSettingsAction) openSettingsAction.action.Enable();
    }

    private void OnDisable()
    {
        if (moveAction) moveAction.action.Disable();
        if (sprintAction) sprintAction.action.Disable();
        if (interactAction) interactAction.action.Disable();
        if (openSettingsAction) openSettingsAction.action.Disable();
    }

    private void Update()
    {
        // Settings can be pressed even when movement is locked
        HandleOpenSettingsInput_Safe();

        // ✅ HARD SAFETY: Never call Move on inactive controller
        if (!EnsureControllerActive())
        {
            FeedAnimator(0f, false, false);
            ApplyVisualRotation();
            return;
        }

        // ✅ NEW: Forced freeze window (used by returns/loads)
        if (_forcedFreezeUntilUnscaled > 0f && Time.unscaledTime < _forcedFreezeUntilUnscaled)
        {
            FeedAnimator(0f, false, controller.isGrounded);
            ApplyVisualRotation();
            return;
        }

        bool locked = IsLockedByAnyone() || !canMove;
        if (lockWhileDialogueOrCutscene) locked |= IsDialogueOrCutsceneActive_Safe();

        HandleInteractInput(locked);

        if (locked)
        {
            FeedAnimator(0f, false, controller.isGrounded);
            ApplyVisualRotation();
            return;
        }

        Vector2 raw = (moveAction != null) ? moveAction.action.ReadValue<Vector2>() : Vector2.zero;
        Vector2 mv = (raw.magnitude < inputDeadZone) ? Vector2.zero : raw;

        bool sprinting = (sprintAction != null) && sprintAction.action.IsPressed();
        float currentSpeed = sprinting ? sprintSpeed : speed;

        Vector3 moveDir = new Vector3(mv.x, 0f, mv.y);

        if (Mathf.Abs(movementYawOffset) > 0.001f)
            moveDir = Quaternion.Euler(0f, movementYawOffset, 0f) * moveDir;

        if (moveDir.sqrMagnitude > 1e-4f) moveDir.Normalize();

        Vector3 horiz = moveDir * currentSpeed;

        if (controller.isGrounded)
        {
            if (verticalVel < groundedGravity)
                verticalVel = groundedGravity;
        }
        else
        {
            verticalVel += gravity * stickMultiplier * Time.deltaTime;

            float castHeight = controller.height * 0.5f + 0.2f;
            Vector3 nextFlat = transform.position + new Vector3(horiz.x, 0f, horiz.z) * Time.deltaTime;
            Vector3 origin = nextFlat + Vector3.up * castHeight;

            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit,
                                castHeight + snapDistance, groundMask,
                                QueryTriggerInteraction.Ignore))
            {
                verticalVel = Mathf.Max(verticalVel, -maxSnapPull);
            }
        }

        Vector3 velocity = new Vector3(horiz.x, verticalVel, horiz.z);

        // ✅ SAFE: controller guaranteed active here
        controller.Move(velocity * Time.deltaTime);

        if (controller.isGrounded && verticalVel < groundedGravity)
            verticalVel = groundedGravity;

        Vector3 planar = new Vector3(horiz.x, 0f, horiz.z);
        float planarSpeed = planar.magnitude;

        if (planarSpeed > rotateDeadZone)
        {
            float targetYaw = Mathf.Atan2(planar.x, planar.z) * Mathf.Rad2Deg;
            _currentYaw = Mathf.LerpAngle(_currentYaw, targetYaw, Mathf.Clamp01(turnSpeed * Time.deltaTime));

            if (rotateRoot)
                transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
        }

        ApplyVisualRotation();

        float normalized = Mathf.InverseLerp(0f, Mathf.Max(0.0001f, sprintSpeed), planarSpeed);
        if (planarSpeed < kIdleEpsilon) normalized = 0f;

        FeedAnimator(normalized, sprinting, controller.isGrounded);
    }

    // =========================================================
    // CONTROLLER SAFETY
    // =========================================================
    private bool EnsureControllerActive()
    {
        if (controller == null) controller = GetComponent<CharacterController>();
        if (controller == null) return false;

        // If parent is inactive, we cannot fix that here.
        if (!gameObject.activeInHierarchy) return false;

        // If someone disabled the CharacterController, optionally re-enable it.
        if (!controller.enabled)
        {
            if (forceEnableController)
            {
                controller.enabled = true;
            }
        }

        bool active = controller.enabled;

        if (!active && logWhenControllerInactive && !_warnedInactiveController)
        {
            Debug.LogWarning(
                "PlayerMovement: CharacterController is DISABLED, so movement is blocked. " +
                "Something in your scene is disabling the CharacterController or player root."
            );
            _warnedInactiveController = true;
        }

        if (active) _warnedInactiveController = false;
        return active;
    }

    // =========================================================
    // OPTIONAL SYSTEMS — SAFE (no compile-time dependencies)
    // =========================================================
    private void BuildOptionalSystemCache()
    {
        if (_optionalCacheBuilt) return;
        _optionalCacheBuilt = true;

        CacheSingleton("SimpleDialogueManager", out _simpleDialogueInstanceProp, out _simpleDialogueIsPlayingProp, out _simpleDialogueInstance);
        CacheSingleton("CutsceneSystem", out _cutsceneInstanceProp, out _cutsceneIsPlayingProp, out _cutsceneInstance);

        var settingsType = FindTypeByName("SettingsBootstrap");
        if (settingsType != null)
        {
            _settingsInstanceProp = settingsType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            _settingsToggleMethod = settingsType.GetMethod("ToggleSettingsUI", BindingFlags.Public | BindingFlags.Instance);

            if (_settingsInstanceProp != null)
                _settingsInstance = _settingsInstanceProp.GetValue(null);
        }
    }

    private void CacheSingleton(string typeName, out PropertyInfo instanceProp, out PropertyInfo isPlayingProp, out object instanceObj)
    {
        instanceProp = null;
        isPlayingProp = null;
        instanceObj = null;

        var t = FindTypeByName(typeName);
        if (t == null) return;

        instanceProp = t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        isPlayingProp = t.GetProperty("IsPlaying", BindingFlags.Public | BindingFlags.Instance);

        if (instanceProp != null)
            instanceObj = instanceProp.GetValue(null);
    }

    private Type FindTypeByName(string typeName)
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type[] types;
            try { types = assemblies[i].GetTypes(); }
            catch { continue; }

            for (int j = 0; j < types.Length; j++)
            {
                if (types[j].Name == typeName)
                    return types[j];
            }
        }
        return null;
    }

    private bool IsDialogueOrCutsceneActive_Safe()
    {
        BuildOptionalSystemCache();
        return ReadIsPlaying(_simpleDialogueInstanceProp, _simpleDialogueIsPlayingProp, ref _simpleDialogueInstance)
            || ReadIsPlaying(_cutsceneInstanceProp, _cutsceneIsPlayingProp, ref _cutsceneInstance);
    }

    private bool ReadIsPlaying(PropertyInfo instanceProp, PropertyInfo isPlayingProp, ref object instanceObj)
    {
        if (instanceProp == null || isPlayingProp == null) return false;

        if (instanceObj == null)
        {
            instanceObj = instanceProp.GetValue(null);
            if (instanceObj == null) return false;
        }

        try
        {
            object v = isPlayingProp.GetValue(instanceObj);
            return v is bool b && b;
        }
        catch { return false; }
    }

    private void HandleOpenSettingsInput_Safe()
    {
        if (!enableSettingsHotkey) return;
        if (openSettingsAction == null) return;

        if (!allowOpenSettingsWhenLocked && (IsLockedByAnyone() || !canMove))
            return;

        if (!openSettingsAction.action.WasPressedThisFrame())
            return;

        BuildOptionalSystemCache();

        if (_settingsInstance == null && _settingsInstanceProp != null)
            _settingsInstance = _settingsInstanceProp.GetValue(null);

        if (_settingsInstance != null && _settingsToggleMethod != null)
        {
            try { _settingsToggleMethod.Invoke(_settingsInstance, null); }
            catch { }
        }
    }

    private void HandleInteractInput(bool locked)
    {
        if (interactAction == null) return;
        if (locked && !allowInteractWhenLocked) return;

        if (interactAction.action.WasPressedThisFrame())
            onInteractPressed?.Invoke();
    }

    private void ApplyVisualRotation()
    {
        if (!visualRoot) return;
        Quaternion vis = Quaternion.Euler(0f, _currentYaw, 0f) * Quaternion.Euler(0f, visualYawOffset, 0f);
        visualRoot.rotation = vis;
    }

    private void FeedAnimator(float normalizedSpeed, bool sprinting, bool grounded)
    {
        if (!animator) return;

        if (!string.IsNullOrEmpty(speedParam))
            animator.SetFloat(hSpeed, normalizedSpeed, speedDampTime, Time.deltaTime);

        if (!string.IsNullOrEmpty(sprintParam))
            animator.SetBool(hSprint, sprinting && normalizedSpeed > kIdleEpsilon);

        if (!string.IsNullOrEmpty(groundedParam))
            animator.SetBool(hGrounded, grounded);
    }

    private void OnValidate() => RefreshAnimatorHashes();

    private void RefreshAnimatorHashes()
    {
        hSpeed = Animator.StringToHash(string.IsNullOrEmpty(speedParam) ? "Speed" : speedParam);
        hSprint = Animator.StringToHash(string.IsNullOrEmpty(sprintParam) ? "IsSprinting" : sprintParam);
        hGrounded = Animator.StringToHash(string.IsNullOrEmpty(groundedParam) ? "IsGrounded" : groundedParam);
    }
}
