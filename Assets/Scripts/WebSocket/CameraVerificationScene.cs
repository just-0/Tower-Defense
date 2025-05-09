using System;
using UnityEngine;
using UnityEngine.UI;
using NativeWebSocket;
using System.Text;
using UnityEngine.SceneManagement;

public class CameraVerificationScene : MonoBehaviour
{
    [Header("Configuración de WebSockets")]
    [SerializeField] private string mainServerUrl = "ws://localhost:8767";
    [SerializeField] private string fingerTrackingServerUrl = "ws://localhost:8768";
    
    [Header("Elementos UI")]
    [SerializeField] private RawImage mainCameraDisplay;
    [SerializeField] private RawImage fingerCameraDisplay;
    [SerializeField] private GameObject mainCameraStatus;
    [SerializeField] private GameObject fingerCameraStatus;
    [SerializeField] private Text statusText;
    [SerializeField] private Text countdownText;
    [SerializeField] private Image backgroundPanel;
    
    [Header("Configuración")]
    [SerializeField] private float autoStartCountdown = 20f; // 20 segundos para debug
    [SerializeField] private string mainGameSceneName = "MainGame";
    
    // Referencias a WebSockets
    private WebSocket mainWebsocket;
    private WebSocket fingerWebsocket;
    
    // Texturas para las cámaras
    private Texture2D mainCameraTexture;
    private Texture2D fingerCameraTexture;
    
    // Estado de conexión
    private bool mainCameraConnected = false;
    private bool fingerCameraConnected = false;
    private bool isConnecting = false;
    
    // Contador para inicio automático
    private float countdownTimer = 0f;
    private bool countdownActive = false;
    
    // Constantes para tipos de mensajes
    private const byte MESSAGE_TYPE_CAMERA_FRAME = 1;
    private const byte MESSAGE_TYPE_FINGER_COUNT = 5;
    
    // Tiempo para reconexión automática
    private float reconnectTimer = 0f;
    private const float reconnectInterval = 5f;
    
    async void Start()
    {
        // Configurar fondo negro
        if (backgroundPanel != null)
        {
            backgroundPanel.color = Color.black;
        }
        
        // Inicializar texturas
        mainCameraTexture = new Texture2D(2, 2);
        fingerCameraTexture = new Texture2D(2, 2);
        
        // Asignar texturas a los RawImage
        if (mainCameraDisplay != null)
            mainCameraDisplay.texture = mainCameraTexture;
        
        if (fingerCameraDisplay != null)
            fingerCameraDisplay.texture = fingerCameraTexture;
        
        // Ocultar texto de cuenta regresiva
        if (countdownText != null)
            countdownText.gameObject.SetActive(false);
        
        // Actualizar estado inicial
        UpdateStatusVisuals();
        
        // Iniciar conexiones
        await ConnectToServers();
    }
    
    async System.Threading.Tasks.Task ConnectToServers()
    {
        isConnecting = true;
        
        // Conectar al servidor principal
        try
        {
            mainWebsocket = new WebSocket(mainServerUrl);
            
            mainWebsocket.OnOpen += () => {
                Debug.Log("Conexión abierta al servidor principal");
                mainWebsocket.SendText("START_CAMERA");
            };
            
            mainWebsocket.OnError += (e) => {
                Debug.LogError($"Error en WebSocket principal: {e}");
                mainCameraConnected = false;
                UpdateStatusVisuals();
                ResetCountdown();
            };
            
            mainWebsocket.OnClose += (e) => {
                Debug.Log($"Conexión cerrada al servidor principal: {e}");
                mainCameraConnected = false;
                UpdateStatusVisuals();
                ResetCountdown();
            };
            
            mainWebsocket.OnMessage += (bytes) => ProcessMainServerMessage(bytes);
            
            await mainWebsocket.Connect();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error al conectar al servidor principal: {e.Message}");
            mainCameraConnected = false;
            UpdateStatusVisuals();
        }
        
        // Conectar al servidor de seguimiento de dedos
        try
        {
            fingerWebsocket = new WebSocket(fingerTrackingServerUrl);
            
            fingerWebsocket.OnOpen += () => {
                Debug.Log("Conexión abierta al servidor de rastreo de dedos");
            };
            
            fingerWebsocket.OnError += (e) => {
                Debug.LogError($"Error en WebSocket de rastreo de dedos: {e}");
                fingerCameraConnected = false;
                UpdateStatusVisuals();
                ResetCountdown();
            };
            
            fingerWebsocket.OnClose += (e) => {
                Debug.Log($"Conexión cerrada al servidor de rastreo de dedos: {e}");
                fingerCameraConnected = false;
                UpdateStatusVisuals();
                ResetCountdown();
            };
            
            fingerWebsocket.OnMessage += (bytes) => ProcessFingerServerMessage(bytes);
            
            await fingerWebsocket.Connect();
        }
        catch (Exception e)
        {
            Debug.LogError($"Error al conectar al servidor de rastreo de dedos: {e.Message}");
            fingerCameraConnected = false;
            UpdateStatusVisuals();
        }
        
        isConnecting = false;
    }
    
