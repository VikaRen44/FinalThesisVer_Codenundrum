using UnityEngine;
using UnityEngine.SceneManagement;

public class MinigameChoice : MonoBehaviour
{
    public void LoadHeapGame()
    {
        SceneManager.LoadScene("04_True_Heap");
    }

    public void LoadQuickSortGame()
    {
        SceneManager.LoadScene("06_QuickSort");
    }
}