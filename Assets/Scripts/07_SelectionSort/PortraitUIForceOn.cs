using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PortraitUIForceOn : MonoBehaviour
{
    [Header("Assign PortraitGrid root (content root)")]
    public GameObject portraitGridRoot;

    [Header("Behavior")]
    public bool forceEnableOnStart = true;
    public bool forceEnableAfterOneSecond = true; // catches late spawners
    public bool forceEnableOnEnable = true;

    private void OnEnable()
    {
        if (forceEnableOnEnable)
            StartCoroutine(ForcePass());
    }

    private IEnumerator Start()
    {
        if (!forceEnableOnStart) yield break;

        yield return null;
        yield return new WaitForEndOfFrame();
        yield return ForcePass();

        if (forceEnableAfterOneSecond)
        {
            yield return new WaitForSecondsRealtime(1f);
            yield return ForcePass();
        }
    }

    public IEnumerator ForcePass()
    {
        if (!portraitGridRoot) yield break;

        // Make sure the grid is active (DO NOT disable it in other scripts)
        if (!portraitGridRoot.activeInHierarchy)
            portraitGridRoot.SetActive(true);

        // Force layout to settle before enabling visuals
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(portraitGridRoot.GetComponent<RectTransform>());
        Canvas.ForceUpdateCanvases();

        // 1) Never touch LayoutElement enabled state (leave it alone)
        // 2) Never touch PortraitCard script enabled state (leave it alone)
        // 3) Only guarantee rendering components are enabled:

        foreach (var cr in portraitGridRoot.GetComponentsInChildren<CanvasRenderer>(true))
            cr.cullTransparentMesh = false; // prevents accidental culling

        foreach (var g in portraitGridRoot.GetComponentsInChildren<Graphic>(true))
            g.enabled = true;

        foreach (var t in portraitGridRoot.GetComponentsInChildren<TMP_Text>(true))
            t.enabled = true;

        // IMPORTANT: ensure each PortraitCard.Visual is active
        var cards = portraitGridRoot.GetComponentsInChildren<PortraitCard>(true);
        foreach (var card in cards)
        {
            if (card != null && card.visual != null && !card.visual.gameObject.activeSelf)
                card.visual.gameObject.SetActive(true);
        }
    }
}
