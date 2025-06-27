using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI; // Necesario para el texto de estado
using NativeWebSocket; // Necesario para el sondeo
using System.Threading.Tasks; // Necesario para async

public class MainMenuController : MonoBehaviour
{
    [Header("UI Feedback")]
    [Tooltip("Panel que contiene los botones para desactivarlo durante la carga.")]
    [SerializeField] private GameObject buttonPanel;
    
    [Header("Panel Management")]
    [Tooltip("Panel de configuración (opcional, se auto-detecta si no se asigna)")]
    [SerializeField] private GameObject settingsPanel;

    // Asigna este método al botón "Jugar Solo"
    public async void OnSinglePlayerClicked()
    {
        // Desactivar UI para evitar clics múltiples y dar feedback
        if (buttonPanel != null) buttonPanel.SetActive(false);
        LoadingManager.Instance.Show("Iniciando modo Un Jugador...");

        if (BackendManager.Instance != null)
        {
            // Debug.Log("Solicitando modo Single-Player al backend...");
            await BackendManager.Instance.RequestBackendMode(BackendMode.SinglePlayer);
        }

        // --- SONDEO INTELIGENTE PARA SINGLE PLAYER ---
        LoadingManager.Instance.Show("Esperando al servidor del juego (SAM)...");
        bool gameServerReady = await PingServerUntilReady("ws://localhost:8767", 20f);
        
        LoadingManager.Instance.Show("Esperando al servidor de gestos...");
        bool gestureServerReady = await PingServerUntilReady("ws://localhost:8768", 10f);
        // ---------------------------------------------

        if (gameServerReady && gestureServerReady)
        {
            LoadingManager.Instance.Show("¡Todo listo! Cargando...");
            SceneManager.LoadSceneAsync("5_SinglePlayer");
        }
        else
        {
            Debug.LogError("Uno o más servidores no respondieron a tiempo. Volviendo al menú.");
            LoadingManager.Instance.Hide(); // Ocultamos la pantalla de carga
            if (buttonPanel != null) buttonPanel.SetActive(true); // Reactivar UI
        }
    }

    // Asigna este método al botón "Multijugador"
    public void OnMultiplayerClicked()
    {
        // Primero, le decimos a Photon que inicie la conexión
        if(PhotonManager.Instance != null)
        {
            PhotonManager.Instance.ConnectToPhoton();
        }
        
        // Navega al Lobby para la configuración multijugador
        SceneManager.LoadScene("2_Lobby");
    }

    // Asigna este método al botón "Tutorial"
    public void OnTutorialClicked()
    {
        // Ya no carga una escena directamente, sino que muestra el panel de selección de tutorial.
        if (UniversalPanelManager.Instance != null)
        {
            UniversalPanelManager.Instance.ShowPanel("TutorialPanel");
        }
        else
        {
            Debug.LogError("No se encontró UniversalPanelManager en la escena. No se puede mostrar el panel de tutorial.");
        }
    }

    // Asigna este método al botón "Configuración"
    public void OnSettingsClicked()
    {
        // Debug.Log("Botón de Configuración presionado.");
        
        // Usar el UniversalPanelManager para manejar el cambio de paneles y gestos.
        if (UniversalPanelManager.Instance != null)
        {
            UniversalPanelManager.Instance.ShowPanel("SettingsPanel");
        }
        else
        {
            Debug.LogError("No se encontró UniversalPanelManager en la escena. No se puede cambiar al panel de configuración.");
            // Fallback por si acaso, aunque no debería ocurrir si está bien configurado.
            // Pero lo ideal es asegurar que siempre haya una instancia de UniversalPanelManager.
            if (settingsPanel != null)
            {
                // Crear panel de configuración simple
                CreateSettingsPanel();
            }
        }
    }
    
    // Método universal para ir a cualquier panel
    public void ShowPanel(string panelName)
    {
        if (UniversalPanelManager.Instance != null)
        {
            UniversalPanelManager.Instance.ShowPanel(panelName);
        }
        else
        {
            Debug.LogWarning($"UniversalPanelManager no encontrado. No se puede mostrar: {panelName}");
        }
    }
    
    // Método para volver al panel anterior
    public void GoBackToPreviousPanel()
    {
        if (UniversalPanelManager.Instance != null)
        {
            UniversalPanelManager.Instance.GoBack();
        }
        else
        {
            Debug.LogWarning("UniversalPanelManager no encontrado para navegación hacia atrás");
        }
    }
    
    void EnsureSettingsPanelExists()
    {
        if (settingsPanel == null)
        {
            // Buscar si ya existe
            settingsPanel = GameObject.Find("SettingsPanel");
            
            if (settingsPanel == null)
            {
                // Crear panel de configuración simple
                CreateSettingsPanel();
            }
        }
    }
    
