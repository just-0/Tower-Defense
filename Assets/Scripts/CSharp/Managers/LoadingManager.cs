using UnityEngine;
using TMPro; // Asegúrate de tener TextMeshPro importado

public class LoadingManager : MonoBehaviour
{
    // --- NUEVO PATRÓN SINGLETON ---
    private static LoadingManager _instance;

    public static LoadingManager Instance
    {
        get
        {
            // Si la instancia no existe, la buscamos o la creamos
            if (_instance == null)
            {
                // Primero, buscar si ya hay una en la escena
                _instance = FindObjectOfType<LoadingManager>();

                // Si aún no existe, la creamos desde el prefab en Resources
                if (_instance == null)
                {
                    GameObject managerPrefab = Resources.Load<GameObject>("_Managers");
                    if (managerPrefab != null)
                    {
                        GameObject managerObject = Instantiate(managerPrefab);
                        _instance = managerObject.GetComponent<LoadingManager>();
                        managerObject.name = "_Managers (Auto-Instanciado)";
                    }
                    else
                    {
                        Debug.LogError("¡No se pudo encontrar el prefab '_Managers' en la carpeta Resources! El LoadingManager no puede funcionar.");
                        return null;
                    }
                }
            }
            return _instance;
        }
    }

    // --- FIN NUEVO PATRÓN ---

    [Header("Referencias")]
    [Tooltip("El Prefab de la pantalla de carga que se instanciará.")]
    [SerializeField] private GameObject loadingScreenPrefab;
    
    private GameObject currentLoadingScreen;
    private TextMeshProUGUI loadingText;
    private TextMeshProUGUI progressText; // Ya no es [SerializeField]

