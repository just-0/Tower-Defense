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
    
    public SAMSystemController SAM;
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
    private bool waitingForServerResponse = false;
    private bool waitingForCombatResponse = false;
    private float serverResponseTimer = 0f;
    private const float serverResponseTimeout = 10f;
    
    private const float requiredHoldTime = 3f;
    private const float phaseChangeCooldown = 2f;

    [SerializeField] private float connectionTimeout = 5f;
    private float connectionTimer = 0f;
    private bool connectionChecked = false;
    private bool fingerCameraConnected = false;

    async void Start()
    {
        receivedTexture = new Texture2D(2, 2);
        if (SAM == null) 
        {
            //Debug.LogError("GestureReceiver: SAMController (SAMSystemController) no está asignado.");
        }
        else if (!(SAM is SAMSystemController))
        {
            //Debug.LogWarning("GestureReceiver: La referencia SAM no es del tipo SAMSystemController. Verifica la asignación en el Inspector.");
        }

        websocket = new WebSocket("ws://localhost:8768");

        websocket.OnOpen += () => { /*Debug.Log("Conexión abierta al servidor de rastreo de dedos");*/ };
        websocket.OnError += (errorMsg) => { /*Debug.LogError($"Error en WebSocket: {errorMsg}");*/ };
        websocket.OnClose += (code) => {
            //Debug.Log($"Conexión cerrada con código: {code}");
            
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

        if (!connectionChecked)
        {
            connectionTimer += Time.deltaTime;
            
            if (connectionTimer >= connectionTimeout)
            {
                connectionChecked = true;
                
                if (!fingerCameraConnected)
                {
                    //Debug.LogWarning("No se detectó conexión con la cámara de dedos. Redirigiendo a la escena de verificación.");
                    SceneManager.LoadScene("CameraVerification");
                }
            }
        }

        HandleGestureDetection();
        UpdatePhaseDisplay();
    }

    private void HandleGestureDetection()
    {
        if (waitingForServerResponse)
        {
            serverResponseTimer += Time.deltaTime;
            
            if (serverResponseTimer >= serverResponseTimeout)
            {
                //Debug.LogWarning("Timeout esperando respuesta del servidor - reactivando gestos");
                waitingForServerResponse = false;
                serverResponseTimer = 0f;
            }
            else
            {
                if (holdProgressSlider != null) holdProgressSlider.value = 0f;
                return;
            }
        }
        
        bool isPlanningGesture = currentPhase == GamePhase.Planning && (currentFingerCount == 3 || currentFingerCount == 5);
        bool isCombatExitGesture = currentPhase == GamePhase.Combat && currentFingerCount == 4;
        bool validGesture = isPlanningGesture || isCombatExitGesture;

        if (validGesture)
        {
            holdTimer += Time.deltaTime;
            
            if (holdTimer >= requiredHoldTime && !actionTriggered)
            {
                actionTriggered = true;
                holdTimer = 0f;
                
                if (currentFingerCount == 3)
                {
                    waitingForServerResponse = true;
                    waitingForCombatResponse = false;
                    serverResponseTimer = 0f;
                    currentFingerCount = 0;
                    if (SAM != null) SAM.SendMessage("PROCESS_SAM");
                    else { /*Debug.LogError("GestureReceiver: SAM es nulo, no se puede enviar PROCESS_SAM");*/ }
                }
                else if (currentFingerCount == 5)
                {
                    ChangePhase(GamePhase.Combat);
                    waitingForServerResponse = true;
                    waitingForCombatResponse = true;
                    serverResponseTimer = 0f;
                    if (SAM != null) SAM.SendMessage("START_COMBAT");
                    else { /*Debug.LogError("GestureReceiver: SAM es nulo, no se puede enviar START_COMBAT");*/ }
                }
                else if (currentFingerCount == 4)
                {
                    ChangePhase(GamePhase.Planning);
                    if (SAM != null) SAM.SendMessage("STOP_COMBAT");
                    else { /*Debug.LogError("GestureReceiver: SAM es nulo, no se puede enviar STOP_COMBAT");*/ }
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
        if (currentFingerCount >= 1 && currentFingerCount <= 3)
        {
            if (TurretManager.Instance != null)
            {
                TurretManager.Instance.SelectTurret(currentFingerCount - 1);
            }
        }
    }

    private void UpdatePhaseDisplay()
    {
        if (phaseDisplayText != null)
        {
            string statusText = "";
            if (waitingForServerResponse)
            {
                if (waitingForCombatResponse)
                {
                    statusText = "Iniciando modo combate...";
                }
                else
                {
                    statusText = "Procesando SAM...";
                }
            }
            
            phaseDisplayText.text = currentPhase == GamePhase.Planning
                ? $"Fase: Planificación\n3 dedos: Procesar SAM\n5 dedos: Ir a Combate\nProgreso: {(holdTimer/requiredHoldTime*100).ToString("0")}%\n{statusText}"
                : $"Fase: Combate\nDedos detectados: {currentFingerCount}\n(4 dedos para salir)";
        }
    }
    
    private void ChangePhase(GamePhase newPhase)
    {
        if (currentPhase == newPhase) return;

        currentPhase = newPhase;
        //Debug.Log($"Cambiando a fase: {currentPhase}");
        
        if (UIManager.Instance != null)
        {
            UIManager.Instance.SetCombatUIVisibility(currentPhase == GamePhase.Combat);
        }
    }

    public void OnServerResponseReceived()
    {
        waitingForServerResponse = false;
        waitingForCombatResponse = false;
        serverResponseTimer = 0f;
        //Debug.Log("Respuesta del servidor recibida - gestos habilitados nuevamente");
    }

    public void OnCombatModeStarted()
    {
        if (waitingForCombatResponse)
        {
            waitingForServerResponse = false;
            waitingForCombatResponse = false;
            serverResponseTimer = 0f;
            //Debug.Log("Modo combate iniciado - gestos habilitados nuevamente");
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