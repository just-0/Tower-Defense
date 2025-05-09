using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;
using UnityEngine.UI;

public class SceneTransitionManager : MonoBehaviour
{
    [SerializeField] private GameObject transitionPanel;
    [SerializeField] private float transitionDuration = 1.0f;
    [SerializeField] private AnimationCurve transitionCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private string initialSceneName = "CameraVerification";
    [SerializeField] private string mainGameSceneName = "MainGame";
    
    private static SceneTransitionManager _instance;
    
    public static SceneTransitionManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<SceneTransitionManager>();
                
                if (_instance == null)
                {
                    GameObject go = new GameObject("SceneTransitionManager");
                    _instance = go.AddComponent<SceneTransitionManager>();
                }
            }
            
            return _instance;
        }
    }
    
    private void Awake()
    {
        // Singleton pattern
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        
        _instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Asegurarse de que el panel de transición esté inicialmente invisible
        if (transitionPanel != null)
        {
            Image panelImage = transitionPanel.GetComponent<Image>();
            if (panelImage != null)
            {
                Color color = panelImage.color;
                color.a = 0;
                panelImage.color = color;
            }
            transitionPanel.SetActive(false);
        }
    }
    
    public void LoadMainGame()
    {
        StartCoroutine(TransitionToScene(mainGameSceneName));
    }
    
    public void LoadCameraVerification()
    {
        StartCoroutine(TransitionToScene(initialSceneName));
    }
    
    public void ReloadCurrentScene()
    {
        StartCoroutine(TransitionToScene(SceneManager.GetActiveScene().name));
    }
    
    private IEnumerator TransitionToScene(string sceneName)
    {
        // Mostrar panel de transición y hacer fade in
        if (transitionPanel != null)
        {
            transitionPanel.SetActive(true);
            Image panelImage = transitionPanel.GetComponent<Image>();
            
            // Fade in
            float elapsedTime = 0;
            while (elapsedTime < transitionDuration)
            {
                elapsedTime += Time.deltaTime;
                float t = transitionCurve.Evaluate(elapsedTime / transitionDuration);
                
                if (panelImage != null)
                {
                    Color color = panelImage.color;
                    color.a = t;
                    panelImage.color = color;
                }
                
                yield return null;
            }
        }
        
        // Cargar la nueva escena
        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        
        // Esperar a que la escena se cargue completamente
        while (!asyncLoad.isDone)
        {
            yield return null;
        }
        
        // Esperar un pequeño tiempo para asegurar que la escena esté lista
        yield return new WaitForSeconds(0.2f);
        
        // Hacer fade out del panel de transición
        if (transitionPanel != null)
        {
            Image panelImage = transitionPanel.GetComponent<Image>();
            
            // Fade out
            float elapsedTime = 0;
            while (elapsedTime < transitionDuration)
            {
                elapsedTime += Time.deltaTime;
                float t = transitionCurve.Evaluate(elapsedTime / transitionDuration);
                
                if (panelImage != null)
                {
                    Color color = panelImage.color;
                    color.a = 1 - t;
                    panelImage.color = color;
                }
                
                yield return null;
            }
            
            transitionPanel.SetActive(false);
        }
    }
} 