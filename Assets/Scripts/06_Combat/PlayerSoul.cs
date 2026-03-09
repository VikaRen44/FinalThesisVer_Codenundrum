using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class PlayerSoul : MonoBehaviour
{
    [Header("Refs")]
    public PlayerStats player;      // will auto-find if left empty
    public Image soulImage;         // the red soul Image (UI)

    [Header("Hit & Invulnerability")]
    public float invulnDuration = 1.2f; // total time you can't be hit again
    public float hitFlashDuration = 0.3f;
    public int hitFlashCount = 4;
    public float shakeIntensity = 6f;

    [Header("SFX")]
    [Tooltip("AudioSource used for hit sounds. If empty, will try GetComponent<AudioSource>() then FindObjectOfType<AudioSource>().")]
    public AudioSource sfxSource;
    public AudioClip playerHitSfx;
    [Range(0f, 1f)] public float playerHitSfxVolume = 1f;

    private bool _invulnerable = false;
    private Color _originalColor = Color.white;
    private Coroutine _hitRoutine;

    private void Awake()
    {
        // Grab the Image automatically if not set
        if (soulImage == null)
            soulImage = GetComponent<Image>();

        if (soulImage != null)
            _originalColor = soulImage.color;

        // Auto-find PlayerStats if not assigned in inspector
        if (player == null)
            player = FindObjectOfType<PlayerStats>();

        if (player == null)
            Debug.LogWarning("[PlayerSoul] PlayerStats reference is missing. HP will not be reduced on hit.");

        // SFX source auto-find (non-destructive)
        if (sfxSource == null) sfxSource = GetComponent<AudioSource>();
        if (sfxSource == null) sfxSource = FindObjectOfType<AudioSource>(); // fallback (optional)
    }

    /// <summary>
    /// Called by bullets when they hit the soul.
    /// </summary>
    public void TakeHit(int damage)
    {
        if (_invulnerable) return;

        // Apply damage
        if (player != null)
        {
            player.TakeDamage(damage);
        }
        else
        {
            Debug.LogWarning("[PlayerSoul] TakeHit called but player is null.");
        }

        // ✅ Play SFX only when hit is accepted
        PlayPlayerHitSfx();

        if (_hitRoutine != null)
            StopCoroutine(_hitRoutine);

        _hitRoutine = StartCoroutine(HitRoutine());
    }

    private void PlayPlayerHitSfx()
    {
        if (playerHitSfx == null) return;
        if (sfxSource == null) return;

        sfxSource.PlayOneShot(playerHitSfx, playerHitSfxVolume);
    }

    /// <summary>
    /// Reset invulnerability and visuals (called when a new attack starts).
    /// </summary>
    public void ResetInvulnerability()
    {
        _invulnerable = false;

        if (_hitRoutine != null)
        {
            StopCoroutine(_hitRoutine);
            _hitRoutine = null;
        }

        if (soulImage != null)
        {
            soulImage.enabled = true;
            soulImage.color = _originalColor;
            // IMPORTANT: do NOT touch position here, let the movement script own it
        }
    }

    private IEnumerator HitRoutine()
    {
        if (soulImage == null)
        {
            // still respect invuln timing even if no image assigned
            _invulnerable = true;
            yield return new WaitForSeconds(invulnDuration);
            _invulnerable = false;
            _hitRoutine = null;
            yield break;
        }

        _invulnerable = true;

        RectTransform rt = soulImage.rectTransform;
        // Use the CURRENT position as the center of the shake
        Vector3 basePos = rt.anchoredPosition;

        // ------- HIT FLASH + SHAKE -------
        float totalTime = hitFlashDuration;
        int flashes = Mathf.Max(1, hitFlashCount);
        float step = totalTime / (flashes * 2f);

        Color flashColor = new Color(1f, 1f, 1f, 0.4f); // pale white flash

        for (int i = 0; i < flashes; i++)
        {
            // flash on + shake
            soulImage.color = flashColor;
            rt.anchoredPosition = basePos + new Vector3(
                Random.Range(-shakeIntensity, shakeIntensity),
                Random.Range(-shakeIntensity, shakeIntensity),
                0f
            );
            yield return new WaitForSeconds(step);

            // flash off + shake
            soulImage.color = _originalColor;
            rt.anchoredPosition = basePos + new Vector3(
                Random.Range(-shakeIntensity, shakeIntensity),
                Random.Range(-shakeIntensity, shakeIntensity),
                0f
            );
            yield return new WaitForSeconds(step);
        }

        // reset to the position where we started the hit (NOT middle of box)
        rt.anchoredPosition = basePos;
        soulImage.color = _originalColor;

        // ------- REMAINING INVULN BLINK -------
        float remainingInvuln = Mathf.Max(0f, invulnDuration - hitFlashDuration);
        float blinkStep = 0.08f; // speed of blinking

        while (remainingInvuln > 0f)
        {
            soulImage.enabled = !soulImage.enabled;
            remainingInvuln -= blinkStep;
            yield return new WaitForSeconds(blinkStep);
        }

        // ensure visible at the end
        soulImage.enabled = true;
        _invulnerable = false;
        _hitRoutine = null;
    }
}
