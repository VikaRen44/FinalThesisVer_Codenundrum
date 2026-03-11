using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Collider))]
public class TeleportTrigger : MonoBehaviour
{
    [Header("Teleport")]
    [SerializeField] private Transform destination;
    [SerializeField] private bool matchDestinationRotation = true;
    [SerializeField] private float cooldownAfterTeleport = 0.25f;

    [Header("Fade UI (local to this trigger)")]
    [Tooltip("CanvasGroup that controls the fade overlay alpha + input blocking.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Tooltip("Optional. The overlay Image (usually full-screen black). Not required if CanvasGroup is assigned.")]
    [SerializeField] private Image fadeImage;

    [Header("Fade Timing")]
    [SerializeField] private float fadeOutDuration = 0.25f;
    [SerializeField] private float fadeInDuration = 0.25f;

    [Header("Behavior")]
    [SerializeField] private bool blockInputDuringFade = true;

    [Tooltip("If true, tries to activate the destination RoomCullingZone before teleporting.")]
    [SerializeField] private bool preloadDestinationRoom = true;

    [Tooltip("Extra wait after preloading the room so colliders/floor are active before the player is moved.")]
    [SerializeField] private float roomPreloadWait = 0.08f;

    [Tooltip("Additional frames to wait after destination room activation.")]
    [SerializeField] private int extraWarmupFrames = 1;

    [Tooltip("Optional upward offset to avoid spawning slightly inside the floor.")]
    [SerializeField] private float landingOffsetY = 0.05f;

    [Tooltip("If true, tries to resolve room culling again after teleport.")]
    [SerializeField] private bool reResolveRoomAfterTeleport = true;

    [Tooltip("How long to wait for camera/player settling before fading back in.")]
    [SerializeField] private float postTeleportCameraSettleTime = 0.15f;

    [Tooltip("Maximum wait for player to appear near camera center before fade-in.")]
    [SerializeField] private float maxCameraCenterWait = 0.75f;

    [Header("Debug")]
    [SerializeField] private bool verboseLogs = false;

    private bool _isTeleporting;
    private Collider _triggerCol;

    private void Awake()
    {
        _triggerCol = GetComponent<Collider>();
        if (_triggerCol != null)
            _triggerCol.isTrigger = true;

        // Auto-find if not assigned
        if (canvasGroup == null)
            canvasGroup = GetComponentInChildren<CanvasGroup>(true);

        if (fadeImage == null)
            fadeImage = GetComponentInChildren<Image>(true);

        // Start clear
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_isTeleporting)
            return;

        if (destination == null)
        {
            Debug.LogError("[TP] No destination set on " + name);
            return;
        }

        if (!other.CompareTag("Player"))
            return;

        TeleportState state = other.GetComponent<TeleportState>();

        if (state != null && state.IsOnCooldown)
        {
            if (verboseLogs)
                Debug.Log("[TP] Ignored trigger because player teleport cooldown is active.");
            return;
        }

        if (verboseLogs)
            Debug.Log("[TP] Triggered by Player, starting teleport sequence.");

        StartCoroutine(TeleportSequence(other.transform, state));
    }

    private IEnumerator TeleportSequence(Transform player, TeleportState state)
    {
        if (player == null)
            yield break;

        _isTeleporting = true;

        PlayerMovement mover = player.GetComponent<PlayerMovement>();
        CharacterController controller = player.GetComponent<CharacterController>();
        Rigidbody rb = player.GetComponent<Rigidbody>();

        Vector3 savedVelocity = Vector3.zero;
        Vector3 savedAngularVelocity = Vector3.zero;
        bool rbWasKinematic = false;

        // Lock movement
        if (mover != null)
            mover.canMove = false;

        if (rb != null)
        {
            savedVelocity = rb.linearVelocity;
            savedAngularVelocity = rb.angularVelocity;
            rbWasKinematic = rb.isKinematic;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        // Fade to black first
        yield return Fade(1f, fadeOutDuration);

        // PRELOAD destination room before moving player
        if (preloadDestinationRoom)
        {
            if (verboseLogs)
                Debug.Log("[TP] Attempting to preload destination room.");

            Physics.SyncTransforms();

            RoomCullingZone.ActivateZoneContainingPoint(destination.position);

            // Let activation settle
            if (roomPreloadWait > 0f)
                yield return new WaitForSecondsRealtime(roomPreloadWait);

            for (int i = 0; i < extraWarmupFrames; i++)
                yield return null;

            Physics.SyncTransforms();
        }

        // Disable CharacterController before changing transform
        if (controller != null)
            controller.enabled = false;

        // Teleport position
        Vector3 targetPos = destination.position + new Vector3(0f, landingOffsetY, 0f);
        player.position = targetPos;

        if (matchDestinationRotation)
            player.rotation = Quaternion.Euler(0f, destination.eulerAngles.y, 0f);

        Physics.SyncTransforms();

        // Re-resolve room after move as extra safety
        if (reResolveRoomAfterTeleport)
        {
            RoomCullingZone.ActivateZoneContainingPoint(player.position);
            Physics.SyncTransforms();
        }

        // Re-enable controller after environment is ready
        if (controller != null)
            controller.enabled = true;

        // If Rigidbody exists, re-enable safely
        if (rb != null)
        {
            rb.isKinematic = rbWasKinematic;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        // Give camera / systems time to catch up
        yield return null;
        yield return new WaitForEndOfFrame();

        if (postTeleportCameraSettleTime > 0f)
            yield return new WaitForSecondsRealtime(postTeleportCameraSettleTime);

        // Wait until player is roughly centered on screen, or timeout
        float t = 0f;
        while (t < maxCameraCenterWait)
        {
            Camera cam = Camera.main;
            if (cam != null)
            {
                Vector3 vp = cam.WorldToViewportPoint(player.position);
                bool onScreen = vp.z > 0f;
                bool nearCenter = Mathf.Abs(vp.x - 0.5f) < 0.08f && Mathf.Abs(vp.y - 0.5f) < 0.08f;

                if (onScreen && nearCenter)
                    break;
            }

            t += Time.unscaledDeltaTime;
            yield return null;
        }

        // Final room resolve one more time in case build timing is weird
        if (reResolveRoomAfterTeleport)
        {
            RoomCullingZone.ActivateZoneContainingPoint(player.position);
            Physics.SyncTransforms();
        }

        // Fade back in
        yield return Fade(0f, fadeInDuration);

        if (mover != null)
            mover.canMove = true;

        if (state != null)
            state.StartCooldown(cooldownAfterTeleport);

        _isTeleporting = false;

        if (verboseLogs)
            Debug.Log("[TP] Teleport complete.");
    }

    private IEnumerator Fade(float targetAlpha, float duration)
    {
        if (canvasGroup == null)
            yield break;

        if (blockInputDuringFade)
        {
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        float start = canvasGroup.alpha;
        float t = 0f;

        if (duration <= 0f)
        {
            canvasGroup.alpha = targetAlpha;
        }
        else
        {
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float p = Mathf.Clamp01(t / duration);
                canvasGroup.alpha = Mathf.Lerp(start, targetAlpha, p);
                yield return null;
            }

            canvasGroup.alpha = targetAlpha;
        }

        if (blockInputDuringFade && Mathf.Approximately(targetAlpha, 0f))
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
    }
}