using System.Collections;
using UnityEngine;
using UnityEngine.UI;

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

    private bool _isTeleporting;

    private void Awake()
    {
        // Auto-find if not assigned (looks in children, including inactive)
        if (canvasGroup == null)
            canvasGroup = GetComponentInChildren<CanvasGroup>(true);

        if (fadeImage == null)
            fadeImage = GetComponentInChildren<Image>(true);

        // Start "clear"
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_isTeleporting) return;

        if (!destination)
        {
            Debug.LogError("[TP] No destination set on " + name);
            return;
        }

        if (!other.CompareTag("Player")) return;

        var state = other.GetComponent<TeleportState>();


        Debug.Log("[TP] Triggered by Player, starting teleport sequence.");
        StartCoroutine(TeleportSequence(other.transform, state));
    }

    private IEnumerator TeleportSequence(Transform player, TeleportState state)
    {
        _isTeleporting = true;

        var mover = player.GetComponent<PlayerMovement>();
        var controller = player.GetComponent<CharacterController>();

        if (mover) mover.canMove = false;

        // Fade to black
        yield return Fade(1f, fadeOutDuration);

        // Teleport while black
        if (controller) controller.enabled = false;

        player.position = destination.position;

        if (matchDestinationRotation)
            player.rotation = Quaternion.Euler(0f, destination.eulerAngles.y, 0f);

        if (controller) controller.enabled = true;

        // Let camera catch up (min hold)
        yield return null;                     // one Update
        yield return new WaitForEndOfFrame();  // one LateUpdate (cameras often move here)
        yield return new WaitForSecondsRealtime(0.15f);

        // Wait until player is near screen center, or timeout
        float maxWait = 0.75f;   // tweak as needed
        float t = 0f;

        while (t < maxWait)
        {
            var cam = Camera.main;
            if (cam)
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

        // Fade back in
        yield return Fade(0f, fadeInDuration);

        if (mover) mover.canMove = true;
        if (state) state.StartCooldown(cooldownAfterTeleport);

        _isTeleporting = false;
    }

    private IEnumerator Fade(float targetAlpha, float duration)
    {
        if (canvasGroup == null)
        {
            // No fade setup; just proceed.
            yield break;
        }

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
                canvasGroup.alpha = Mathf.Lerp(start, targetAlpha, t / duration);
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

