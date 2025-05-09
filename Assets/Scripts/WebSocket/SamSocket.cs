using UnityEngine;
using UnityEngine.UI;
using System.Text;
using NativeWebSocket;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using UnityEngine.SceneManagement;

public class SAMController : MonoBehaviour
{
    [Serializable]
    public struct Vector2Serializable
    {
        public float x;
        public float y;
        
        public override string ToString() => $"({x}, {y})";
    }

    [Serializable]
    public struct GridPosition
    {
        public float x;
        public float y;
        public bool valid;
    }

    [SerializeField] private RawImage cameraDisplay;
    [SerializeField] private RawImage maskDisplay;
    [SerializeField] private GameObject gridCursor; // Cursor visual para la posición del dedo
    [SerializeField] private Material validPositionMaterial; // Material verde para posición válida
    [SerializeField] private Material invalidPositionMaterial; // Material rojo para posición inválida
    
    private Texture2D cameraTexture;
    private Texture2D maskTexture;
    private WebSocket websocket;
    private bool isConnected = false;
    private bool processingFrame = false;
    private bool inCombatMode = false;
    private string serverUrl = "ws://localhost:8767";
    private List<GameObject> pathSpheres = new List<GameObject>();

    [SerializeField] private float connectionTimeout = 5f;
    private float connectionTimer = 0f;
    private bool connectionChecked = false;
    private bool mainCameraConnected = false;

    // Evento para notificar cuando se selecciona una posición válida
    public delegate void GridPositionSelected(Vector3 worldPosition);
    public event GridPositionSelected OnGridPositionSelected;

    async void Start()
    {
        Debug.Log("DEBUG-UNITY-START: Iniciando SAMController");
        
        // Verificar componentes críticos
        if (gridCursor == null)
        {
            Debug.LogError("ERROR-UNITY-START: ¡gridCursor no está asignado en el Inspector!");
        }
        else
        {
            Debug.Log($"DEBUG-UNITY-START: gridCursor encontrado en posición: {gridCursor.transform.position}");
            
            Renderer renderer = gridCursor.GetComponent<Renderer>();
            if (renderer == null)
            {
                Debug.LogError("ERROR-UNITY-START: ¡gridCursor no tiene componente Renderer!");
            }
        }
        
        if (validPositionMaterial == null)
        {
            Debug.LogError("ERROR-UNITY-START: ¡validPositionMaterial no está asignado en el Inspector!");
        }
        
        if (invalidPositionMaterial == null)
        {
            Debug.LogError("ERROR-UNITY-START: ¡invalidPositionMaterial no está asignado en el Inspector!");
        }
        
        cameraTexture = new Texture2D(1, 1);
        maskTexture = new Texture2D(1, 1);

        cameraDisplay.texture = cameraTexture;
        maskDisplay.texture = maskTexture;

        cameraDisplay.enabled = true;
        cameraDisplay.color = Color.white;

        maskDisplay.enabled = false;
        maskDisplay.color = Color.white;

        Debug.Log("DEBUG-UNITY-START: Conectando al servidor WebSocket");
        await ConnectToServer();
        Debug.Log("DEBUG-UNITY-START: Conexión al servidor WebSocket completada");
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
            
            // Marcar las cámaras como desconectadas
            InitialSceneLoader.SetCamerasConnected(false);
        };

        websocket.OnMessage += (bytes) => ProcessIncomingMessage(bytes);

