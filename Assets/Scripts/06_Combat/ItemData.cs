using UnityEngine;

[CreateAssetMenu(menuName = "Battle/Item")]
public class ItemData : ScriptableObject
{
    public string itemName;
    public Sprite icon;
    public int healAmount;
}
