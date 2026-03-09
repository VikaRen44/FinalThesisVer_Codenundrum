using UnityEngine;
using System.Collections;

public class TeleportState : MonoBehaviour
{
    public bool IsOnCooldown { get; private set; }

    public void StartCooldown(float seconds)
    {
        if (gameObject.activeInHierarchy)
            StartCoroutine(Cooldown(seconds));
    }

    private IEnumerator Cooldown(float seconds)
    {
        IsOnCooldown = true;
        yield return new WaitForSeconds(seconds);
        IsOnCooldown = false;
    }
}