    void CreateSettingsPanel()
    {
        if (buttonPanel == null) return;
        
        // Debug.Log("Creando panel de configuración automáticamente...");
        
        // Crear como hermano del buttonPanel
        settingsPanel = new GameObject("SettingsPanel");
        settingsPanel.transform.SetParent(buttonPanel.transform.parent, false);
        
        // Copiar RectTransform del buttonPanel
        RectTransform buttonRect = buttonPanel.GetComponent<RectTransform>();
        RectTransform settingsRect = settingsPanel.AddComponent<RectTransform>();
        
        if (buttonRect != null)
        {
            settingsRect.anchorMin = buttonRect.anchorMin;
            settingsRect.anchorMax = buttonRect.anchorMax;
            settingsRect.offsetMin = buttonRect.offsetMin;
            settingsRect.offsetMax = buttonRect.offsetMax;
            settingsRect.anchoredPosition = buttonRect.anchoredPosition;
            settingsRect.sizeDelta = buttonRect.sizeDelta;
        }
        
        // Agregar fondo
        var bgImage = settingsPanel.AddComponent<UnityEngine.UI.Image>();
        bgImage.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        
        // Crear contenido básico
        CreateSettingsContent();
        
        // Inicialmente oculto
        settingsPanel.SetActive(false);
        
        // Debug.Log("✅ Panel de configuración creado");
    }
    
    void CreateSettingsContent()
    {
        // Título
        var titleObj = new GameObject("Title");
        titleObj.transform.SetParent(settingsPanel.transform, false);
        
        var titleRect = titleObj.AddComponent<RectTransform>();
        titleRect.anchorMin = new Vector2(0.5f, 0.8f);
        titleRect.anchorMax = new Vector2(0.5f, 0.8f);
        titleRect.pivot = Vector2.one * 0.5f;
        titleRect.sizeDelta = new Vector2(400, 80);
        
        var titleText = titleObj.AddComponent<UnityEngine.UI.Text>();
        titleText.text = "⚙️ CONFIGURACIÓN";
        titleText.fontSize = 36;
        titleText.color = Color.yellow;
        titleText.alignment = TextAnchor.MiddleCenter;
        titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        
        // Instrucciones
        var instrObj = new GameObject("Instructions");
        instrObj.transform.SetParent(settingsPanel.transform, false);
        
        var instrRect = instrObj.AddComponent<RectTransform>();
        instrRect.anchorMin = new Vector2(0.5f, 0.5f);
        instrRect.anchorMax = new Vector2(0.5f, 0.5f);
        instrRect.pivot = Vector2.one * 0.5f;
        instrRect.sizeDelta = new Vector2(600, 200);
        
        var instrText = instrObj.AddComponent<UnityEngine.UI.Text>();
        instrText.text = "🎮 Configuración:\n\n" +
                        "Aquí puedes cambiar ajustes del juego\n\n" +
                        "Usa los gestos asignados\n" +
                        "para navegar y confirmar";
        instrText.fontSize = 24;
        instrText.color = Color.white;
        instrText.alignment = TextAnchor.MiddleCenter;
        instrText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
    
    void CreateUniversalPanelManager()
    {
        var managerObj = new GameObject("UniversalPanelManager");
        var manager = managerObj.AddComponent<UniversalPanelManager>();
        
        // Debug.Log("✅ UniversalPanelManager creado automáticamente");
        
        // Pequeña espera para que se inicialice y luego intentar de nuevo
        StartCoroutine(DelayedShowSettings());
    }
    
    System.Collections.IEnumerator DelayedShowSettings()
    {
        yield return new WaitForEndOfFrame();
        if (UniversalPanelManager.Instance != null)
        {
            UniversalPanelManager.Instance.ShowPanel("SettingsPanel");
        }
    }

    // Asigna este método al botón "Salir"
    public void OnQuitClicked()
    {
        // Debug.Log("Saliendo de la aplicación...");
        Application.Quit();
    }

    // Método para volver al menú principal desde configuración
    public void OnBackToMenuClicked()
    {
        // Usar UniversalPanelManager para manejar el regreso.
        if (UniversalPanelManager.Instance != null)
        {
            UniversalPanelManager.Instance.ShowPanel("MenuPanel");
        }
        else
        {
            Debug.LogError("No se encontró UniversalPanelManager en la escena. No se puede volver al menú principal.");
        }
    }

    private async Task<bool> PingServerUntilReady(string url, float timeout)
    {
        float startTime = Time.time;
        while (Time.time - startTime < timeout)
        {
            var testSocket = new WebSocket(url);
            var tcs = new TaskCompletionSource<bool>();

            testSocket.OnOpen += () => tcs.TrySetResult(true);
            testSocket.OnError += (e) => tcs.TrySetResult(false);
            testSocket.OnClose += (code) => {
                // Si la tarea aún no se ha completado (por ejemplo, si el servidor cierra la conexión al instante),
                // la marcamos como fallida para evitar un bloqueo.
                tcs.TrySetResult(false);
            };

            testSocket.Connect();
            
            bool success = await tcs.Task;
            
            if (success)
            {
                // ¡Éxito! La conexión se estableció.
                // Cerramos el socket de prueba pero NO esperamos (await) la respuesta.
                // Esto evita la excepción "Operation Aborted" si el servidor no responde al cierre a tiempo.
                testSocket.Close();
                return true;
            }

            // Pequeña espera antes del siguiente intento para no saturar.
            await Task.Delay(250);
        }

        Debug.LogWarning($"El sondeo a {url} falló tras {timeout} segundos.");
        return false;
    }
} 