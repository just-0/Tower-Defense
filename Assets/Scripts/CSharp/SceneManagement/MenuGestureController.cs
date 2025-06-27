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
    
    // --- INICIO DE CAMBIOS PARA PERSISTENCIA ---
    public static MenuGestureController Instance { get; private set; }
    // --- FIN DE CAMBIOS PARA PERSISTENCIA ---

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

    void Awake()
    {
        // --- INICIO DE CAMBIOS PARA PERSISTENCIA ---
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(transform.root.gameObject); // Hacemos que todo el objeto Managers persista
        }
        else if (Instance != this)
        {
            // Si ya existe una instancia (de una escena anterior), destruimos este nuevo objeto duplicado.
            Destroy(transform.root.gameObject);
            return;
        }
        // --- FIN DE CAMBIOS PARA PERSISTENCIA ---
    }

    async void Start()
    {
        // La UI se asignará dinámicamente, por lo que las referencias iniciales pueden ser nulas.
        if (loadingPanel != null) loadingPanel.SetActive(true);
        if (cameraFeed != null) cameraFeed.gameObject.SetActive(false);

        receivedTexture = new Texture2D(2, 2);
        websocket = new WebSocket("ws://localhost:8766"); // Conectar al nuevo puerto del menú

        websocket.OnOpen += () => {}; // Debug.Log("Conectado al servidor de gestos del menú.");
        websocket.OnError += (e) => Debug.LogError($"Error en WebSocket del menú: {e}");
        websocket.OnMessage += OnMessageReceived;

        // Configurar el sistema simple de gestos si está habilitado
        if (useSimpleGestureSystem && simpleGestureManager != null)
        {
            // Debug.Log("Sistema simple de gestos activado");
        }

        await websocket.Connect();

        #if !UNITY_WEBGL || UNITY_EDITOR
            websocket?.DispatchMessageQueue();
        #endif

        // Ya no es necesario manejar aquí los gestos antiguos,
        // el sistema se ha modularizado completamente.
        // HandleGestureActions();
    }

    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
            websocket?.DispatchMessageQueue();
        #endif

        // Ya no es necesario manejar aquí los gestos antiguos,
        // el sistema se ha modularizado completamente.
        // HandleGestureActions();
    }

    private void OnMessageReceived(byte[] bytes)
    {
        if (bytes.Length == 0) return;
        
        byte messageType = bytes[0];
        byte[] messageData = new byte[bytes.Length - 1];
        Buffer.BlockCopy(bytes, 1, messageData, 0, bytes.Length - 1);

        if (UnityMainThreadDispatcher.Instance() == null)
        {
            Debug.LogError("FATAL: UnityMainThreadDispatcher no encontrado. Añade el script a tu GlobalManagers en la escena 0_Boot.");
            return;
        }

        UnityMainThreadDispatcher.Instance().Enqueue(() => {
            switch (messageType)
            {
                case MESSAGE_TYPE_CAMERA_FRAME:
                    // Si es el primer frame, ocultamos el panel de carga
                    if (!firstFrameReceived)
                    {
                        firstFrameReceived = true;
                        if (loadingPanel != null) loadingPanel.SetActive(false);
                        if (cameraFeed != null) cameraFeed.gameObject.SetActive(true);
                    }
                    // Debug.Log($"Recibido frame de cámara ({messageData.Length} bytes).");
                    receivedTexture.LoadImage(messageData);
                    if(cameraFeed) cameraFeed.texture = receivedTexture;
                    break;
                case MESSAGE_TYPE_FINGER_COUNT:
                    string jsonStr = Encoding.UTF8.GetString(messageData);
                    FingerCountData data = JsonUtility.FromJson<FingerCountData>(jsonStr);
                    currentFingerCount = data.count;
                    if(fingerCountText) fingerCountText.text = $"Dedos: {data.count}";
                    
                    // Actualizar el sistema simple de gestos
                    if (useSimpleGestureSystem && simpleGestureManager != null)
                    {
                        simpleGestureManager.UpdateFingerCount(data.count);
                    }
                    break;
                case MESSAGE_TYPE_SERVER_STATUS:
                    // Debug.Log("Recibido mensaje de estado del servidor en el menú.");
                    break;
                case MESSAGE_TYPE_CAMERA_LIST:
                    string camListJson = Encoding.UTF8.GetString(messageData);
                    CameraListData camData = JsonUtility.FromJson<CameraListData>(camListJson);
                    if (settingsController != null)
                    {
                        settingsController.SetAvailableCameras(camData.available_cameras);
                    }
                    break;
            }
        });
    }

    // --- MÉTODOS PÚBLICOS PARA ENLACE DE UI ---

    /// <summary>
    /// Permite que un 'Binder' de UI de una escena registre sus elementos en este gestor persistente.
    /// </summary>
    public void RegisterUI(RawImage newCameraFeed, Text newFingerCountText, GameObject newLoadingPanel)
    {
        Debug.Log("MenuGestureController: Registrando nueva UI desde la escena actual.");
        cameraFeed = newCameraFeed;
        fingerCountText = newFingerCountText;
        loadingPanel = newLoadingPanel;

        // Aplicamos el estado actual a la nueva UI para que todo se vea consistente.
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
        // Limpiamos las referencias para no apuntar a objetos destruidos.
        Debug.Log("MenuGestureController: Des-registrando UI de la escena anterior.");
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
            // Debug.Log($"Solicitando cambio de cámara al índice {newIndex}");
        }
    }

    private void HandleGestureActions()
    {
        // Si está usando el sistema simple, no procesar la lógica antigua
        if (useSimpleGestureSystem && simpleGestureManager != null)
        {
            return; // El SimpleGestureManager maneja toda la lógica
        }

        // Doble seguridad: si el sistema simple no se usa pero no hay controlador de menú, no hacer nada.
        if (mainMenuController == null)
        {
            return;
        }

        // Lógica del sistema antiguo (mantenida para compatibilidad)
        bool validGesture = currentFingerCount == 1 || currentFingerCount == 3 || currentFingerCount == 5;

        if (validGesture && !actionTriggered)
        {
            holdTimer += Time.deltaTime;
            if (holdProgressSlider) holdProgressSlider.value = holdTimer / REQUIRED_HOLD_TIME;

            if (holdTimer >= REQUIRED_HOLD_TIME)
            {
                actionTriggered = true; // Evita que se dispare varias veces
                
                if (currentFingerCount == 1)
                {
                    // Debug.Log("Gesto 'Un Jugador' detectado.");
                    mainMenuController.OnSinglePlayerClicked();
                }
                else if (currentFingerCount == 3)
                {
                    // Debug.Log("Gesto 'Multijugador' detectado.");
                    mainMenuController.OnMultiplayerClicked();
                }
                else if (currentFingerCount == 5)
                {
                    // Debug.Log("Gesto 'Salir' detectado.");
                    mainMenuController.OnQuitClicked();
                }
            }
        }
        else
        {
            // Si el gesto se suelta, reseteamos el temporizador y el estado
            holdTimer = 0;
            if (holdProgressSlider) holdProgressSlider.value = 0;
            if (!validGesture)
            {
                actionTriggered = false;
            }
        }
    }
    
    private async void OnDestroy()
    {
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