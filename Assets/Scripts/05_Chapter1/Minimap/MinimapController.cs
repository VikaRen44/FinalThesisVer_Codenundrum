using UnityEngine;
using TMPro;

public class MinimapController : MonoBehaviour
{
    public static MinimapController Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Parent RectTransform containing Floor_1, Floor_2, Floor_3 ...")]
    public RectTransform floorsRoot;

    [Tooltip("The player icon RectTransform, anchored at the center of the minimap.")]
    public RectTransform playerIcon;

    [Tooltip("TMP text that shows the current floor number.")]
    public TextMeshProUGUI floorLabel;

    [Header("Follow Settings")]
    [Tooltip("Higher = snappier pan; Lower = smoother.")]
    public float panLerp = 15f;

    [Tooltip("Clamp for how much the floor can move per second (UI units).")]
    public float maxPanPerFrame = 1000f;

    [Header("Detection (optional)")]
    [Tooltip("If set, SyncNow() will search only these layers for room triggers.")]
    public LayerMask minimapTriggerLayer = ~0;

    // expose current room (read-only)
    public MinimapRoom CurrentRoom => _currentRoom;

    // internals
    private MinimapRoom _currentRoom;
    private RectTransform _activeFloor;
    private Transform _player;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Update()
    {
        if (_currentRoom == null || _player == null || _activeFloor == null) return;

        // Desired UI position of player (within the active floor's local space)
        Vector2 playerUI = _currentRoom.WorldToUI(_player.position);

        // PlayerIcon stays centered; move the floor so playerUI aligns with icon
        Vector2 iconInFloorSpace = WorldToLocal(_activeFloor, playerIcon.position);
        Vector2 neededOffset = iconInFloorSpace - playerUI;

        Vector2 target = _activeFloor.anchoredPosition + neededOffset;
        Vector2 newPos = Vector2.Lerp(
            _activeFloor.anchoredPosition,
            target,
            1f - Mathf.Exp(-panLerp * Time.unscaledDeltaTime)
        );

        Vector2 delta = newPos - _activeFloor.anchoredPosition;
        delta = Vector2.ClampMagnitude(delta, maxPanPerFrame * Time.unscaledDeltaTime);
        _activeFloor.anchoredPosition += delta;
    }

    public void EnterRoom(MinimapRoom room, Transform player)
    {
        if (room == null)
        {
            Debug.LogWarning("MinimapController.EnterRoom called with null room.");
            return;
        }
        if (_currentRoom == room && _player == player) return; // idempotent

        // unhighlight old
        if (_currentRoom) _currentRoom.SetActiveVisual(false);

        _currentRoom = room;
        _player = player;

        // enable only the active floor
        if (floorsRoot != null)
        {
            for (int i = 0; i < floorsRoot.childCount; i++)
                floorsRoot.GetChild(i).gameObject.SetActive(false);
        }

        _activeFloor = room.floorContainer;
        if (_activeFloor != null)
            _activeFloor.gameObject.SetActive(true);

        _currentRoom.SetActiveVisual(true);

        if (floorLabel)
            floorLabel.text = $"FLOOR: {room.floorIndex}";

        // hard snap once on entry so it feels instant
        if (_activeFloor != null)
        {
            Vector2 playerUI = _currentRoom.WorldToUI(_player.position);
            Vector2 iconInFloor = WorldToLocal(_activeFloor, playerIcon.position);
            _activeFloor.anchoredPosition += (iconInFloor - playerUI);
        }
    }

    /// Force detection when you teleport/spawn (call right after moving the player).
    public void SyncNow(Transform player)
    {
        if (!player) return;
        _player = player;

        // Look for any trigger you're currently inside
        Collider[] hits = Physics.OverlapSphere(
            player.position,
            0.05f,
            minimapTriggerLayer,
            QueryTriggerInteraction.Collide
        );

        foreach (var h in hits)
        {
            var rz = h.GetComponentInParent<RoomZone>();
            if (rz != null && rz.minimapRoom != null)
            {
                EnterRoom(rz.minimapRoom, player);
                return;
            }
        }

        // Optional: log if nothing found
        // Debug.LogWarning("Minimap SyncNow: No RoomZone found at player position.");
    }

    private static Vector2 WorldToLocal(RectTransform target, Vector3 worldPoint)
    {
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            target,
            RectTransformUtility.WorldToScreenPoint(null, worldPoint),
            null,
            out local
        );
        return local;
    }

    // … keep your existing code …

    public void ShowFloor(int floorIndex)
    {
        if (floorsRoot == null) return;

        for (int i = 0; i < floorsRoot.childCount; i++)
            floorsRoot.GetChild(i).gameObject.SetActive(false);

        for (int i = 0; i < floorsRoot.childCount; i++)
        {
            var child = floorsRoot.GetChild(i).GetComponent<RectTransform>();
            if (!child) continue;

            // simple match: "Floor_1", "Floor_2", etc. Adjust to your naming.
            if (child.gameObject.name.Contains(floorIndex.ToString()))
            {
                child.gameObject.SetActive(true);
                break;
            }
        }
    }

}
