using System;
using UnityEngine;
using UnityEngine.AI;

public class TransformStateSaveable : MonoBehaviour, ISaveable
{
    [Serializable]
    private struct Data
    {
        public float px, py, pz;
        public float rx, ry, rz;
    }

    [Header("What to Save")]
    [Tooltip("If true, saves LOCAL position/rotation (relative to parent). Otherwise saves WORLD.")]
    public bool useLocal = false;

    // ✅ NEW: cache last restore pose (useful if visuals/root toggled on/off)
    private Vector3 _lastRestoredPos;
    private Quaternion _lastRestoredRot;
    private bool _hasLastRestore;

    public string CaptureState()
    {
        Vector3 pos = useLocal ? transform.localPosition : transform.position;
        Vector3 eul = useLocal ? transform.localEulerAngles : transform.eulerAngles;

        var d = new Data
        {
            px = pos.x,
            py = pos.y,
            pz = pos.z,
            rx = eul.x,
            ry = eul.y,
            rz = eul.z
        };

        return JsonUtility.ToJson(d);
    }

    public void RestoreState(string state)
    {
        if (string.IsNullOrEmpty(state)) return;

        var d = JsonUtility.FromJson<Data>(state);

        Vector3 pos = new Vector3(d.px, d.py, d.pz);
        Quaternion rot = Quaternion.Euler(d.rx, d.ry, d.rz);

        _lastRestoredPos = pos;
        _lastRestoredRot = rot;
        _hasLastRestore = true;

        if (useLocal)
        {
            transform.localPosition = pos;
            transform.localRotation = rot;
            return;
        }

        // ✅ WORLD restore (safe for agents/rigidbodies/controllers)
        SafeSetPose(pos, rot);
    }

    // ---- NEW: helper (does not remove any existing function) ----
    private void SafeSetPose(Vector3 position, Quaternion rotation)
    {
        // NavMeshAgent needs Warp or it may snap back
        var agent = GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled)
        {
            agent.Warp(position);
            transform.rotation = rotation;
            return;
        }

        // Rigidbody (physics-safe)
        var rb = GetComponent<Rigidbody>();
        if (rb != null && !rb.isKinematic)
        {
            rb.MovePosition(position);
            rb.MoveRotation(rotation);
            rb.WakeUp();
            return;
        }

        // CharacterController needs disable/enable to prevent collision warping
        var cc = GetComponent<CharacterController>();
        if (cc != null)
        {
            bool wasEnabled = cc.enabled;
            try { cc.enabled = false; } catch { }
            transform.SetPositionAndRotation(position, rotation);
            try { cc.enabled = wasEnabled; } catch { }
            return;
        }

        // Default
        transform.SetPositionAndRotation(position, rotation);
    }

    // ✅ NEW: if object was disabled/enabled by scene flow, re-assert last restore
    private void OnEnable()
    {
        if (_hasLastRestore && !useLocal)
        {
            SafeSetPose(_lastRestoredPos, _lastRestoredRot);
        }
    }
}