        await websocket.Connect();
    }

    private void ProcessIncomingMessage(byte[] bytes)
    {
        Debug.Log($"DEBUG-UNITY: Recibido mensaje de {bytes.Length} bytes");
        
        if (bytes.Length > 0 && (bytes[0] == '{' || bytes[0] == '[' || (bytes[0] >= 'A' && bytes[0] <= 'Z') || (bytes[0] >= 'a' && bytes[0] <= 'z')))
        {
            string textMessage = Encoding.UTF8.GetString(bytes);
            Debug.Log($"DEBUG-UNITY: Mensaje de texto recibido: {textMessage}");
            return;
        }

        if (bytes.Length > 1)
        {
            byte messageType = bytes[0];
            Debug.Log($"DEBUG-UNITY: Tipo de mensaje recibido: {messageType}");
            
            byte[] messageData = new byte[bytes.Length - 1];
            Buffer.BlockCopy(bytes, 1, messageData, 0, bytes.Length - 1);

            switch (messageType)
            {
                case 1: // Camera
                    cameraTexture.LoadImage(messageData);
                    cameraTexture.Apply();
                     
                    cameraDisplay.enabled = true;
                    maskDisplay.enabled = false;
                    
                    // Marcar que la cámara principal está conectada
                    mainCameraConnected = true;
                    break;

                case 2: // Processing complete
                    processingFrame = false;
                    cameraDisplay.enabled = true;
                    maskDisplay.enabled = false;
                    break;

                case 3: // SAM mask
                    maskTexture = new Texture2D(2, 2);
                    maskTexture.LoadImage(messageData);
                    maskDisplay.texture = maskTexture;
                    break;
                    
                case 4: // A* path points
                    try 
                    {
                        string jsonPath = Encoding.UTF8.GetString(messageData);
                        Vector2Serializable[] pathPoints = JsonHelper.FromJson<Vector2Serializable>(jsonPath);
                        
                        if (pathPoints == null || pathPoints.Length == 0) 
                        {
                            Debug.LogError("Could not parse path points");
                            return;
                        }
                        DrawPath(pathPoints);
                    }
                    catch (Exception e) 
                    {
                        Debug.LogError($"JSON parsing error: {e.Message}\n{e.StackTrace}");
                    }
                    break;

                case 6: // Grid Position (MESSAGE_TYPE_GRID_POSITION)
                    Debug.Log($"DEBUG-UNITY-CASE6: Procesando mensaje tipo 6 (MESSAGE_TYPE_GRID_POSITION)");
                    try
                    {
                        string jsonGrid = Encoding.UTF8.GetString(messageData);
                        Debug.Log($"DEBUG-UNITY-CASE6: Recibido mensaje de posición de cuadrícula: {jsonGrid}");
                        GridPosition gridPos = JsonUtility.FromJson<GridPosition>(jsonGrid);
                        Debug.Log($"DEBUG-UNITY-CASE6: Posición de cuadrícula parseada: x={gridPos.x}, y={gridPos.y}, válida={gridPos.valid}");
                        
                        // Verificar que los materiales están asignados
                        if (validPositionMaterial == null || invalidPositionMaterial == null)
                        {
                            Debug.LogError($"ERROR-UNITY-CASE6: Materiales no asignados. validPositionMaterial={validPositionMaterial}, invalidPositionMaterial={invalidPositionMaterial}");
                        }
                        
                        // Verificar que el cursor existe
                        if (gridCursor == null)
                        {
                            Debug.LogError("ERROR-UNITY-CASE6: gridCursor es null");
                        }
                        else
                        {
                            Debug.Log($"DEBUG-UNITY-CASE6: gridCursor encontrado en posición: {gridCursor.transform.position}");
                        }
                        
                        UpdateGridCursor(gridPos);
                        Debug.Log($"DEBUG-UNITY-CASE6: UpdateGridCursor completado");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"ERROR-UNITY-CASE6: Error al procesar posición de cuadrícula: {e.Message}\n{e.StackTrace}");
                    }
                    break;
            }
        }
    }

    private void DrawPath(Vector2Serializable[] pathPoints) 
    {
        // Clear previous path
        foreach (var sphere in pathSpheres)
        {
            if (sphere != null)
                Destroy(sphere);
        }
        pathSpheres.Clear();
        
        // Camera dimensions
        float orthoHeight = Camera.main.orthographicSize;
        float orthoWidth = orthoHeight * Camera.main.aspect;
        
        // Original size (640x480)
        float originalWidth = 640f;
        float originalHeight = 480f;
        
        // Scale factors
        float scaleX = (orthoWidth * 2f) / originalWidth;
        float scaleY = (orthoHeight * 2f) / originalHeight;
        
        // Z position
        float zPos = -5f;
        
        
        int step = 15; 

        for (int i = 0; i < pathPoints.Length; i += step)
        {
            var point = pathPoints[i];
            float worldX = (point.x - originalWidth / 2f) * scaleX;
            float worldY = -(point.y - originalHeight / 2f) * scaleY;

            CreateSphere(new Vector3(worldX, worldY, zPos), Color.cyan);
        }

        
        // Debug first point
        if (pathPoints.Length > 0)
        {
            float firstX = (pathPoints[0].x - originalWidth / 2f) * scaleX;
            float firstY = -(pathPoints[0].y - originalHeight / 2f) * scaleY;
            Debug.Log($"First point: 640x480=({pathPoints[0].x}, {pathPoints[0].y}) → World=({firstX}, {firstY})");
        }
    }

    private void CreateSphere(Vector3 position, Color color)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * 0.3f;
        
        Renderer renderer = sphere.GetComponent<Renderer>();
        renderer.material = new Material(Shader.Find("Standard"));
        renderer.material.color = color;

        sphere.AddComponent<PulsingSphere>(); // <- animación

        pathSpheres.Add(sphere);
    }

    private void UpdateGridCursor(GridPosition gridPos)
    {
        Debug.Log($"DEBUG-UNITY-CURSOR: Actualizando cursor con posición: x={gridPos.x}, y={gridPos.y}, válida={gridPos.valid}");
        
        if (gridCursor == null) 
        {
            Debug.LogError("ERROR-UNITY-CURSOR: ¡El gridCursor es null! No se puede actualizar la posición.");
            return;
        }

        // Convertir coordenadas de la cámara a coordenadas del mundo
        float orthoHeight = Camera.main.orthographicSize;
        float orthoWidth = orthoHeight * Camera.main.aspect;
        float originalWidth = 640f;
        float originalHeight = 480f;
        float scaleX = (orthoWidth * 2f) / originalWidth;
        float scaleY = (orthoHeight * 2f) / originalHeight;

        float worldX = (gridPos.x - originalWidth / 2f) * scaleX;
        float worldY = -(gridPos.y - originalHeight / 2f) * scaleY;
        Vector3 worldPosition = new Vector3(worldX, worldY, -5f);
        
        Debug.Log($"DEBUG-UNITY-CURSOR: Coordenadas convertidas a mundo: {worldPosition}");

        // Actualizar posición del cursor
        gridCursor.transform.position = worldPosition;
        Debug.Log($"DEBUG-UNITY-CURSOR: Posición del cursor actualizada a: {gridCursor.transform.position}");

        // Actualizar material según validez
        Renderer renderer = gridCursor.GetComponent<Renderer>();
        if (renderer != null)
        {
            string materialAnterior = renderer.material.name;
            renderer.material = gridPos.valid ? validPositionMaterial : invalidPositionMaterial;
            Debug.Log($"DEBUG-UNITY-CURSOR: Material actualizado de '{materialAnterior}' a '{renderer.material.name}', válido={gridPos.valid}");
            
            if (validPositionMaterial == null || invalidPositionMaterial == null)
            {
                Debug.LogError($"ERROR-UNITY-CURSOR: Materiales no asignados. validPositionMaterial={validPositionMaterial}, invalidPositionMaterial={invalidPositionMaterial}");
            }
        }
        else
        {
            Debug.LogError("ERROR-UNITY-CURSOR: ¡El cursor no tiene componente Renderer!");
        }

        // Si la posición es válida y el usuario está apuntando, notificar a los listeners
        if (gridPos.valid)
        {
            Debug.Log($"DEBUG-UNITY-CURSOR: Posición válida, notificando a los listeners");
            OnGridPositionSelected?.Invoke(worldPosition);
        }
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket?.DispatchMessageQueue();
#endif

        // Verificar si las cámaras están conectadas
        if (!connectionChecked)
        {
            connectionTimer += Time.deltaTime;
            
            if (connectionTimer >= connectionTimeout)
            {
                connectionChecked = true;
                
                // Si no se ha recibido ningún frame de la cámara principal después del timeout,
                // redirigir a la escena de verificación de cámaras
                if (!mainCameraConnected)
                {
                    Debug.LogWarning("No se detectó conexión con la cámara principal. Redirigiendo a la escena de verificación.");
                    SceneManager.LoadScene("CameraVerification");
                }
            }
        }

        if (Input.GetKeyDown(KeyCode.Space) && isConnected && !processingFrame)
        {
            processingFrame = true;
            SendMessage("PROCESS_SAM");
            Debug.Log("Sent request to process current frame with SAM");
        }

        // Tecla C para alternar modo combate
        if (Input.GetKeyDown(KeyCode.C) && isConnected)
        {
            inCombatMode = !inCombatMode;
            SendMessage(inCombatMode ? "START_COMBAT" : "STOP_COMBAT");
            Debug.Log($"Combat mode {(inCombatMode ? "started" : "stopped")}");
        }
    }

    public async void SendMessage(string message)
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

public static class JsonHelper
{
    public static T[] FromJson<T>(string json)
    {
        string newJson = "{ \"array\": " + json + "}";
        Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(newJson);
        return wrapper.array;
    }

    [Serializable]
    private class Wrapper<T>
    {
        public T[] array;
    }
}

public class PulsingSphere : MonoBehaviour
{
    private float pulseSpeed = 2f;
    private float minScale = 0.1f;
    private float maxScale = 0.3f;
    private float startTime;
    private Renderer rend;

    void Start()
    {
        startTime = Time.time;
        rend = GetComponent<Renderer>();
    }

    void Update()
    {
        float t = Mathf.PingPong((Time.time - startTime) * pulseSpeed, 1f);
        float scale = Mathf.Lerp(minScale, maxScale, t);
        transform.localScale = new Vector3(scale, scale, scale);

        Color c = rend.material.color;
        c.a = Mathf.Lerp(0.2f, 1f, t); // transparencia
        rend.material.color = c;
    }
} 