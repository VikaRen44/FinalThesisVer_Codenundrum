using UnityEngine;

[CreateAssetMenu(menuName = "Tutorial/Tutorial Sequence")]
public class TutorialSequenceSO : ScriptableObject
{
    public Sprite[] pages;

    [Tooltip("If true, Next on last page goes back to first, Prev on first goes to last.")]
    public bool loop = false;
}
