using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class UIFirstSelect : MonoBehaviour
{
    [Tooltip("Drag your UpperCase grid here.")]
    public RectTransform upperCaseGrid;

    void Start()
    {
        // Wait 1 frame so LetterGenerator.Start() finishes spawning
        StartCoroutine(SelectFirst());
    }

    System.Collections.IEnumerator SelectFirst()
    {
        yield return null;

        if (EventSystem.current == null) yield break;
        if (upperCaseGrid == null) yield break;

        var firstBtn = upperCaseGrid.GetComponentInChildren<Button>(true);
        if (firstBtn != null)
            EventSystem.current.SetSelectedGameObject(firstBtn.gameObject);
    }
}
