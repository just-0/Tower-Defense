using UnityEngine;
using NativeWebSocket;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;

public class MainWebSocketClient : MonoBehaviour
{
    [Tooltip("URL del servidor WebSocket principal (ej: ws://localhost:8767)")]
    public string serverUrl = "ws://localhost:8767";
    private WebSocket websocket;
    public bool IsConnected { get; private set; } = false;

    // Eventos para notificar la recepción de diferentes tipos de mensajes
    public event Action OnConnectionOpened;
    public event Action<byte[]> OnCameraMessageReceived;      // Tipo 1 (datos de imagen)
    public event Action OnProcessingCompleteReceived; // Tipo 2 (señal de procesamiento completado)
    public event Action<byte[]> OnSamMaskReceived;          // Tipo 3 (datos de máscara SAM)
    public event Action<byte[]> OnPathPointsReceived;       // Tipo 4 (puntos del path A*)
    public event Action<byte[]> OnGridPositionReceived;     // Tipo 6 (posición en la cuadrícula)
    public event Action<byte, byte[]> OnUnknownMessageReceived; // Para tipos de mensaje no manejados explícitamente

    private readonly Queue<byte[]> messageQueue = new Queue<byte[]>();
    private readonly object queueLock = new object();
    private bool isProcessingQueue = false;

    async void Start()
    {
        Debug.Log($"MainWebSocketClient: Intentando conectar a {serverUrl}");
        await ConnectToServer();
    }

    async Task ConnectToServer()
    {
        websocket = new WebSocket(serverUrl);

        websocket.OnOpen += () =>
        {
            Debug.Log("MainWebSocketClient: Conexión abierta.");
            IsConnected = true;
            OnConnectionOpened?.Invoke();
        };

        websocket.OnError += (e) =>
        {
            Debug.LogError($"MainWebSocketClient Error: {e}");
        };

        websocket.OnClose += (e) =>
        {
            Debug.Log($"MainWebSocketClient: Conexión cerrada (código: {e}).");
            IsConnected = false;
            // Considera si InitialSceneLoader.SetCamerasConnected(false) debe ser invocado aquí
            // o si el SAMSystemController debe manejarlo basado en este evento o su propio estado.
            // Por ahora, lo mantenemos si era una lógica global crítica.
            if (FindObjectOfType<InitialSceneLoader>() != null) // Evita error si no existe
            {
                InitialSceneLoader.SetCamerasConnected(false);
            }
        };

        websocket.OnMessage += (bytes) =>
        {
            // Encolar el mensaje para ser procesado en el hilo principal (Update)
            lock (queueLock)
            {
                messageQueue.Enqueue(bytes);
            }
        };

        try
        {
            await websocket.Connect();
        }
        catch (Exception ex)
        {
            Debug.LogError($"MainWebSocketClient: Falló la conexión inicial: {ex.Message}");
        }
    }

    void Update()
    {
        ProcessQueuedMessages();

        #if !UNITY_WEBGL || UNITY_EDITOR
        if (websocket != null && IsConnected) // O usar websocket.State == WebSocketState.Open
        {
            websocket.DispatchMessageQueue(); // Despachar mensajes en el hilo principal
        }
        #endif
    }

    private void ProcessQueuedMessages()
    {
        if (isProcessingQueue) return;

        isProcessingQueue = true;
        try
        {
            int messagesToProcessThisFrame = 5; // Limitar para no bloquear el hilo principal
            int processedCount = 0;

            lock (queueLock)
            {
                while (messageQueue.Count > 0 && processedCount < messagesToProcessThisFrame)
                {
                    byte[] message = messageQueue.Dequeue();
                    DistributeMessage(message);
                    processedCount++;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"MainWebSocketClient: Error procesando la cola de mensajes: {e.Message}");
        }
        finally
        {
            isProcessingQueue = false;
        }
    }

    private void DistributeMessage(byte[] bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            Debug.LogWarning("MainWebSocketClient: Mensaje recibido vacío o nulo.");
            return;
        }
            
        byte messageType = bytes[0];
        byte[] messageData = new byte[bytes.Length - 1];
        if (bytes.Length > 1)
        {
            Buffer.BlockCopy(bytes, 1, messageData, 0, bytes.Length - 1);
        }

        // Debug.Log($"MainWebSocketClient: Distribuyendo mensaje tipo {messageType}");
        switch (messageType)
        {
            case 1: // Camera Frame
                OnCameraMessageReceived?.Invoke(messageData);
                break;
            case 2: // Processing Complete
                OnProcessingCompleteReceived?.Invoke();
                break;
            case 3: // SAM Mask
                OnSamMaskReceived?.Invoke(messageData);
                break;
            case 4: // A* Path Points
                OnPathPointsReceived?.Invoke(messageData);
                break;
            case 6: // Grid Position
                OnGridPositionReceived?.Invoke(messageData);
                break;
            default:
                Debug.LogWarning($"MainWebSocketClient: Tipo de mensaje desconocido recibido: {messageType}");
                OnUnknownMessageReceived?.Invoke(messageType, messageData);
                break;
        }
    }

    public async Task SendMessageAsync(string message)
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            // Debug.Log($"MainWebSocketClient: Enviando mensaje: {message}");
            await websocket.SendText(message);
        }
        else
        {
            Debug.LogWarning($"MainWebSocketClient: WebSocket no está abierto. No se puede enviar mensaje: {message}");
        }
    }

    private async void OnApplicationQuit()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            Debug.Log("MainWebSocketClient: Cerrando conexión WebSocket al salir de la aplicación.");
            await websocket.Close();
        }
    }

    void OnDestroy()
    {
        // Asegurarse de cerrar el WebSocket si el objeto se destruye antes de OnApplicationQuit
        if (websocket != null && IsConnected) // O websocket.State == WebSocketState.Open
        {
            // No podemos usar async void aquí directamente de forma segura en OnDestroy
            // Considerar cerrar de forma síncrona si es crítico o manejarlo de otra forma.
            // Por ahora, confiamos en OnApplicationQuit o en que NativeWebSocket maneje bien el cierre abrupto.
            // websocket.Close(); // Esto podría necesitar ser llamado de forma diferente
        }
    }
} 