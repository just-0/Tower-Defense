using UnityEngine;
using UnityEngine.UI;
using System.Text;
using NativeWebSocket;
using System.Threading.Tasks;

public class SAMController : MonoBehaviour
{
    [SerializeField] private RawImage cameraDisplay;  // Para mostrar la cámara
    [SerializeField] private RawImage maskDisplay;  // Para mostrar la máscara de SAM
    
    public bool tutorial = false; //---------------------------------------------------------------

    private Texture2D cameraTexture;  // Textura para mostrar el feed de la cámara
    private Texture2D maskTexture;  // Textura para mostrar la máscara procesada por SAM

    private WebSocket websocket;  // Conexión WebSocket
    private bool isConnected = false;  // Estado de la conexión WebSocket
    private bool processingFrame = false;  // Para evitar enviar múltiples solicitudes

    private string serverUrl = "ws://localhost:8767";  // URL del servidor WebSocket
    async void Start()
    {
        // Inicializar las texturas
        cameraTexture = new Texture2D(1, 1);
        maskTexture = new Texture2D(1, 1);

        // Asignar las texturas a los RawImages
        cameraDisplay.texture = cameraTexture;
        maskDisplay.texture = maskTexture;

        // Asegurarse de que los RawImages son visibles
        cameraDisplay.enabled = true;
        cameraDisplay.color = new Color(1, 1, 1, 1);

        maskDisplay.enabled = false;  // Solo se activa cuando se procesa la máscara
        maskDisplay.color = new Color(1, 1, 1, 1);  // Mostrar la máscara cuando esté disponible

        // Intentar conectarse al servidor WebSocket
        await ConnectToServer();
    }

    public void tutonext()
    {
        tutorial = true;
    }

    // Conexión al servidor WebSocket
    async Task ConnectToServer()
    {
        websocket = new WebSocket(serverUrl);

        websocket.OnOpen += () =>
        {
            Debug.Log("Connection opened");
            isConnected = true;
            SendMessage("START_CAMERA");  // Solicitar que inicie la cámara en Python
        };

        websocket.OnError += (e) => Debug.LogError($"Error: {e}");

        websocket.OnClose += (e) =>
        {
            Debug.Log("Connection closed");
            isConnected = false;
        };

        websocket.OnMessage += (bytes) => ProcessIncomingMessage(bytes);  // Procesar los mensajes recibidos

        await websocket.Connect();
    }

    // Procesa los mensajes recibidos desde el servidor WebSocket
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
                case 1:  // Tipo 1: Cámara
                    cameraTexture.LoadImage(imageData);  // Cargar la imagen de la cámara
                    cameraTexture.Apply();  // Aplicar la textura a la RawImage
                    break;

                case 2:  // Tipo 2: Procesamiento de SAM
                    processingFrame = false;
                    break;

                case 3:  // Tipo 3: Máscara SAM
                    Debug.Log("Mask received");
                    maskTexture = new Texture2D(2, 2);  // Crear una textura temporal
                    maskTexture.LoadImage(imageData);  // Cargar la máscara procesada
                    maskDisplay.texture = maskTexture;  // Mostrar la máscara en la RawImage
                    maskDisplay.enabled = true;

                    // Aquí puedes usar la máscara para realizar cualquier otra acción
                    AStarPathfinder pathfinder = FindObjectOfType<AStarPathfinder>();
                    if (pathfinder != null)
                    {
                        pathfinder.InitializeGrid();
                        pathfinder.CalculatePath();  // Calcular el camino para la lógica del juego
                    }

                    processingFrame = false;
                    break;
                case 4:
                    string qrMessage = Encoding.UTF8.GetString(imageData);
                    Debug.Log("Mensaje recibido de Python: " + qrMessage);
                    break;

            }
        }
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();  // Procesar la cola de mensajes WebSocket
#endif

        // Enviar solicitud de procesamiento de SAM cuando se presiona la barra espaciadora
        if (Input.GetKeyDown(KeyCode.Space) && isConnected && !processingFrame)
        {
            processingFrame = true;
            SendMessage("PROCESS_SAM");
            Debug.Log("Sent request to process current frame with SAM");
        }

        // Enviar solicitud para obtener la zona segura cuando se presiona "Z"
        if (Input.GetKeyDown(KeyCode.Z) && isConnected && !processingFrame)
        {
            processingFrame = true;
            SendMessage("GET_SAFE_ZONE");  // Solicitar la zona segura (ArUco) al servidor Python
            Debug.Log("Sent request to detect ArUco marker for safe zone");
        }       
    }

    // Enviar mensaje al servidor WebSocket
    private async void SendMessage(string message)
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await websocket.SendText(message);
        }
    }

    // Cerrar la conexión WebSocket cuando la aplicación termine
    private async void OnApplicationQuit()
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            await websocket.Close();
        }
    }
}
