using System;
using System.Reflection;
using UnityEngine;

/// Shows the host only when the Player and Host share at least one HostRoomTrigger.
/// Does NOT use minimap or RoomZone at all.
[DefaultExecutionOrder(1000)]
public class HostRoomAutoCuller : MonoBehaviour
{
    public static HostRoomAutoCuller Instance { get; private set; }

    [Header("Visual")]
    [Tooltip("Root object to show/hide (recommended: a CHILD visual root, not the SaveID root).")]
    public GameObject visualRoot;

    [Header("Host Root (optional)")]
    [Tooltip("Transform used as the host position. If empty, uses this transform.")]
    public Transform hostRoot;

    [Header("Safety")]
    [Tooltip("If true, tries to avoid disabling the SaveID root by auto-picking a child visual.")]
    public bool preventDisablingSaveRoot = true;

    [Header("Fallback Behavior")]
    [Tooltip("If HostRoomTrigger script/type is missing, should host stay visible? (Recommended: true)")]
    public bool defaultVisibleIfTriggerMissing = true;

    private void Awake()
    {
        Instance = this;

        if (!hostRoot)
            hostRoot = transform;

        // ✅ Safer default: if not assigned, try to auto-find a CHILD visual
        if (!visualRoot)
        {
            visualRoot = AutoFindVisualRootChild();
            if (!visualRoot)
                visualRoot = gameObject; // last resort fallback
        }

        // ✅ If visualRoot is the same as the save root, try to fix it
        if (preventDisablingSaveRoot && visualRoot == gameObject)
        {
            var alt = AutoFindVisualRootChild();
            if (alt != null) visualRoot = alt;
        }

        // Start visible by default (we’ll immediately correct it)
        if (visualRoot)
            visualRoot.SetActive(true);

        UpdateVisibility();
    }

    private void OnEnable()
    {
        // ✅ Important in your setup: scenes/rooms may disable objects (room culling)
        // so when this comes back, force a recompute.
        if (Instance == null) Instance = this;
        UpdateVisibility();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// Called by HostRoomTrigger whenever its inside flags change.
    public static void UpdateVisibilityStatic()
    {
        if (Instance != null)
            Instance.UpdateVisibility();
    }

    private void UpdateVisibility()
    {
        if (!visualRoot) return;

        // ✅ Reflection-based support:
        // - Works if HostRoomTrigger.All is a FIELD (your current code)
        // - Works if it's a PROPERTY (older variants)
        bool? shared = TryComputeSharedRoomViaHostRoomTriggerReflection();
        if (shared.HasValue)
        {
            // Only set if different (avoids spam + animator hiccups)
            bool wantActive = shared.Value;
            if (visualRoot.activeSelf != wantActive)
                visualRoot.SetActive(wantActive);
            return;
        }

        // Fallback if trigger system isn't present / discoverable
        if (visualRoot.activeSelf != defaultVisibleIfTriggerMissing)
            visualRoot.SetActive(defaultVisibleIfTriggerMissing);
    }

    // ✅ Find child visuals safely (no hierarchy changes required)
    private GameObject AutoFindVisualRootChild()
    {
        // Prefer animator root
        var anim = GetComponentInChildren<Animator>(true);
        if (anim != null && anim.gameObject != gameObject)
            return anim.gameObject;

        // Fallback to first renderer root
        var rend = GetComponentInChildren<Renderer>(true);
        if (rend != null && rend.gameObject != gameObject)
            return rend.gameObject;

        return null;
    }

    // ✅ Reflection-based HostRoomTrigger support
    // IMPORTANT FIX: supports All being FIELD or PROPERTY.
    private bool? TryComputeSharedRoomViaHostRoomTriggerReflection()
    {
        // Find the HostRoomTrigger type (handles non-namespaced case; best-effort for namespaced too)
        var t = FindTypeByName("HostRoomTrigger");
        if (t == null) return null;

        // Get All (FIELD or PROPERTY)
        object allObj = GetStaticAllCollection(t);
        if (allObj == null) return null;

        var enumerable = allObj as System.Collections.IEnumerable;
        if (enumerable == null) return null;

        // Expecting bool PlayerInside / HostInside properties
        var pInsideProp = t.GetProperty("PlayerInside", BindingFlags.Public | BindingFlags.Instance);
        var hInsideProp = t.GetProperty("HostInside", BindingFlags.Public | BindingFlags.Instance);
        if (pInsideProp == null || hInsideProp == null) return null;

        foreach (var zone in enumerable)
        {
            if (zone == null) continue;

            bool pi = false, hi = false;
            try { pi = (bool)pInsideProp.GetValue(zone, null); } catch { }
            try { hi = (bool)hInsideProp.GetValue(zone, null); } catch { }

            if (pi && hi)
                return true;
        }

        return false;
    }

    private static Type FindTypeByName(string typeName)
    {
        // Fast path (sometimes works depending on assembly qualification)
        var t = Type.GetType(typeName);
        if (t != null) return t;

        // Scan loaded assemblies (robust)
        var asms = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < asms.Length; i++)
        {
            Type found = null;
            try
            {
                // Try exact name first
                found = asms[i].GetType(typeName);
                if (found != null) return found;

                // If namespaced, search by simple name
                var types = asms[i].GetTypes();
                for (int k = 0; k < types.Length; k++)
                {
                    if (types[k] != null && types[k].Name == typeName)
                        return types[k];
                }
            }
            catch
            {
                // ignore reflection errors
            }
        }

        return null;
    }

    private static object GetStaticAllCollection(Type hostRoomTriggerType)
    {
        // Case A: public static List<HostRoomTrigger> All { get; }
        var allProp = hostRoomTriggerType.GetProperty("All", BindingFlags.Public | BindingFlags.Static);
        if (allProp != null)
        {
            try { return allProp.GetValue(null, null); }
            catch { }
        }

        // Case B: public static readonly List<HostRoomTrigger> All = ...
        var allField = hostRoomTriggerType.GetField("All", BindingFlags.Public | BindingFlags.Static);
        if (allField != null)
        {
            try { return allField.GetValue(null); }
            catch { }
        }

        return null;
    }
}
