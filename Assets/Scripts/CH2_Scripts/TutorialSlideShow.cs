using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using System.Collections;

public class TutorialSlideshow : MonoBehaviour
{
    public GameObject[] pages;

    public GameObject backButton;
    public GameObject nextButton;
    public GameObject playButton;

    public GameObject heapMinigame;

    public MonoBehaviour uiManager;

    private int currentPage = 0;

    // Prevents input from triggering instantly when scene loads
    private bool inputEnabled = false;

    void Start()
    {
        ShowPage(0);
        StartCoroutine(EnableInputDelay());
    }

    IEnumerator EnableInputDelay()
    {
        yield return new WaitForSeconds(0.2f);
        inputEnabled = true;
    }

    void Update()
    {
        if (!inputEnabled) return;

        if (Keyboard.current.aKey.wasPressedThisFrame)
        {
            GoBackScene();
        }

        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            if (currentPage == pages.Length - 1)
                StartGame();
            else
                NextPage();
        }

        if (Keyboard.current.wKey.wasPressedThisFrame)
        {
            PreviousPage();
        }
    }
    
    public void NextPage()
    {
        if (currentPage < pages.Length - 1)
        {
            currentPage++;
            ShowPage(currentPage);
        }
    }

    public void PreviousPage()
    {
        if (currentPage > 0)
        {
            currentPage--;
            ShowPage(currentPage);
        }
    }

    void ShowPage(int index)
    {
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i].SetActive(i == index);
        }

        // Back button hidden on first page
        backButton.SetActive(index > 0);

        nextButton.SetActive(index < pages.Length - 1);
        playButton.SetActive(index == pages.Length - 1);
    }

    public void StartGame()
    {
        // Hide tutorial
        gameObject.SetActive(false);

        // Enable the heap minigame UI
        if (heapMinigame != null)
            heapMinigame.SetActive(true);

        // Start the heap game through UIManager_Intro
        if (uiManager != null){
            uiManager.SendMessage("CloseTutorial", SendMessageOptions.DontRequireReceiver);
            uiManager.SendMessage("OnPressPlay", SendMessageOptions.DontRequireReceiver);
        }
    }

    public void GoBackScene()
    {
        int currentIndex = SceneManager.GetActiveScene().buildIndex;

        if (currentIndex > 0)
        {
            SceneManager.LoadScene(currentIndex - 1);
        }
    }
}