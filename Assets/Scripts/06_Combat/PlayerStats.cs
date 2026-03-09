using UnityEngine;
using System.Collections.Generic;

public class PlayerStats : MonoBehaviour
{
    public int maxHP = 20;
    public int HP { get; private set; }

    [Header("Inventory")]
    public List<ItemData> inventory = new List<ItemData>();

    public System.Action OnHPChanged;

    void Awake()
    {
        HP = maxHP;
        OnHPChanged?.Invoke();
    }

    public void Heal(int amount)
    {
        if (amount <= 0) return;
        HP = Mathf.Clamp(HP + amount, 0, maxHP);
        OnHPChanged?.Invoke();
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0) return;
        HP = Mathf.Clamp(HP - amount, 0, maxHP);
        OnHPChanged?.Invoke();
    }

    public void UseItem(ItemData item)
    {
        if (item == null) return;
        Heal(item.healAmount);      // Heal() already calls OnHPChanged
        inventory.Remove(item);
    }
}
