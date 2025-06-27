using UnityEngine;
using UnityEngine.SceneManagement;
using System.Collections;

public class InitialSceneLoader : MonoBehaviour
{
    [SerializeField] private string cameraVerificationSceneName = "CameraVerification";
    [SerializeField] private string mainGameSceneName = "MainGame";
    [SerializeField] private bool alwaysStartWithVerification = true;
    [SerializeField] private float initialDelay = 1.0f;
    
    // Singleton para asegurar que solo hay una instancia
    private static InitialSceneLoader _instance;
    
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
        
        // Iniciar la carga de la escena apropiada
        StartCoroutine(LoadInitialScene());
    }
    
    private IEnumerator LoadInitialScene()
    {
        // Esperar un momento para permitir que Unity inicialice completamente
        yield return new WaitForSeconds(initialDelay);
        
        // Siempre comenzar con la escena de verificación de cámaras si está configurado así
        if (alwaysStartWithVerification)
        {
            LoadCameraVerificationScene();
            yield break;
        }
        
        // Intentar detectar si las cámaras ya están conectadas
        // Esto podría implementarse con PlayerPrefs para recordar el estado anterior
        bool camerasWereConnected = PlayerPrefs.GetInt("CamerasConnected", 0) == 1;
        
        if (camerasWereConnected)
        {
            // Si las cámaras estaban conectadas la última vez, ir directamente al juego
            // pero con un timeout para verificar que siguen conectadas
            LoadMainGameScene();
        }
        else
        {
            // Si no hay información o las cámaras no estaban conectadas, ir a verificación
            LoadCameraVerificationScene();
        }
    }
    
    public void LoadCameraVerificationScene()
    {
        // Debug.Log("Cargando escena de verificación de cámaras...");
        SceneManager.LoadScene(cameraVerificationSceneName);
    }
    
    public void LoadMainGameScene()
    {
        // Debug.Log("Cargando escena principal del juego...");
        SceneManager.LoadScene(mainGameSceneName);
    }
    
    // Método para recordar que las cámaras están conectadas
    public static void SetCamerasConnected(bool connected)
    {
        PlayerPrefs.SetInt("CamerasConnected", connected ? 1 : 0);
        PlayerPrefs.Save();
    }
} 