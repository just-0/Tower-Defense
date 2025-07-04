using UnityEngine;
using UnityEngine.UI;
using NativeWebSocket;
using System.Text;
using System;

public class MenuGestureController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private RawImage cameraFeed;
    [SerializeField] private GameObject loadingPanel; // Panel que se muestra mientras carga
    [SerializeField] private Text fingerCountText;
    [SerializeField] private Slider holdProgressSlider; // Mantenido para compatibilidad
    [SerializeField] private MainMenuController mainMenuController; // Referencia al controlador del menú
    [SerializeField] private SettingsController settingsController; // Referencia directa al SettingsController
    
    [Header("Simple Gesture System")]
    [SerializeField] private SimpleGestureManager simpleGestureManager; // Sistema simple de gestos
    [SerializeField] private bool useSimpleGestureSystem = true; // Toggle para usar el sistema simple

    private WebSocket websocket;
    private Texture2D receivedTexture;
    
    // --- SINGLETON PERSISTENTE ---
    public static MenuGestureController Instance { get; private set; }

    private const byte MESSAGE_TYPE_CAMERA_FRAME = 1;
    private const byte MESSAGE_TYPE_FINGER_COUNT = 5;
    private const byte MESSAGE_TYPE_SERVER_STATUS = 8;
    private const byte MESSAGE_TYPE_SWITCH_CAMERA = 9;
    private const byte MESSAGE_TYPE_CAMERA_LIST = 10;

    private int currentFingerCount = 0;
    private float holdTimer = 0f;
    private bool actionTriggered = false;
    private bool firstFrameReceived = false; // Flag para controlar la carga
    private const float REQUIRED_HOLD_TIME = 2.5f; // Segundos para mantener el gesto
    
    // Control de estado de conexión
    private bool isConnecting = false;
    private bool isDestroyed = false;

    void Awake()
    {
        // Patrón Singleton corregido
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject); // Corregido: aplicar al gameObject actual, no al root
            Debug.Log("MenuGestureController: Instancia creada y marcada como persistente");
        }
        else if (Instance != this)
        {
            // Si ya existe una instancia, destruir esta nueva
            Debug.Log("MenuGestureController: Destruyendo instancia duplicada");
            Destroy(gameObject);
            return;
        }
    }

    async void Start()
    {
        // Solo ejecutar Start si esta es la instancia válida
        if (Instance != this || isDestroyed) return;
        
        await InitializeGestureController();
    }
    
    private async System.Threading.Tasks.Task InitializeGestureController()
    {
        if (isConnecting) return; // Evitar múltiples conexiones simultáneas
        
        isConnecting = true;
        
        Debug.Log("MenuGestureController: Inicializando...");
        
        // Inicializar UI si existe
        if (loadingPanel != null) loadingPanel.SetActive(true);
        if (cameraFeed != null) cameraFeed.gameObject.SetActive(false);

        receivedTexture = new Texture2D(2, 2);
        
        // Configurar el sistema simple de gestos si está habilitado
        if (useSimpleGestureSystem && simpleGestureManager != null)
        {
            Debug.Log("MenuGestureController: Sistema simple de gestos activado");
        }

        await ConnectToMenuServer();
        
        isConnecting = false;
    }
    
    private async System.Threading.Tasks.Task ConnectToMenuServer()
    {
        try
        {
            // Cerrar conexión anterior si existe
            if (websocket != null && websocket.State == WebSocketState.Open)
            {
                await websocket.Close();
            }
            
            websocket = new WebSocket("ws://localhost:8766"); // Puerto del menú

            websocket.OnOpen += () => {
                Debug.Log("MenuGestureController: Conectado al servidor de gestos del menú");
            };
            
            websocket.OnError += (e) => {
                Debug.LogError($"MenuGestureController: Error en WebSocket del menú: {e}");
            };
            
            websocket.OnClose += (code) => {
                Debug.Log($"MenuGestureController: WebSocket cerrado con código: {code}");
            };
            
            websocket.OnMessage += OnMessageReceived;

            await websocket.Connect();
        }
        catch (Exception e)
        {
            Debug.LogError($"MenuGestureController: Error al conectar: {e.Message}");
        }
    }

    void Update()
    {
        // Solo ejecutar Update si esta es la instancia válida
        if (Instance != this || isDestroyed) return;
        
        #if !UNITY_WEBGL || UNITY_EDITOR
            websocket?.DispatchMessageQueue();
        #endif
    }

    private void OnMessageReceived(byte[] bytes)
    {
        if (bytes.Length == 0 || isDestroyed) return;
        
        byte messageType = bytes[0];
        byte[] messageData = new byte[bytes.Length - 1];
        Buffer.BlockCopy(bytes, 1, messageData, 0, bytes.Length - 1);

        if (UnityMainThreadDispatcher.Instance() == null)
        {
            Debug.LogError("MenuGestureController: UnityMainThreadDispatcher no encontrado. Añade el script a tu GlobalManagers en la escena 0_Boot.");
            return;
        }

        UnityMainThreadDispatcher.Instance().Enqueue(() => {
            ProcessMessage(messageType, messageData);
        });
    }
    
    private void ProcessMessage(byte messageType, byte[] messageData)
    {
        if (isDestroyed) return;
        
        switch (messageType)
        {
            case MESSAGE_TYPE_CAMERA_FRAME:
                ProcessCameraFrame(messageData);
                break;
            case MESSAGE_TYPE_FINGER_COUNT:
                ProcessFingerCount(messageData);
                break;
            case MESSAGE_TYPE_SERVER_STATUS:
                Debug.Log("MenuGestureController: Estado del servidor recibido");
                break;
            case MESSAGE_TYPE_CAMERA_LIST:
                ProcessCameraList(messageData);
                break;
        }
    }
    
    private void ProcessCameraFrame(byte[] messageData)
    {
        // Si es el primer frame, ocultar el panel de carga
        if (!firstFrameReceived)
        {
            firstFrameReceived = true;
            if (loadingPanel != null) loadingPanel.SetActive(false);
            if (cameraFeed != null) cameraFeed.gameObject.SetActive(true);
        }
        
        if (receivedTexture != null)
        {
            receivedTexture.LoadImage(messageData);
            if (cameraFeed != null) cameraFeed.texture = receivedTexture;
        }
    }
    
    private void ProcessFingerCount(byte[] messageData)
    {
        string jsonStr = Encoding.UTF8.GetString(messageData);
        FingerCountData data = JsonUtility.FromJson<FingerCountData>(jsonStr);
        currentFingerCount = data.count;
        
        if (fingerCountText != null) 
            fingerCountText.text = $"Dedos: {data.count}";
        
        // Actualizar el sistema simple de gestos
        if (useSimpleGestureSystem && simpleGestureManager != null)
        {
            simpleGestureManager.UpdateFingerCount(data.count);
        }
    }
    
    private void ProcessCameraList(byte[] messageData)
    {
        string camListJson = Encoding.UTF8.GetString(messageData);
        CameraListData camData = JsonUtility.FromJson<CameraListData>(camListJson);
        if (settingsController != null)
        {
            settingsController.SetAvailableCameras(camData.available_cameras);
        }
    }

    // --- MÉTODOS PÚBLICOS PARA ENLACE DE UI ---

    /// <summary>
    /// Permite que un 'Binder' de UI de una escena registre sus elementos en este gestor persistente.
    /// </summary>
    public void RegisterUI(RawImage newCameraFeed, Text newFingerCountText, GameObject newLoadingPanel)
    {
        Debug.Log("MenuGestureController: Registrando nueva UI desde la escena actual");
        cameraFeed = newCameraFeed;
        fingerCountText = newFingerCountText;
        loadingPanel = newLoadingPanel;

        // Aplicar el estado actual a la nueva UI para que todo se vea consistente
        if (cameraFeed != null)
        {
            cameraFeed.texture = receivedTexture;
            cameraFeed.gameObject.SetActive(firstFrameReceived);
        }
        if (loadingPanel != null)
        {
            loadingPanel.SetActive(!firstFrameReceived);
        }
        if (fingerCountText != null)
        {
            fingerCountText.text = $"Dedos: {currentFingerCount}";
        }
    }

    /// <summary>
    /// Limpia las referencias de UI para evitar errores cuando se deja una escena.
    /// </summary>
    public void UnregisterUI(MenuUIBinder binder)
    {
        Debug.Log("MenuGestureController: Des-registrando UI de la escena anterior");
        cameraFeed = null;
        fingerCountText = null;
        loadingPanel = null;
    }

    public async void RequestCameraSwitch(int newIndex)
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            SwitchCameraData data = new SwitchCameraData { index = newIndex };
            string json = JsonUtility.ToJson(data);
            
            byte[] messageBytes = Encoding.UTF8.GetBytes(json);
            byte[] finalMessage = new byte[messageBytes.Length + 1];
            
            finalMessage[0] = MESSAGE_TYPE_SWITCH_CAMERA;
            Buffer.BlockCopy(messageBytes, 0, finalMessage, 1, messageBytes.Length);

            await websocket.Send(finalMessage);
            Debug.Log($"MenuGestureController: Solicitando cambio de cámara al índice {newIndex}");
        }
    }
    
    /// <summary>
    /// Método público para reconectar si es necesario (llamado desde el exterior)
    /// </summary>
    public async void ReconnectIfNeeded()
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
        {
            Debug.Log("MenuGestureController: Reconectando al servidor de gestos...");
            await ConnectToMenuServer();
        }
    }

    void OnDestroy()
    {
        isDestroyed = true;
        
        // Solo limpiar si esta es la instancia del Singleton
        if (Instance == this)
        {
            Debug.Log("MenuGestureController: Limpiando instancia singleton");
            Instance = null;
        }
    }
    
    async void OnApplicationQuit()
    {
        isDestroyed = true;
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await websocket.Close();
        }
    }

    [Serializable]
    private class FingerCountData { public int count; }
    [Serializable]
    private class SwitchCameraData { public int index; }
    [Serializable]
    private class CameraListData { public System.Collections.Generic.List<int> available_cameras; }
} 