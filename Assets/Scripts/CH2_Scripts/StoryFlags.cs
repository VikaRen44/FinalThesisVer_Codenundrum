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

    public Route currentRoute;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
}