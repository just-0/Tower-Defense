using UnityEngine;
using NativeWebSocket;
using System.Threading.Tasks;

public enum BackendMode
{
    SinglePlayer,
    MultiplayerSelector,
    MultiplayerPlacer,
    Stop
}

public class BackendManager : MonoBehaviour
{
    public static BackendManager Instance { get; private set; }

    [Header("Network Configuration")]
    [SerializeField] private string controlServerUrl = "ws://localhost:8765";

    private WebSocket websocket;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    async void Start()
    {
        await ConnectToControlServer();
    }

    private async Task ConnectToControlServer()
    {
        websocket = new WebSocket(controlServerUrl);

        websocket.OnOpen += () =>
        {
            Debug.Log("Conectado al servidor de control de Python.");
        };

        websocket.OnError += (e) =>
        {
            Debug.LogError($"Error en la conexión con el servidor de control: {e}");
        };

        websocket.OnClose += (e) =>
        {
            Debug.Log("Desconectado del servidor de control de Python.");
        };
        
        // No esperamos mensajes de vuelta, así que OnMessage no es necesario.

        await websocket.Connect();
    }
    
    // El despachador de mensajes es necesario en el Update para que los eventos se ejecuten en el hilo principal
    void Update()
    {
        #if !UNITY_WEBGL || UNITY_EDITOR
            if (websocket != null)
            {
                websocket.DispatchMessageQueue();
            }
        #endif
    }

    public async Task RequestBackendMode(BackendMode mode)
    {
        if (websocket == null || websocket.State != WebSocketState.Open)
        {
            Debug.LogError("No se puede enviar la solicitud: El WebSocket no está conectado al servidor de control.");
            return;
        }

        string command = "";
        switch (mode)
        {
            case BackendMode.SinglePlayer:
                command = "start_singleplayer";
                break;
            case BackendMode.MultiplayerSelector:
                command = "start_multiplayer_selector";
                break;
            case BackendMode.MultiplayerPlacer:
                command = "start_multiplayer_placer";
                break;
            case BackendMode.Stop:
                command = "stop";
                break;
        }

        Debug.Log($"Enviando comando al backend: '{command}'");
        await websocket.SendText(command);
    }

    private async void OnApplicationQuit()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await RequestBackendMode(BackendMode.Stop); // Solicita detener los servidores
            await websocket.Close();
        }
    }
} 