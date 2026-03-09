using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BattleItemMenuUI : MonoBehaviour
{
    [Header("Root Panel")]
    public GameObject root;              // Panel_ItemMenu

    [Header("List")]
    public Transform itemListParent;     // ItemList object
    public Button itemButtonPrefab;      // text-style button

    private BattleController battle;
    private readonly List<Button> spawned = new();

    private void Awake()
    {
        if (root != null)
            root.SetActive(false);
    }

    public void Bind(BattleController controller)
    {
        battle = controller;
    }

    public void Show(List<ItemData> items)
    {
        Clear();

        if (items == null || items.Count == 0)
        {
            if (root != null) root.SetActive(false);
            return;
        }

        foreach (var item in items)
        {
            var btn = Instantiate(itemButtonPrefab, itemListParent);
            spawned.Add(btn);

            var txt = btn.GetComponentInChildren<TMP_Text>();
            if (txt != null)
                txt.text = item != null ? item.itemName : "(null item)";

            var capturedItem = item; // closure
            btn.onClick.AddListener(() =>
            {
                if (battle != null)
                    battle.OnPlayerChooseItem(capturedItem);
            });
        }

        if (root != null)
            root.SetActive(true);
    }

    public void Hide()
    {
        if (root != null)
            root.SetActive(false);
    }

    public void OnBackPressed()
    {
        Hide();

        if (battle != null)
            battle.ReturnFromSubMenu(); // ✅ must exist in BattleController
    }

    private void Clear()
    {
        foreach (var b in spawned)
            if (b != null) Destroy(b.gameObject);

        spawned.Clear();
    }
}
