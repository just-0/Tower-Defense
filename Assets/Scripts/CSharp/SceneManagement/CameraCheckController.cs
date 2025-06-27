using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using NativeWebSocket;
using System.Text;
using System;

public class CameraCheckController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Text statusText;

    private WebSocket websocket;
    private const string ServerUrl = "ws://localhost:8766"; // Puerto del menú

    private const byte MESSAGE_TYPE_SERVER_STATUS = 8;

    private float retryTimer = 0f;
    private const float RETRY_INTERVAL = 5f; // Reintentar cada 5 segundos

    void Start()
    {
        if (statusText == null)
        {
            Debug.LogError("Status Text no está asignado en el Inspector.");
            return;
        }
        ConnectToServer();
    }

    private async void ConnectToServer()
    {
        statusText.text = "Conectando al backend...";
        
        websocket = new WebSocket(ServerUrl);

        websocket.OnOpen += () => {
            // Debug.Log("Conexión abierta con el backend. Esperando estado de la cámara...");
            statusText.text = "Conexión exitosa. Verificando cámara...";
        };
        
        websocket.OnError += (e) => {
            Debug.LogError($"No se pudo conectar al backend: {e}. Reintentando en {RETRY_INTERVAL} segundos...");
            statusText.text = "Error: No se puede conectar al backend de Python.\nAsegúrate de que el script 'run_backend.py' se está ejecutando.\nReintentando...";
            retryTimer = RETRY_INTERVAL; // Activa el contador para reintentar
        };

        websocket.OnMessage += OnMessageReceived;
        
        await websocket.Connect();
    }

    private void OnMessageReceived(byte[] bytes)
    {
        if (bytes.Length == 0) return;
        
        byte messageType = bytes[0];
        if (messageType != MESSAGE_TYPE_SERVER_STATUS) return;

        byte[] messageData = new byte[bytes.Length - 1];
        Buffer.BlockCopy(bytes, 1, messageData, 0, bytes.Length - 1);
        string jsonStr = Encoding.UTF8.GetString(messageData);
        ServerStatusData data = JsonUtility.FromJson<ServerStatusData>(jsonStr);

        UnityMainThreadDispatcher.Instance().Enqueue(() => {
            if (data.status == "camera_ok")
            {
                statusText.text = "¡Cámara detectada! Iniciando...";
                SceneManager.LoadScene("1_MainMenu");
            }
            else
            {
                statusText.text = "Cámara no detectada.\nPor favor, conecta una cámara y reinicia la aplicación y el script de Python.";
                // Aquí, el websocket se cierra, por lo que no se reintentará la conexión.
            }
        });
    }

    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
            websocket?.DispatchMessageQueue();
        #endif

        // Lógica de reintento si la conexión inicial falló
        if (retryTimer > 0)
        {
            retryTimer -= Time.deltaTime;
            if (retryTimer <= 0)
            {
                ConnectToServer();
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
    private class ServerStatusData { public string status; }
} 