    private void ProcessMainServerMessage(byte[] bytes)
    {
        if (bytes.Length > 0)
        {
            byte messageType = bytes[0];
            byte[] messageData = new byte[bytes.Length - 1];
            Buffer.BlockCopy(bytes, 1, messageData, 0, bytes.Length - 1);
            
            if (messageType == MESSAGE_TYPE_CAMERA_FRAME)
            {
                mainCameraTexture.LoadImage(messageData);
                mainCameraTexture.Apply();
                mainCameraConnected = true;
                UpdateStatusVisuals();
                CheckBothCamerasConnected();
            }
        }
    }
    
    private void ProcessFingerServerMessage(byte[] bytes)
    {
        if (bytes.Length > 0)
        {
            byte messageType = bytes[0];
            byte[] messageData = new byte[bytes.Length - 1];
            Buffer.BlockCopy(bytes, 1, messageData, 0, bytes.Length - 1);
            
            switch (messageType)
            {
                case MESSAGE_TYPE_CAMERA_FRAME:
                    fingerCameraTexture.LoadImage(messageData);
                    fingerCameraTexture.Apply();
                    fingerCameraConnected = true;
                    UpdateStatusVisuals();
                    CheckBothCamerasConnected();
                    break;
                    
                case MESSAGE_TYPE_FINGER_COUNT:
                    // Recibir conteo de dedos confirma que la cámara está funcionando
                    fingerCameraConnected = true;
                    UpdateStatusVisuals();
                    CheckBothCamerasConnected();
                    break;
            }
        }
    }
    
    private void CheckBothCamerasConnected()
    {
        // Si ambas cámaras están conectadas, iniciar cuenta regresiva
        if (mainCameraConnected && fingerCameraConnected && !countdownActive)
        {
            StartCountdown();
            
            // Guardar el estado de las cámaras
            InitialSceneLoader.SetCamerasConnected(true);
        }
    }
    
    private void StartCountdown()
    {
        countdownActive = true;
        countdownTimer = autoStartCountdown;
        
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            UpdateCountdownText();
        }
        
