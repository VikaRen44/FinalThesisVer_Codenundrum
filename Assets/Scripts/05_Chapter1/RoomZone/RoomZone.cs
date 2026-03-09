using UnityEngine;

[RequireComponent(typeof(Collider))]
public class RoomZone : MonoBehaviour
{
    [Tooltip("The matching MinimapRoom on the minimap plane.")]
    public MinimapRoom minimapRoom;

    void Reset()
    {
        var col = GetComponent<Collider>();
        if (col) col.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        var mc = MinimapController.Instance;
        if (mc != null)
            mc.EnterRoom(minimapRoom, other.transform);
    }

    void OnTriggerStay(Collider other)
    {
        if (!other.CompareTag("Player")) return;

        var mc = MinimapController.Instance;
        if (mc != null && mc.CurrentRoom != minimapRoom)
            mc.EnterRoom(minimapRoom, other.transform);
    }
}
