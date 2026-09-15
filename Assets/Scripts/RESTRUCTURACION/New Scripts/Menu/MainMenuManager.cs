using UnityEngine;
using UnityEngine.UI; // Si usas Canvas tradicional

public class MainMenuManager : MonoBehaviour
{
    [SerializeField] private SceneController sceneController;
    [SerializeField] private string gameplaySceneName = "GameplayScene";

    // Método que llamará el botón "Jugar" / "Start"
    public void OnPlayButtonClicked()
    {
        sceneController.LoadSceneByName(gameplaySceneName);
    }

    // Método que llamará el botón "Salir"
    public void OnExitButtonClicked()
    {
        sceneController.QuitGame();
    }
}