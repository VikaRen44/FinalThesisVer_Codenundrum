using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Collections;

public class UINavigationController : MonoBehaviour
{
    public static UINavigationController Instance;

    [Tooltip("Failsafe selectable if UI opens without focus.")]
    public Selectable fallbackSelectable;

    private EventSystem _eventSystem;
    private Coroutine _repairRoutine;

    void Awake()
    {
        Instance = this;
        _eventSystem = EventSystem.current;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        if (_eventSystem == null)
            _eventSystem = EventSystem.current;

        if (_eventSystem == null)
            return;

        // ✅ Controller lost focus protection
        if (_eventSystem.currentSelectedGameObject == null)
        {
            TryRepairSelection();
        }
    }

    public void PrimeNavigation(GameObject preferred)
    {
        if (!isActiveAndEnabled) return;

        StopRepairRoutine();
        _repairRoutine = StartCoroutine(PrimeRoutine(preferred));
    }

    IEnumerator PrimeRoutine(GameObject preferred)
    {
        yield return null;
        yield return null;

        if (_eventSystem == null)
            yield break;

        GameObject target = preferred;

        if (target == null && fallbackSelectable != null)
            target = fallbackSelectable.gameObject;

        if (target != null && target.activeInHierarchy)
        {
            _eventSystem.SetSelectedGameObject(null);
            _eventSystem.SetSelectedGameObject(target);
        }

        _repairRoutine = null;
    }

    void TryRepairSelection()
    {
        Selectable next = Selectable.allSelectablesArray.Length > 0
            ? Selectable.allSelectablesArray[0]
            : null;

        if (next != null && next.gameObject.activeInHierarchy)
        {
            _eventSystem.SetSelectedGameObject(next.gameObject);
        }
    }

    void StopRepairRoutine()
    {
        if (_repairRoutine != null)
        {
            StopCoroutine(_repairRoutine);
            _repairRoutine = null;
        }
    }
}