using UnityEngine;
using UnityEngine.UI; 

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] private SceneController sceneController;
    [SerializeField] private string gameplaySceneName = "GameplayScene";


    public void OnPlayButtonClicked()
    {
        sceneController.LoadSceneByName(gameplaySceneName);
    }

   
    public void OnExitButtonClicked()
    {
        sceneController.QuitGame();
    }
}