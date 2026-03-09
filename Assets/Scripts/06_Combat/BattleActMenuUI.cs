using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class BattleActMenuUI : MonoBehaviour
{
    [Header("Root Panel")]
    public GameObject root;              // Panel_ActMenu

    [Header("List")]
    public Transform actListParent;      // ActList object
    public Button actButtonPrefab;       // Button prefab with TMP_Text child

    private BattleController battle;
    private readonly List<Button> spawned = new();

    private void Awake()
    {
        if (root != null)
            root.SetActive(false);       // hide at start
    }

    public void Bind(BattleController controller)
    {
        battle = controller;
    }

    public void Show(List<ActOptionData> acts)
    {
        Clear();

        if (acts == null || acts.Count == 0)
        {
            root.SetActive(false);
            return;
        }

        foreach (var act in acts)
        {
            var btn = Instantiate(actButtonPrefab, actListParent);
            spawned.Add(btn);

            // label
            var txt = btn.GetComponentInChildren<TMPro.TMP_Text>();
            if (txt != null)
                txt.text = "* " + act.actName;

            var captured = act;
            btn.onClick.AddListener(() =>
            {
                battle.OnPlayerChooseAct(captured);
                Hide();
            });
        }

        root.SetActive(true);  // <– this is what should make Panel_ActMenu appear
    }

    public void Hide()
    {
        root.SetActive(false);
    }

    private void Clear()
    {
        foreach (var b in spawned)
            if (b != null) Destroy(b.gameObject);
        spawned.Clear();
    }
}
