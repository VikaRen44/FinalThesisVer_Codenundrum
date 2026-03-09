using UnityEngine;

public class PersistentEventSystem : MonoBehaviour
{
    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }
}