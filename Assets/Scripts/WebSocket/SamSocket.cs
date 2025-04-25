using UnityEngine;
using UnityEngine.UI;
using System.Text;
using NativeWebSocket;
using System.Threading.Tasks;

public class SAMController : MonoBehaviour
{
    [SerializeField] private RawImage cameraDisplay;
    [SerializeField] private RawImage maskDisplay;

    private Texture2D cameraTexture;
    private Texture2D maskTexture;

    private WebSocket websocket;
    private bool isConnected = false;
    private bool processingFrame = false;

    private string serverUrl = "ws://localhost:8767";

    async void Start()
    {
        cameraTexture = new Texture2D(1, 1);
        maskTexture = new Texture2D(1, 1);

        cameraDisplay.texture = cameraTexture;
        maskDisplay.texture = maskTexture;

        // Asegurar visibilidad correcta
        cameraDisplay.enabled = true;
        cameraDisplay.color = new Color(1, 1, 1, 1);

        maskDisplay.enabled = false;  // solo se activa con la máscara
        maskDisplay.color = new Color(1, 1, 1, 1);  // full opaco para ver cambios

        await ConnectToServer();
    }

    async Task ConnectToServer()
    {
        websocket = new WebSocket(serverUrl);

        websocket.OnOpen += () =>
        {
            Debug.Log("Connection opened");
            isConnected = true;
            SendMessage("START_CAMERA");
        };

        websocket.OnError += (e) => Debug.LogError($"Error: {e}");

        websocket.OnClose += (e) =>
        {
            Debug.Log("Connection closed");
            isConnected = false;
        };

        websocket.OnMessage += (bytes) => ProcessIncomingMessage(bytes);

        await websocket.Connect();
    }

    private void ProcessIncomingMessage(byte[] bytes)
    {
        if (bytes.Length > 0 && (bytes[0] == '{' || bytes[0] == '[' || (bytes[0] >= 'A' && bytes[0] <= 'Z') || (bytes[0] >= 'a' && bytes[0] <= 'z')))
        {
            string textMessage = Encoding.UTF8.GetString(bytes);
            Debug.Log($"Received text message: {textMessage}");
            return;
        }

        if (bytes.Length > 1)
        {
            byte messageType = bytes[0];
            byte[] imageData = new byte[bytes.Length - 1];
            System.Buffer.BlockCopy(bytes, 1, imageData, 0, bytes.Length - 1);

            switch (messageType)
            {
                case 1: // Cámara
                    cameraTexture.LoadImage(imageData);
                    cameraTexture.Apply();
                    break;

                case 2: // Solo indicar que procesó, no se usa
                    processingFrame = false;
                    break;

                case 3: // Máscara SAM
                    Debug.Log("xdddd");
                    maskTexture = new Texture2D(2, 2); // Tamaño temporal
                    maskTexture.LoadImage(imageData);
                    maskDisplay.texture = maskTexture;
                    maskDisplay.enabled = true;

                    AStarPathfinder pathfinder = FindObjectOfType<AStarPathfinder>();
                    if (pathfinder != null) 
                    {
                        pathfinder.InitializeGrid();
                        pathfinder.CalculatePath();
                    }
                    processingFrame = false;
                    break;
            }
        }
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif

        if (Input.GetKeyDown(KeyCode.Space) && isConnected && !processingFrame)
        {
            processingFrame = true;
            SendMessage("PROCESS_SAM");
            Debug.Log("Sent request to process current frame with SAM");
        }
    }

    private async void SendMessage(string message)
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await websocket.SendText(message);
        }
    }

    private async void OnApplicationQuit()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await websocket.Close();
        }
    }
}
