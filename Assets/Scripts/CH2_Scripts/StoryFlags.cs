using UnityEngine;

public enum Route
{
    Good,
    Bad,
    Neutral
}

public class StoryFlags : MonoBehaviour
{
    public static StoryFlags instance;

    public Route currentRoute = Route.Neutral;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }

    // ✅ NEW: helper so other systems can safely ensure this exists
    public static StoryFlags EnsureExists()
    {
        if (instance != null)
            return instance;

        GameObject existing = GameObject.Find("StoryFlags");
        if (existing != null)
        {
            StoryFlags flags = existing.GetComponent<StoryFlags>();
            if (flags != null)
            {
                instance = flags;
                DontDestroyOnLoad(existing);
                return instance;
            }
        }

        GameObject go = new GameObject("StoryFlags");
        instance = go.AddComponent<StoryFlags>();
        return instance;
    }
}