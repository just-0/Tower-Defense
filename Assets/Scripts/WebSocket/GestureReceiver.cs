using System;
using UnityEngine;
using UnityEngine.UI;
using NativeWebSocket;
using System.Text;
using UnityEngine.SceneManagement;

public class GestureReceiver : MonoBehaviour
{
    [SerializeField] private RawImage gestureImage;
    [SerializeField] private Text fingerCountText;
    [SerializeField] private Slider holdProgressSlider;
    [SerializeField] private Text phaseDisplayText;
    
    public SAMController SAM;
    private WebSocket websocket;
    private Texture2D receivedTexture;
    
    private const byte MESSAGE_TYPE_CAMERA_FRAME = 1;
    private const byte MESSAGE_TYPE_FINGER_COUNT = 5;

    public enum GamePhase { Planning, Combat }
    private GamePhase currentPhase = GamePhase.Planning;
    
    private int currentFingerCount = 0;
    private int lastValidCount = 0;
    private float holdTimer = 0f;
    private bool actionTriggered = false;
    private float cooldownTimer = 0f;
    
    private const float requiredHoldTime = 3f;
    private const float cooldownDuration = 45f;
    private const float phaseChangeCooldown = 2f;

    [SerializeField] private float connectionTimeout = 5f;
    private float connectionTimer = 0f;
    private bool connectionChecked = false;
    private bool fingerCameraConnected = false;

    async void Start()
    {
        receivedTexture = new Texture2D(2, 2);
        websocket = new WebSocket("ws://localhost:8768");

        websocket.OnOpen += () => Debug.Log("Conexión abierta al servidor de rastreo de dedos");
        websocket.OnError += (errorMsg) => Debug.LogError($"Error en WebSocket: {errorMsg}");
        websocket.OnClose += (code) => {
            Debug.Log($"Conexión cerrada con código: {code}");
            
            // Marcar las cámaras como desconectadas
            InitialSceneLoader.SetCamerasConnected(false);
        };
        
        websocket.OnMessage += (bytes) => 
        {
            if (bytes.Length > 0)
            {
                byte messageType = bytes[0];
                byte[] messageData = new byte[bytes.Length - 1];
                Buffer.BlockCopy(bytes, 1, messageData, 0, bytes.Length - 1);

                switch (messageType)
                {
                    case MESSAGE_TYPE_CAMERA_FRAME:
                        ProcessCameraFrame(messageData);
                        fingerCameraConnected = true;
                        break;
                    case MESSAGE_TYPE_FINGER_COUNT:
                        ProcessFingerCount(messageData);
                        fingerCameraConnected = true;
                        break;
                }
            }
        };

        await websocket.Connect();
        UpdatePhaseDisplay();
    }

    void Update()
    {
    #if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
    #endif

        // Verificar si la cámara de dedos está conectada
        if (!connectionChecked)
        {
            connectionTimer += Time.deltaTime;
            
            if (connectionTimer >= connectionTimeout)
            {
                connectionChecked = true;
                
                // Si no se ha recibido ningún frame de la cámara de dedos después del timeout,
                // redirigir a la escena de verificación de cámaras
                if (!fingerCameraConnected)
                {
                    Debug.LogWarning("No se detectó conexión con la cámara de dedos. Redirigiendo a la escena de verificación.");
                    SceneManager.LoadScene("CameraVerification");
                }
            }
        }

        if (cooldownTimer > 0f)
        {
            cooldownTimer -= Time.deltaTime;
            if (holdProgressSlider != null) holdProgressSlider.value = 0f;
            return;
        }

        HandleGestureDetection();
        UpdatePhaseDisplay();
    }

    private void HandleGestureDetection()
    {
        bool validGesture = (currentPhase == GamePhase.Planning && currentFingerCount == 3) || 
                          (currentPhase == GamePhase.Planning && currentFingerCount == 5);

        if (validGesture)
        {
            holdTimer += Time.deltaTime;
            
            if (holdTimer >= requiredHoldTime && !actionTriggered)
            {
                actionTriggered = true;
                holdTimer = 0f;
                
                if (currentFingerCount == 3)
                {
                    cooldownTimer = cooldownDuration;
                    SAM.SendMessage("PROCESS_SAM");
                }
                else if (currentFingerCount == 5)
                {
                    currentPhase = GamePhase.Combat;
                    cooldownTimer = phaseChangeCooldown;
                    SAM.SendMessage("START_COMBAT"); // Nuevo mensaje

                }
            }
        }
        else
        {
            holdTimer = Mathf.Max(0f, holdTimer - Time.deltaTime * 2);
            actionTriggered = false;
        }

        if (holdProgressSlider != null)
        {
            holdProgressSlider.gameObject.SetActive(currentPhase == GamePhase.Planning);
            holdProgressSlider.value = Mathf.Clamp(holdTimer / requiredHoldTime, 0f, 1f);
        }
    }

    private void ProcessCameraFrame(byte[] imageData)
    {
        receivedTexture.LoadImage(imageData);
        gestureImage.texture = receivedTexture;
    }

    private void ProcessFingerCount(byte[] jsonData)
    {
        string jsonStr = Encoding.UTF8.GetString(jsonData);
        FingerCountData data = JsonUtility.FromJson<FingerCountData>(jsonStr);

        UnityMainThreadDispatcher.Instance().Enqueue(() => 
        {
            if (data.count > 0) lastValidCount = data.count;
            currentFingerCount = data.count;
            
            if (fingerCountText != null)
                fingerCountText.text = $"Dedos: {lastValidCount} ({currentFingerCount})";
                
            if (currentPhase == GamePhase.Combat)
                HandleCombatActions();
        });
    }

    private void HandleCombatActions()
    {
        // Espacio para implementar lógica de combate
       // Debug.Log($"Acción de combate con {currentFingerCount} dedos");
        
       
    }

    private void UpdatePhaseDisplay()
    {
        if (phaseDisplayText != null)
        {
            phaseDisplayText.text = currentPhase == GamePhase.Planning
                ? $"Fase: Planificación\n3 dedos: Procesar SAM\n5 dedos: Ir a Combate\nProgreso: {(holdTimer/requiredHoldTime*100).ToString("0")}%"
                : $"Fase: Combate\nDedos detectados: {currentFingerCount}";
        }
    }

    [Serializable]
    private class FingerCountData { public int count; }

    private async void OnApplicationQuit()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
            await websocket.Close();
    }
}