    private void Awake()
    {
        // Esto asegura que solo haya una instancia del manager y que persista entre escenas
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void Start()
    {
        // Conectarse a los eventos de Photon si estamos en un entorno multijugador
        var photonManager = PhotonManager.Instance;
        if (photonManager != null)
        {
            Debug.Log("[LoadingManager] Suscribiéndose a eventos de PhotonManager...");
            // No queremos que el MasterClient (Colocador) reciba sus propios eventos de vuelta
            // La lógica de RPC con RpcTarget.Others ya lo previene, pero esta es una capa extra de seguridad.
            // Nos suscribimos para que el cliente (Selector) pueda mostrar el progreso.
            photonManager.OnProgressUpdateReceived += HandleRemoteProgressUpdate;
            photonManager.OnSamProcessingComplete += HandleRemoteSamComplete;
            photonManager.OnErrorReceived += HandleRemoteError;
            Debug.Log("[LoadingManager] ✅ Suscripción a eventos de PhotonManager completada.");
        }
        else
        {
            Debug.LogWarning("[LoadingManager] PhotonManager.Instance es null, no se pueden suscribir eventos remotos.");
        }
    }

    private void OnDestroy()
    {
        // Es MUY importante desuscribirse de los eventos para evitar errores.
        if (PhotonManager.Instance != null)
        {
            PhotonManager.Instance.OnProgressUpdateReceived -= HandleRemoteProgressUpdate;
            PhotonManager.Instance.OnSamProcessingComplete -= HandleRemoteSamComplete;
            PhotonManager.Instance.OnErrorReceived -= HandleRemoteError;
        }
    }

    // --- MANEJADORES DE EVENTOS DE PHOTON ---

    private void HandleRemoteProgressUpdate(string step, float progress)
    {
        // Este método es llamado en el cliente Selector cuando el Colocador envía una actualización.
        Debug.Log($"[LoadingManager] Recibida actualización remota de progreso: {step} - {progress}%");
        // Asegurarse de que la pantalla de carga se muestre si aún no lo está
        if (currentLoadingScreen == null)
        {
            Show("Sincronizando con el anfitrión...", true);
        }
        UpdateProgress(step, progress);
    }

    private void HandleRemoteSamComplete()
    {
        // Este método es llamado en el cliente Selector cuando el Colocador ha terminado el proceso SAM.
        Debug.Log("[LoadingManager] ✅ HandleRemoteSamComplete ejecutado - Recibida notificación remota de finalización. Ocultando pantalla...");
        Hide();
        Debug.Log("[LoadingManager] ✅ Pantalla de carga ocultada exitosamente en el Selector.");
    }

    private void HandleRemoteError(string errorMessage, string errorCode)
    {
        // Este método es llamado cuando se recibe un error desde el servidor remoto.
        Debug.Log($"[LoadingManager] Error remoto recibido: {errorCode} - {errorMessage}");
        ShowErrorTemporarily(errorMessage, 3f);
    }

    /// <summary>
    /// Muestra la pantalla de carga con un mensaje específico.
    /// </summary>
    /// <param name="message">El mensaje a mostrar. Si es nulo, se usa un texto por defecto.</param>
    /// <param name="showProgress">Indica si se debe mostrar el texto de progreso.</param>
    public void Show(string message = "Cargando...", bool showProgress = false)
    {
        if (currentLoadingScreen != null)
        {
            // Si ya se está mostrando, solo actualiza el mensaje.
            if (loadingText != null)
            {
                loadingText.text = message;
            }
            if (progressText != null)
            {
                progressText.gameObject.SetActive(showProgress);
                if (showProgress) progressText.text = "0%";
            }
            return;
        }

        // Instancia el prefab
        if (loadingScreenPrefab == null)
        {
            // Si el prefab no está asignado en el inspector, intenta cargarlo desde la carpeta "Resources"
            // Esto hace el sistema más robusto.
            loadingScreenPrefab = Resources.Load<GameObject>("LoadingScreen_Panel");
            if (loadingScreenPrefab == null)
            {
                Debug.LogError("LoadingManager: ¡No se pudo encontrar 'LoadingScreen_Panel' en la carpeta Resources!");
                return;
            }
        }
        
        currentLoadingScreen = Instantiate(loadingScreenPrefab, transform);
        // El canvas de la pantalla de carga ahora será hijo del Manager,
        // por lo que persistirá entre escenas gracias al DontDestroyOnLoad del padre.

        // Usamos GetComponentInChildren para encontrar el Canvas sin importar si está en la raíz o en un hijo del prefab.
        var canvas = currentLoadingScreen.GetComponentInChildren<Canvas>();
        if (canvas != null)
        {
            // Aseguramos que sea independiente de la cámara para que sobreviva el cambio de escena.
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 999;
        }
        else
        {
            Debug.LogError("LoadingManager: ¡No se pudo encontrar un componente 'Canvas' en el prefab de la pantalla de carga! La pantalla no será visible.");
        }
        
        // --- BÚSQUEDA AUTOMÁTICA DE TEXTOS ---
        loadingText = null;
        progressText = null;
        var allTexts = currentLoadingScreen.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (var textComponent in allTexts)
        {
            if (textComponent.gameObject.name == "LoadingText")
            {
                loadingText = textComponent;
            }
            else if (textComponent.gameObject.name == "ProgressText")
            {
                progressText = textComponent;
            }
        }

        if (loadingText != null)
        {
            loadingText.text = message;
        }
        else
        {
            Debug.LogWarning("LoadingManager: No se encontró un objeto de texto llamado 'LoadingText' en el prefab de la pantalla de carga.");
        }

        if (progressText != null)
        {
            progressText.gameObject.SetActive(showProgress);
            if (showProgress) progressText.text = "0%";
        }
        else
        {
            Debug.LogWarning("LoadingManager: No se encontró un objeto de texto llamado 'ProgressText' en el prefab de la pantalla de carga.");
        }
    }

    /// <summary>
    /// Actualiza el mensaje y el progreso en la pantalla de carga.
    /// </summary>
    /// <param name="message">El nuevo mensaje de estado.</param>
    /// <param name="progress">El progreso de 0 a 100.</param>
    public void UpdateProgress(string message, float progress)
    {
        if (currentLoadingScreen == null) return;

        if (loadingText != null)
        {
            loadingText.text = message;
        }
        if (progressText != null && progressText.gameObject.activeSelf)
        {
            progressText.text = $"{progress:F0}%";
        }
    }

    /// <summary>
    /// Oculta la pantalla de carga.
    /// </summary>
    public void Hide()
    {
        Debug.Log("[LoadingManager] Hide() llamado - Ocultando pantalla de carga...");
        if (currentLoadingScreen != null)
        {
            Destroy(currentLoadingScreen);
            currentLoadingScreen = null;
            loadingText = null;
            progressText = null; // Limpiar referencia
            Debug.Log("[LoadingManager] Pantalla de carga destruida exitosamente.");
        }
        else
        {
            Debug.LogWarning("[LoadingManager] Hide() llamado pero currentLoadingScreen ya es null.");
        }
    }

    /// <summary>
    /// Comprueba si la pantalla de carga se está mostrando actualmente.
    /// </summary>
    /// <returns>True si la pantalla de carga está activa, false en caso contrario.</returns>
    public bool IsVisible()
    {
        return currentLoadingScreen != null;
    }

    /// <summary>
    /// Muestra un mensaje de error temporal en la pantalla de carga y la oculta después del tiempo especificado.
    /// </summary>
    /// <param name="errorMessage">El mensaje de error a mostrar.</param>
    /// <param name="displayTime">Tiempo en segundos que se mostrará el error antes de ocultar la pantalla.</param>
    public void ShowErrorTemporarily(string errorMessage, float displayTime = 3f)
    {
        // Asegurar que la pantalla de carga esté visible
        if (currentLoadingScreen == null)
        {
            Show("Error", false);
        }

        // Actualizar con el mensaje de error
        if (loadingText != null)
        {
            loadingText.text = $"❌ {errorMessage}";
            loadingText.color = UnityEngine.Color.red;
        }

        // Ocultar el texto de progreso durante errores
        if (progressText != null)
        {
            progressText.gameObject.SetActive(false);
        }

        // Iniciar corrutina para ocultar después del tiempo especificado
        StartCoroutine(HideAfterDelay(displayTime));
    }

    private System.Collections.IEnumerator HideAfterDelay(float delay)
    {
        yield return new UnityEngine.WaitForSeconds(delay);
        Hide();
        
        // Restaurar color del texto para futuros usos
        if (loadingText != null)
        {
            loadingText.color = UnityEngine.Color.white;
        }
    }
} 