using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class HostRoomTrigger : MonoBehaviour
{
    // All room triggers in the scene
    public static readonly List<HostRoomTrigger> All = new List<HostRoomTrigger>();

    public bool PlayerInside { get; private set; }
    public bool HostInside { get; private set; }

    Collider _col;
    Transform _player;

    void Awake()
    {
        _col = GetComponent<Collider>();
        if (_col) _col.isTrigger = true;

        // Register even if disabled later
        Register();
    }

    void OnEnable()
    {
        Register();

        // When re-enabled, start from a safe state
        PlayerInside = false;
        HostInside = false;

        HostRoomAutoCuller.UpdateVisibilityStatic();
    }

    void OnDisable()
    {
        // IMPORTANT: if a room gets culled (SetActive(false)),
        // we must not leave stale "inside" flags behind.
        PlayerInside = false;
        HostInside = false;

        // Also remove from All so the culler won't read stale values.
        Unregister();

        HostRoomAutoCuller.UpdateVisibilityStatic();
    }

    void OnDestroy()
    {
        Unregister();
        HostRoomAutoCuller.UpdateVisibilityStatic();
    }

    private void Register()
    {
        if (!All.Contains(this))
            All.Add(this);
    }

    private void Unregister()
    {
        All.Remove(this);
    }

    void Update()
    {
        if (_col == null) return;

        // Lazy-find player
        if (_player == null)
        {
            var pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj) _player = pObj.transform;
        }

        // Get host root from the auto-culler
        Transform host = null;
        if (HostRoomAutoCuller.Instance != null)
            host = HostRoomAutoCuller.Instance.hostRoot;

        bool newPlayerInside = false;
        bool newHostInside = false;

        // Check bounds containment
        if (_player != null)
            newPlayerInside = _col.bounds.Contains(_player.position);

        if (host != null)
            newHostInside = _col.bounds.Contains(host.position);

        if (newPlayerInside != PlayerInside || newHostInside != HostInside)
        {
            PlayerInside = newPlayerInside;
            HostInside = newHostInside;
            HostRoomAutoCuller.UpdateVisibilityStatic();
        }
    }
}