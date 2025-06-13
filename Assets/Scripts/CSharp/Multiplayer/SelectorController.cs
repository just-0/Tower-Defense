using System;
using UnityEngine;
using UnityEngine.UI;
using NativeWebSocket;
using System.Text;
using System.Threading.Tasks;

public class SelectorController : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private RawImage gestureImage;
    [SerializeField] private Text statusText;
    [SerializeField] private Text fingerCountText;
    [SerializeField] private Slider holdProgressSlider;
    [SerializeField] private Text phaseText;

    [Header("Settings")]
    [SerializeField] private string gestureServerUrl = "ws://localhost:8768";
    [SerializeField] private float requiredHoldTime = 2.5f;
    [SerializeField] private float commandCooldown = 3.0f; // Time to wait after sending a command

    // WebSocket
    private WebSocket websocket;
    private Texture2D receivedTexture;
    
    // Game State
    private enum GamePhase { Planning, Combat }
    private GamePhase currentPhase = GamePhase.Planning;
    
    // Gesture State
    private int currentFingerCount = 0;
    private float holdTimer = 0f;
    private bool actionTriggered = false;
    private float cooldownTimer = 0f;

    private const byte MESSAGE_TYPE_CAMERA_FRAME = 1;
    private const byte MESSAGE_TYPE_FINGER_COUNT = 5;

    async void Start()
    {
        receivedTexture = new Texture2D(2, 2);
        if (gestureImage != null) gestureImage.texture = receivedTexture;
        
        UpdateStatus("Connecting to Photon...");
        
        PhotonManager.Instance.OnJoinedRoomEvent += () => UpdateStatus("Connected to Photon Room. Ready.");

        await ConnectToGestureServer();
        UpdateUI();
    }

    private async Task ConnectToGestureServer()
    {
        websocket = new WebSocket(gestureServerUrl);
        websocket.OnOpen += () => UpdateStatus("Gesture Server connected. Waiting for Photon...");
        websocket.OnError += (e) => Debug.LogError("WebSocket Error: " + e);
        websocket.OnClose += (e) => UpdateStatus("Gesture Server Disconnected.");
        
        websocket.OnMessage += (bytes) => {
            if (bytes.Length > 0)
            {
                byte messageType = bytes[0];
                if (messageType == MESSAGE_TYPE_CAMERA_FRAME)
                {
                    UnityMainThreadDispatcher.Instance().Enqueue(() => ProcessCameraFrame(bytes));
                }
                else if (messageType == MESSAGE_TYPE_FINGER_COUNT)
                {
                    ProcessFingerCount(bytes);
                }
            }
        };

        await websocket.Connect();
    }

    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
        #endif

        if (cooldownTimer > 0)
        {
            cooldownTimer -= Time.deltaTime;
            UpdateStatus($"Waiting... ({cooldownTimer:F1}s)");
            return;
        }
        
        HandleGestureLogic();
        UpdateUI();
    }

    private void HandleGestureLogic()
    {
        bool isValidGesture = false;
        if (currentPhase == GamePhase.Planning)
        {
            isValidGesture = currentFingerCount == 3 || currentFingerCount == 5;
        }
        else // Combat Phase
        {
            // Turret selection does not require holding
            if (currentFingerCount >= 1 && currentFingerCount <= 3)
            {
                PhotonManager.Instance.SendTurretSelection(currentFingerCount - 1);
                UpdateStatus($"Sent: Select Turret {currentFingerCount}");
                cooldownTimer = 0.5f; // Short cooldown for turret selection
                return;
            }
            // Phase change gesture requires holding
            isValidGesture = currentFingerCount == 4;
        }

        if (isValidGesture)
        {
            holdTimer += Time.deltaTime;
            if (holdTimer >= requiredHoldTime && !actionTriggered)
            {
                actionTriggered = true;
                cooldownTimer = commandCooldown; // Start cooldown

                if (currentPhase == GamePhase.Planning)
                {
                    if (currentFingerCount == 3) SendCommand("PROCESS_SAM");
                    else if (currentFingerCount == 5) SendCommand("START_COMBAT");
                }
                else if (currentPhase == GamePhase.Combat)
                {
                    if (currentFingerCount == 4) SendCommand("STOP_COMBAT");
                }
            }
        }
        else
        {
            holdTimer = 0f;
            actionTriggered = false;
        }
    }

    private void SendCommand(string command)
    {
        PhotonManager.Instance.SendPhaseChange(command);
        UpdateStatus($"Sent Command: {command}");

        // Change local state after sending command
        if (command == "START_COMBAT") currentPhase = GamePhase.Combat;
        else if (command == "STOP_COMBAT") currentPhase = GamePhase.Planning;
    }

    private void ProcessCameraFrame(byte[] messageData)
    {
        // The first byte is the message type. We need a new array with only the image data.
        if (messageData.Length > 1)
        {
            byte[] imageData = new byte[messageData.Length - 1];
            Array.Copy(messageData, 1, imageData, 0, messageData.Length - 1);
            
            // Now load the new array containing only the image bytes.
            receivedTexture.LoadImage(imageData);
            if (gestureImage != null)
            {
                gestureImage.texture = receivedTexture;
            }
        }
    }

    private void ProcessFingerCount(byte[] messageData)
    {
        string jsonStr = Encoding.UTF8.GetString(messageData, 1, messageData.Length - 1);
        var data = JsonUtility.FromJson<FingerCountData>(jsonStr);
        currentFingerCount = data.count;
    }

    private void UpdateUI()
    {
        if (fingerCountText != null) fingerCountText.text = $"Fingers: {currentFingerCount}";
        if (phaseText != null) phaseText.text = $"Phase: {currentPhase}";
        
        bool isHolding = (currentPhase == GamePhase.Planning && (currentFingerCount == 3 || currentFingerCount == 5)) ||
                         (currentPhase == GamePhase.Combat && currentFingerCount == 4);

        if (holdProgressSlider != null)
        {
            holdProgressSlider.gameObject.SetActive(isHolding && cooldownTimer <= 0);
            holdProgressSlider.value = isHolding ? Mathf.Clamp01(holdTimer / requiredHoldTime) : 0;
        }
    }
    
    private void UpdateStatus(string message)
    {
        if (statusText != null && (statusText.text != message || cooldownTimer > 0))
        {
            statusText.text = $"Status: {message}";
        }
    }

    private async void OnApplicationQuit()
    {
        if (websocket != null && websocket.State == WebSocketState.Open) await websocket.Close();
    }

    [Serializable]
    private class FingerCountData { public int count; }
} 