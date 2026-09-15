using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class SceneController : MonoBehaviour
{
    [Header("Configuración de Transición")]
    [SerializeField] private Image fadeImage;
    [SerializeField] private float duration = 2f;

    private void Start()
    {
        if (fadeImage != null)
        {
            Color c = fadeImage.color;
            c.a = 0f;
            fadeImage.color = c;
            fadeImage.gameObject.SetActive(false);
        }
    }

    public void MainPage() => StartTransition("OpeningMainScene");
    public void SelectorScene() => StartTransition("SelectorScene");
    public void Map1() => StartTransition("TestCinematic");
    public void ModeScene() => StartTransition("ModeSelector");
    public void CreditsScene() => StartTransition("CreditsScene");
    public void Winning() => StartTransition("WinnerScene");

    public void Quitgame()
    {
        StartCoroutine(QuitRoutine());
    }

    private void StartTransition(string sceneName)
    {
        StartCoroutine(FadeAndLoadRoutine(sceneName));
    }

    // Corrutina base para el efecto visual de Fade Out
    private IEnumerator FadeOutRoutine()
    {
        if (fadeImage != null)
        {
            fadeImage.gameObject.SetActive(true);
            float timer = 0f;
            Color originalColor = fadeImage.color;

            while (timer < duration)
            {
                // Usamos unscaledDeltaTime por si el juego está en pausa (Time.timeScale = 0)
                timer += Time.unscaledDeltaTime;
                float progress = timer / duration;

                originalColor.a = Mathf.Lerp(0f, 1f, progress);
                fadeImage.color = originalColor;

                yield return null;
            }
        }
        else
        {
            yield return new WaitForSecondsRealtime(duration);
        }
    }

    private IEnumerator FadeAndLoadRoutine(string sceneName)
    {
        yield return StartCoroutine(FadeOutRoutine());
        SceneManager.LoadScene(sceneName);
    }

    private IEnumerator QuitRoutine()
    {
        yield return StartCoroutine(FadeOutRoutine());

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
    }
}