        Debug.Log("Iniciando cuenta regresiva para cargar el juego...");
    }
    
    private void ResetCountdown()
    {
        countdownActive = false;
        countdownTimer = 0;
        
        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(false);
        }
    }
    
    private void UpdateCountdownText()
    {
        if (countdownText != null)
        {
            countdownText.text = $"Iniciando juego en: {Mathf.CeilToInt(countdownTimer)}";
        }
    }
    
    void Update()
    {
        // Procesar mensajes WebSocket
        #if !UNITY_WEBGL || UNITY_EDITOR
        mainWebsocket?.DispatchMessageQueue();
        fingerWebsocket?.DispatchMessageQueue();
        #endif
        
        // Manejar cuenta regresiva
        if (countdownActive)
        {
            // Verificar que ambas cámaras sigan conectadas
            if (!mainCameraConnected || !fingerCameraConnected)
            {
                ResetCountdown();
                Debug.Log("Cuenta regresiva detenida: cámara desconectada");
            }
            else
            {
                // Actualizar temporizador
                countdownTimer -= Time.deltaTime;
                UpdateCountdownText();
                
                // Comprobar si es hora de cargar el juego
                if (countdownTimer <= 0)
                {
                    LoadMainGame();
                }
            }
        }
        
        // Intentar reconexión automática si es necesario
        if (!isConnecting && (!mainCameraConnected || !fingerCameraConnected))
        {
            reconnectTimer += Time.deltaTime;
            
            if (reconnectTimer >= reconnectInterval)
            {
                reconnectTimer = 0f;
                ConnectToServers();
            }
        }
        
        // Animar elementos visuales para indicar estado de conexión
        AnimateStatusElements();
    }
    
    private void UpdateStatusVisuals()
    {
        // Actualizar elementos visuales basados en el estado de conexión
        if (mainCameraStatus != null)
        {
            mainCameraStatus.GetComponent<Image>().color = mainCameraConnected ? Color.green : Color.red;
        }
        
        if (fingerCameraStatus != null)
        {
            fingerCameraStatus.GetComponent<Image>().color = fingerCameraConnected ? Color.green : Color.red;
        }
        
        // Actualizar texto de estado
        if (statusText != null)
        {
            if (mainCameraConnected && fingerCameraConnected)
            {
                statusText.text = "¡Todas las cámaras conectadas! Iniciando juego automáticamente...";
                statusText.color = Color.green;
            }
            else if (!mainCameraConnected && !fingerCameraConnected)
            {
                statusText.text = "Esperando conexión de ambas cámaras...";
                statusText.color = Color.red;
            }
            else
            {
                statusText.text = !mainCameraConnected ? 
                    "Esperando conexión de la cámara principal..." : 
                    "Esperando conexión de la cámara de rastreo de dedos...";
                statusText.color = Color.yellow;
            }
        }
    }
    
    private void AnimateStatusElements()
    {
        // Implementar animaciones para hacer más llamativo el estado de conexión
        // Por ejemplo, hacer parpadear los elementos que no están conectados
        if (mainCameraStatus != null && !mainCameraConnected)
        {
            float alpha = Mathf.PingPong(Time.time * 2, 1f);
            Color c = mainCameraStatus.GetComponent<Image>().color;
            c.a = 0.5f + alpha * 0.5f;
            mainCameraStatus.GetComponent<Image>().color = c;
        }
        
        if (fingerCameraStatus != null && !fingerCameraConnected)
        {
            float alpha = Mathf.PingPong(Time.time * 2, 1f);
            Color c = fingerCameraStatus.GetComponent<Image>().color;
            c.a = 0.5f + alpha * 0.5f;
            fingerCameraStatus.GetComponent<Image>().color = c;
        }
        
        // Animar texto de cuenta regresiva
        if (countdownText != null && countdownActive)
        {
            countdownText.color = new Color(
                1f, 
                Mathf.Lerp(0.5f, 1f, countdownTimer / autoStartCountdown),
                0f
            );
            
            // Efecto de escala pulsante
            float scale = 1f + 0.2f * Mathf.Sin(Time.time * 3f);
            countdownText.transform.localScale = new Vector3(scale, scale, 1f);
        }
    }
    
    private void LoadMainGame()
    {
        Debug.Log("Cargando escena principal del juego...");
        
        // Guardar el estado de las cámaras antes de cargar la escena principal
        InitialSceneLoader.SetCamerasConnected(true);
        
        SceneManager.LoadScene(mainGameSceneName);
    }
    
    [Serializable]
    private class FingerCountData { public int count; }
    
    private async void OnApplicationQuit()
    {
        // Cerrar conexiones WebSocket al salir
        if (mainWebsocket != null && mainWebsocket.State == WebSocketState.Open)
            await mainWebsocket.Close();
            
        if (fingerWebsocket != null && fingerWebsocket.State == WebSocketState.Open)
            await fingerWebsocket.Close();
    }
} 