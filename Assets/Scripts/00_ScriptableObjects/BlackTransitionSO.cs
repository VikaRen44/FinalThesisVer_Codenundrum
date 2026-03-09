using UnityEngine;

public enum BlackFadeType
{
    FadeIn = 0,
    FadeOut = 1
}

[CreateAssetMenu(menuName = "Cutscene/Black Transition")]
public class BlackTransitionSO : ScriptableObject
{
    public BlackFadeType fadeType = BlackFadeType.FadeIn;

    [Min(0f)]
    public float duration = 0.25f;
}
