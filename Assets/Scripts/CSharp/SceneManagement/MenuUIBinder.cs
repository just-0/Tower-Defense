using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Este componente se encarga de encontrar los elementos de la UI en su escena
/// y los registra con el singleton persistente MenuGestureController.
/// </summary>
public class MenuUIBinder : MonoBehaviour
{
    [Header("UI de la Escena")]
    [Tooltip("El RawImage que muestra la cámara en esta escena.")]
    [SerializeField] private RawImage cameraFeed;

    [Tooltip("El Texto que muestra el contador de dedos en esta escena.")]
    [SerializeField] private Text fingerCountText;

    [Tooltip("El panel de carga de esta escena.")]
    [SerializeField] private GameObject loadingPanel;
    
    [Header("Auto-detectar UI")]
    [Tooltip("Si está marcado, buscará automáticamente los elementos de UI en la escena.")]
    [SerializeField] private bool autoDetectUI = true;
    
    private bool isRegistered = false;
    private float retryTimer = 0f;
    private const float RETRY_INTERVAL = 1f; // Reintentar cada segundo si no encuentra MenuGestureController

    void Start()
    {
        if (autoDetectUI)
        {
            AutoDetectUIElements();
        }
        
        // Intentar registrar con el MenuGestureController
        StartCoroutine(RegisterWithMenuController());
    }
    
    private void AutoDetectUIElements()
    {
        // Auto-detectar el RawImage de la cámara si no está asignado
        if (cameraFeed == null)
        {
            cameraFeed = FindObjectOfType<RawImage>();
            if (cameraFeed != null)
                Debug.Log("MenuUIBinder: RawImage auto-detectado");
        }
        
        // Auto-detectar el texto de dedos si no está asignado
        if (fingerCountText == null)
        {
            Text[] allTexts = FindObjectsOfType<Text>();
            foreach (Text text in allTexts)
            {
                if (text.name.ToLower().Contains("finger") || text.name.ToLower().Contains("dedo"))
                {
                    fingerCountText = text;
                    Debug.Log($"MenuUIBinder: Texto de dedos auto-detectado: {text.name}");
                    break;
                }
            }
        }
        
        // Auto-detectar el panel de carga si no está asignado
        if (loadingPanel == null)
        {
            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj.name.ToLower().Contains("loading") || obj.name.ToLower().Contains("carga"))
                {
                    loadingPanel = obj;
                    Debug.Log($"MenuUIBinder: Panel de carga auto-detectado: {obj.name}");
                    break;
                }
            }
        }
    }
    
    private IEnumerator RegisterWithMenuController()
    {
        int attempts = 0;
        const int MAX_ATTEMPTS = 10;
        
        while (!isRegistered && attempts < MAX_ATTEMPTS)
        {
            // Intentar encontrar el MenuGestureController
            if (MenuGestureController.Instance != null)
            {
                // Registrar la UI con el controlador persistente
                MenuGestureController.Instance.RegisterUI(cameraFeed, fingerCountText, loadingPanel);
                
                // Solicitar reconexión si es necesario
                MenuGestureController.Instance.ReconnectIfNeeded();
                
                isRegistered = true;
                Debug.Log("MenuUIBinder: UI registrada exitosamente con MenuGestureController");
                break;
            }
            else
            {
                attempts++;
                Debug.LogWarning($"MenuUIBinder: MenuGestureController no encontrado (intento {attempts}/{MAX_ATTEMPTS}). Reintentando...");
                yield return new WaitForSeconds(RETRY_INTERVAL);
            }
        }
        
        if (!isRegistered)
        {
            Debug.LogError("MenuUIBinder: No se pudo encontrar MenuGestureController después de múltiples intentos. Los gestos pueden no funcionar correctamente.");
        }
    }

    void OnDisable()
    {
        // Cuando se desactiva este objeto (cambio de escena), desregistrar la UI
        if (isRegistered && MenuGestureController.Instance != null)
        {
            MenuGestureController.Instance.UnregisterUI(this);
            isRegistered = false;
            Debug.Log("MenuUIBinder: UI desregistrada de MenuGestureController");
        }
    }
    
    void OnDestroy()
    {
        // Asegurar limpieza al destruir
        if (isRegistered && MenuGestureController.Instance != null)
        {
            MenuGestureController.Instance.UnregisterUI(this);
            isRegistered = false;
        }
    }
    
    // Método público para re-registrar manualmente si es necesario
    public void ForceReregister()
    {
        if (MenuGestureController.Instance != null)
        {
            MenuGestureController.Instance.RegisterUI(cameraFeed, fingerCountText, loadingPanel);
            MenuGestureController.Instance.ReconnectIfNeeded();
            isRegistered = true;
            Debug.Log("MenuUIBinder: Re-registro forzado completado");
        }
    }
} 