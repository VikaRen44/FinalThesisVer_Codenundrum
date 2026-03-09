using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class HostCutsceneMove : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 8f;

    [Header("Animator")]
    public Animator animator;

    [Tooltip("State name inside CutsceneMove sub-state machine.")]
    public string cutsceneMoveStateName = "HostWalk";

    [Tooltip("Idle state name (THIS must match the actual state name, e.g. 'Angry').")]
    public string idleStateName = "Angry";

    [Tooltip("Idle sub-state machine name in Base Layer.")]
    public string idleSubStateMachineName = "IdleGroup";

    [Tooltip("Cutscene sub-state machine name in Base Layer.")]
    public string cutsceneSubStateMachineName = "CutsceneMove";

    [Header("Animator Params (optional)")]
    [Tooltip("Optional bool used by some controllers. Your real param is 'InCutscen' (no e). This script DOES NOT depend on it.")]
    public string inCutsceneBoolParam = "InCutscen";

    [Header("Speed Param (recommended)")]
    [Tooltip("Float parameter used by your controller transitions (your Base Layer uses Speed).")]
    public string speedParam = "Speed";
    public float walkSpeedValue = 1f;

    [Header("Arrive")]
    public float arriveDistance = 0.1f;

    [Header("Cutscene Control Safety")]
    public bool stopNavMeshAgent = true;

    [Tooltip("Any scripts/components you want disabled while moving (drag them here).")]
    public List<Behaviour> disableWhileMoving = new List<Behaviour>();

    [Header("Idle Snap Safety")]
    [Tooltip("How many frames we keep re-forcing idle after movement ends.")]
    public int forceIdleFrames = 6;

    private NavMeshAgent _agent;
    private int _speedHash;

    // cached param existence
    private bool _hasSpeedParam;
    private bool _hasInCutsceneBool;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (animator == null)
        {
            Debug.LogError("[HostCutsceneMove] No Animator found.");
            return;
        }

        _speedHash = !string.IsNullOrEmpty(speedParam) ? Animator.StringToHash(speedParam) : 0;

        // Cache whether params exist (prevents “Parameter does not exist” + avoids no-op writes)
        _hasSpeedParam = (_speedHash != 0) && HasParam(animator, _speedHash, AnimatorControllerParameterType.Float);
        _hasInCutsceneBool = !string.IsNullOrEmpty(inCutsceneBoolParam)
                             && HasParam(animator, Animator.StringToHash(inCutsceneBoolParam), AnimatorControllerParameterType.Bool);
    }

    public IEnumerator MoveAlongPath(List<Vector3> points)
    {
        if (animator == null) yield break;
        if (points == null || points.Count == 0) yield break;

        SetDisabledWhileMoving(true);

        // ✅ ENTER “moving”: set Speed > 0 first (matches your Animator transitions)
        SetSpeed(walkSpeedValue);

        // Optional: set bool if you have it (NOT required for your current controller)
        SetInCutsceneBool(true);

        // ✅ Play walk state safely (try full path first, then fallback)
        TryCrossFadeSmart(cutsceneMoveStateName, cutsceneSubStateMachineName, 0.05f);

        if (_agent != null && _agent.enabled)
            yield return MoveWithNavMesh(points);
        else
            yield return MoveManual(points);

        // ✅ STOP movement signals
        if (_agent != null && _agent.enabled && stopNavMeshAgent)
        {
            try { _agent.isStopped = true; _agent.ResetPath(); } catch { }
        }

        // ✅ EXIT “moving”: Speed = 0 is the key for your current Base Layer return transition
        SetSpeed(0f);
        SetInCutsceneBool(false);

        // ✅ Force idle NOW (try full path, then plain name)
        TryCrossFadeSmart(idleStateName, idleSubStateMachineName, 0.05f);

        // ✅ Safety: repeat for a few frames (beats other scripts)
        if (forceIdleFrames > 0)
        {
            for (int i = 0; i < forceIdleFrames; i++)
            {
                SetSpeed(0f);
                SetInCutsceneBool(false);
                TryCrossFadeSmart(idleStateName, idleSubStateMachineName, 0.05f);
                yield return null;
            }
        }

        SetDisabledWhileMoving(false);
    }

    private IEnumerator MoveWithNavMesh(List<Vector3> points)
    {
        float prevSpeed = _agent.speed;
        _agent.speed = moveSpeed;

        if (stopNavMeshAgent)
            _agent.isStopped = false;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 target = points[i];
            _agent.SetDestination(target);

            while (true)
            {
                Vector3 vel = _agent.velocity;
                vel.y = 0f;

                if (vel.sqrMagnitude > 0.0001f)
                {
                    Quaternion look = Quaternion.LookRotation(vel.normalized);
                    transform.rotation = Quaternion.Slerp(transform.rotation, look, rotationSpeed * Time.deltaTime);
                }

                // keep Speed > 0 while moving (optional but helps if agent slows)
                if (_agent.velocity.magnitude > 0.05f)
                    SetSpeed(walkSpeedValue);

                if (!_agent.pathPending && _agent.remainingDistance <= Mathf.Max(arriveDistance, _agent.stoppingDistance))
                    break;

                yield return null;
            }
        }

        _agent.speed = prevSpeed;
    }

    private IEnumerator MoveManual(List<Vector3> points)
    {
        float arriveSqr = arriveDistance * arriveDistance;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 target = points[i];

            while ((transform.position - target).sqrMagnitude > arriveSqr)
            {
                Vector3 dir = target - transform.position;
                dir.y = 0f;

                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion look = Quaternion.LookRotation(dir.normalized);
                    transform.rotation = Quaternion.Slerp(transform.rotation, look, rotationSpeed * Time.deltaTime);
                }

                transform.position = Vector3.MoveTowards(transform.position, target, moveSpeed * Time.deltaTime);

                SetSpeed(walkSpeedValue);
                yield return null;
            }
        }
    }

    // -------------------------
    // Helpers
    // -------------------------

    private void SetSpeed(float v)
    {
        if (animator == null) return;
        if (!_hasSpeedParam) return;
        animator.SetFloat(_speedHash, v);
    }

    private void SetInCutsceneBool(bool v)
    {
        if (animator == null) return;
        if (!_hasInCutsceneBool) return;
        animator.SetBool(inCutsceneBoolParam, v);
    }

    private bool TryCrossFadeSmart(string stateName, string subMachineNameOrNull, float fade)
    {
        if (animator == null) return false;
        if (string.IsNullOrEmpty(stateName)) return false;

        // Try full path: Base Layer.SubMachine.State
        if (!string.IsNullOrEmpty(subMachineNameOrNull))
        {
            string full = $"Base Layer.{subMachineNameOrNull}.{stateName}";
            int fullHash = Animator.StringToHash(full);
            if (animator.HasState(0, fullHash))
            {
                animator.CrossFadeInFixedTime(fullHash, fade, 0);
                return true;
            }
        }

        // Try plain state name (works if it exists in current machine)
        int directHash = Animator.StringToHash(stateName);
        if (animator.HasState(0, directHash))
        {
            animator.CrossFadeInFixedTime(directHash, fade, 0);
            return true;
        }

        // Last fallback: Base Layer.State (rare)
        string baseOnly = $"Base Layer.{stateName}";
        int baseHash = Animator.StringToHash(baseOnly);
        if (animator.HasState(0, baseHash))
        {
            animator.CrossFadeInFixedTime(baseHash, fade, 0);
            return true;
        }

        // If we fail, do nothing (prevents “stuck” from repeatedly trying invalid states)
        return false;
    }

    private void SetDisabledWhileMoving(bool disable)
    {
        if (disableWhileMoving == null) return;
        for (int i = 0; i < disableWhileMoving.Count; i++)
        {
            var b = disableWhileMoving[i];
            if (b == null) continue;
            b.enabled = !disable;
        }
    }

    private bool HasParam(Animator a, int nameHash, AnimatorControllerParameterType type)
    {
        var ps = a.parameters;
        for (int i = 0; i < ps.Length; i++)
        {
            if (ps[i].nameHash == nameHash && ps[i].type == type)
                return true;
        }
        return false;
    }
}
