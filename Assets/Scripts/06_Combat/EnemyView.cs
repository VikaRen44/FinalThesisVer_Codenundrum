using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

public class EnemyView : MonoBehaviour
{
    [Header("UI Refs")]
    public Image portrait;          // enemy sprite image
    public TMP_Text nameText;       // enemy name text (optional)
    public TMP_Text damageText;     // small text near enemy for damage popup

    [Header("Hit Effect")]
    public Color hitFlashColor = Color.red;
    public float hitFlashDuration = 0.4f;
    public int hitFlashCount = 4;
    public float shakeIntensity = 6f;  // how strong the shake is

    [Header("SFX")]
    [Tooltip("AudioSource used for hit sounds. If empty, will try GetComponent<AudioSource>() then FindObjectOfType<AudioSource>().")]
    public AudioSource sfxSource;
    public AudioClip enemyHitSfx;
    [Range(0f, 1f)] public float enemyHitSfxVolume = 1f;

    private Color _originalColor = Color.white;
    private Coroutine _hitRoutine;

    private void Awake()
    {
        if (portrait != null)
            _originalColor = portrait.color;

        if (damageText != null)
            damageText.gameObject.SetActive(false);

        // SFX source auto-find (non-destructive)
        if (sfxSource == null) sfxSource = GetComponent<AudioSource>();
        if (sfxSource == null) sfxSource = FindObjectOfType<AudioSource>(); // fallback (optional)
    }

    // Call this from BattleController when setting the enemy
    public void SetEnemy(EnemyData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[EnemyView] SetEnemy called with null EnemyData.");
            return;
        }

        // Name
        if (nameText != null)
            nameText.text = data.enemyName;

        // Portrait
        if (portrait != null)
        {
            portrait.sprite = data.portrait;
            portrait.enabled = (data.portrait != null);
            _originalColor = portrait.color;
        }
        else
        {
            Debug.LogWarning("[EnemyView] Portrait Image reference is not assigned.");
        }
    }

    /// <summary>
    /// Called when the enemy takes damage. Plays blink + shake + shows damage text.
    /// </summary>
    public void ShowHit(int damage)
    {
        // ✅ Play SFX immediately on hit
        PlayEnemyHitSfx();

        if (_hitRoutine != null)
            StopCoroutine(_hitRoutine);

        _hitRoutine = StartCoroutine(HitRoutine(damage));
    }

    private void PlayEnemyHitSfx()
    {
        if (enemyHitSfx == null) return;
        if (sfxSource == null) return;

        sfxSource.PlayOneShot(enemyHitSfx, enemyHitSfxVolume);
    }

    private IEnumerator HitRoutine(int damage)
    {
        // --- DAMAGE POPUP ---
        if (damageText != null)
        {
            damageText.text = "-" + damage.ToString();
            damageText.gameObject.SetActive(true);
        }

        // --- FLICKER + SHAKE ---
        if (portrait != null && portrait.enabled)
        {
            RectTransform rt = portrait.rectTransform;
            Vector3 originalPos = rt.anchoredPosition;

            float totalTime = Mathf.Max(0.01f, hitFlashDuration);
            int totalFlashes = Mathf.Max(1, hitFlashCount);
            float step = totalTime / (totalFlashes * 2f);

            for (int i = 0; i < totalFlashes; i++)
            {
                // flash on
                portrait.color = hitFlashColor;
                rt.anchoredPosition = originalPos + new Vector3(
                    Random.Range(-shakeIntensity, shakeIntensity),
                    Random.Range(-shakeIntensity, shakeIntensity),
                    0f
                );
                yield return new WaitForSeconds(step);

                // flash off
                portrait.color = _originalColor;
                rt.anchoredPosition = originalPos + new Vector3(
                    Random.Range(-shakeIntensity, shakeIntensity),
                    Random.Range(-shakeIntensity, shakeIntensity),
                    0f
                );
                yield return new WaitForSeconds(step);
            }

            // reset to normal
            portrait.color = _originalColor;
            rt.anchoredPosition = originalPos;
        }

        // small delay so the player can see the number
        if (damageText != null)
        {
            yield return new WaitForSeconds(0.25f);
            damageText.gameObject.SetActive(false);
        }

        _hitRoutine = null;
    }
}
