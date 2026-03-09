using Unity.VisualScripting;
using UnityEngine;

public enum ActEffectType { DialogueOnly, HealPlayer, DebuffEnemy, SpareProgress }

[CreateAssetMenu(menuName = "Battle/Act Option")]
public class ActOptionData : ScriptableObject
{
    public string actName;
    [TextArea] public string dialogueText;

    public ActEffectType effectType;
    public int value; // heal amount, spare progress, debuff strength, etc.

    // optional special handler for unique logic
    public ActHandler customHandler;
}
