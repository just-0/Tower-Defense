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
    [SerializeField] private GestureReceiver gestureReceiver; // Referencia a GestureReceiver para notificar respuestas del servidor
    
    private Texture2D cameraTexture;
    private Texture2D maskTexture;
    private WebSocket websocket;
    private bool isConnected = false;
    private bool processingFrame = false;
    private bool inCombatMode = false;
    private bool combatModeJustStarted = false; // Para detectar el primer frame de combate
    private string serverUrl = "ws://localhost:8767";
    private List<GameObject> pathSpheres = new List<GameObject>();

    [SerializeField] private float connectionTimeout = 5f;
    private float connectionTimer = 0f;
    private bool connectionChecked = false;
    private bool mainCameraConnected = false;

    // Evento para notificar cuando se selecciona una posición válida
    public delegate void GridPositionSelected(Vector3 worldPosition);
    public event GridPositionSelected OnGridPositionSelected;

    private Queue<byte[]> messageQueue = new Queue<byte[]>();
    private object queueLock = new object();
    private bool isProcessingQueue = false;
    private Vector3 targetCursorPosition;
    private float cursorSmoothSpeed = 20f;
    private bool debugSpheresCreated = false; // Para crear las esferas de debug solo una vez

    async void Start()
    {
        Debug.Log("Iniciando SAMController");
        
        // Verificar componentes críticos
        if (gridCursor == null)
        {
            Debug.LogError("¡gridCursor no está asignado en el Inspector!");
        }
        
        if (validPositionMaterial == null)
        {
            Debug.LogError("¡validPositionMaterial no está asignado en el Inspector!");
        }
        
        if (invalidPositionMaterial == null)
        {
            Debug.LogError("¡invalidPositionMaterial no está asignado en el Inspector!");
        }
        
        cameraTexture = new Texture2D(1, 1);
        maskTexture = new Texture2D(1, 1);

        cameraDisplay.texture = cameraTexture;
        maskDisplay.texture = maskTexture;

        cameraDisplay.enabled = true;
        cameraDisplay.color = Color.white;

        maskDisplay.enabled = false;
        maskDisplay.color = Color.white;
        
        // Inicializar posición del cursor
        targetCursorPosition = gridCursor.transform.position;

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
            
            // Marcar las cámaras como desconectadas
            InitialSceneLoader.SetCamerasConnected(false);
        };

        websocket.OnMessage += (bytes) => ProcessIncomingMessage(bytes);

        await websocket.Connect();
    }

    private void ProcessIncomingMessage(byte[] bytes)
    {
        // Añadir el mensaje a la cola para procesarlo en el hilo principal
        lock (queueLock)
        {
            messageQueue.Enqueue(bytes);
        }
    }
    
    private void ProcessMessageFromQueue(byte[] bytes)
    {
        try
        {
            if (bytes == null || bytes.Length <= 1)
                return;
            
            byte messageType = bytes[0];
            byte[] messageData = new byte[bytes.Length - 1];
            Buffer.BlockCopy(bytes, 1, messageData, 0, bytes.Length - 1);

            switch (messageType)
            {
                case 1: // Camera
                    // Usar try para proteger contra errores de carga de texturas
                    try {
                        if (cameraTexture == null)
                            cameraTexture = new Texture2D(2, 2);
                        
                        cameraTexture.LoadImage(messageData);
                        cameraTexture.Apply();
                         
                        cameraDisplay.enabled = true;
                        maskDisplay.enabled = false;
                        
                        // Marcar que la cámara principal está conectada
                        mainCameraConnected = true;
                        
                        // Si acabamos de entrar en modo combate, notificar a GestureReceiver
                        if (combatModeJustStarted && gestureReceiver != null)
                        {
                            gestureReceiver.OnCombatModeStarted();
                            combatModeJustStarted = false; // Solo notificar una vez
                        }
                    } catch (Exception e) {
                        Debug.LogError($"Error al cargar imagen de cámara: {e.Message}");
                    }
                    break;

                case 2: // Processing complete
                    processingFrame = false;
                    cameraDisplay.enabled = true;
                    maskDisplay.enabled = false;
                    break;

                case 3: // SAM mask
                    try {
                        if (maskTexture == null)
                            maskTexture = new Texture2D(2, 2);
                        
                        maskTexture.LoadImage(messageData);
                        maskDisplay.texture = maskTexture;
                        
                        // Notificar a GestureReceiver que se recibió respuesta del servidor
                        if (gestureReceiver != null)
                        {
                            gestureReceiver.OnServerResponseReceived();
                        }
                    } catch (Exception e) {
                        Debug.LogError($"Error al cargar máscara: {e.Message}");
                    }
                    break;
                    
                case 4: // A* path points
                    try 
                    {
                        string jsonPath = Encoding.UTF8.GetString(messageData);
                        Vector2Serializable[] pathPoints = JsonHelper.FromJson<Vector2Serializable>(jsonPath);
                        
                        if (pathPoints != null && pathPoints.Length > 0) 
                        {
                            DrawPath(pathPoints);
                        }
                        
                        // Notificar a GestureReceiver que se recibió respuesta del servidor (path)
                        if (gestureReceiver != null)
                        {
                            gestureReceiver.OnServerResponseReceived();
                        }
                    }
                    catch (Exception e) 
                    {
                        Debug.LogError($"JSON parsing error: {e.Message}");
                    }
                    break;

                case 6: // Grid Position (MESSAGE_TYPE_GRID_POSITION)
                    try
                    {
                        string jsonGrid = Encoding.UTF8.GetString(messageData);
                        GridPosition gridPos = JsonUtility.FromJson<GridPosition>(jsonGrid);
                        
                        
                        if (gridCursor != null && validPositionMaterial != null && invalidPositionMaterial != null)
                        {
                            UpdateGridCursor(gridPos);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Error al procesar posición de cuadrícula: {e.Message}");
                    }
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error procesando mensaje: {e.Message}");
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
        if (renderer != null) 
        {
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = color;
        }

        sphere.AddComponent<PulsingSphere>(); // <- animación

        pathSpheres.Add(sphere);
    }

    private GameObject CreateDebugSphere(Vector3 position, Color color)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position = position;
        // Usar una escala ligeramente diferente o igual, según preferencia para debug
        sphere.transform.localScale = Vector3.one * 0.35f; 
        
        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = color;
        }
        // No añadir PulsingSphere para que sean estáticas
        // No añadir a pathSpheres para que no se borren con el camino
        return sphere;
    }

    void DrawDebugEdgeSpheres(){
        if (Camera.main == null)
        {
            Debug.LogError("Camera.main no está disponible para dibujar esferas de depuración.");
            return;
        }

        float orthoHeight = Camera.main.orthographicSize;
        float orthoWidth = orthoHeight * Camera.main.aspect;
        
        float originalWidth = 640f; 
        float originalHeight = 480f;
        
        float scaleX = (orthoWidth * 2f) / originalWidth;
        float scaleY = (orthoHeight * 2f) / originalHeight;
        
        float zPos = -5f;   
    }



    private void UpdateGridCursor(GridPosition gridPos)
    {
        if (gridCursor == null) 
            return;

        // Convertir coordenadas de la cámara a coordenadas del mundo
        float orthoHeight = Camera.main.orthographicSize;
        float orthoWidth = orthoHeight * Camera.main.aspect;
        float originalWidth = 640f;
        float originalHeight = 480f;
        float scaleX = (orthoWidth * 2f) / originalWidth;
        float scaleY = (orthoHeight * 2f) / originalHeight;

        float worldX = (gridPos.x - originalWidth / 2f) * scaleX;
        float worldY = -(gridPos.y - originalHeight / 2f) * scaleY;
        
        // Actualizar la posición objetivo, no la posición directa
        targetCursorPosition = new Vector3(worldX, worldY, -5f);

        // Actualizar material según validez
        Renderer renderer = gridCursor.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = gridPos.valid ? validPositionMaterial : invalidPositionMaterial;
        }

        // Si la posición es válida, notificar a los listeners
        if (gridPos.valid)
        {
            OnGridPositionSelected?.Invoke(targetCursorPosition);
        }
    }

    void Update()
    {
        // Suavizar movimiento del cursor
        if (gridCursor != null)
        {
            gridCursor.transform.position = Vector3.Lerp(
                gridCursor.transform.position,
                targetCursorPosition,
                cursorSmoothSpeed * Time.deltaTime
            );
        }
        
        // Procesar la cola de mensajes en el update para evitar problemas de thread
        ProcessQueuedMessages();
        
        #if !UNITY_WEBGL || UNITY_EDITOR
        if (websocket != null)
        {
            websocket.DispatchMessageQueue();
        }
        #endif

        // Dibujar esferas de depuración una vez que la cámara principal esté conectada
        if (mainCameraConnected && !debugSpheresCreated)
        {
            DrawDebugEdgeSpheres();
            debugSpheresCreated = true;
        }

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
        }

        // Tecla C para alternar modo combate
        if (Input.GetKeyDown(KeyCode.C) && isConnected)
        {
            inCombatMode = !inCombatMode;
            SendMessage(inCombatMode ? "START_COMBAT" : "STOP_COMBAT");
        }
    }
    
    // Procesar mensajes en cola para evitar bloqueos
    private void ProcessQueuedMessages()
    {
        if (isProcessingQueue)
            return;
            
        isProcessingQueue = true;
        
        try
        {
            // Procesar un número limitado de mensajes por frame para mantener el rendimiento
            int messagesToProcess = 5;
            int processedCount = 0;
            
            lock (queueLock)
            {
                while (messageQueue.Count > 0 && processedCount < messagesToProcess)
                {
                    byte[] message = messageQueue.Dequeue();
                    ProcessMessageFromQueue(message);
                    processedCount++;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error al procesar cola de mensajes: {e.Message}");
        }
        finally
        {
            isProcessingQueue = false;
        }
    }

    public async void SendMessage(string message)
    {
        if (websocket != null && websocket.State == WebSocketState.Open)
        {
            // Si es START_COMBAT, marcar que acabamos de iniciar modo combate
            if (message == "START_COMBAT")
            {
                combatModeJustStarted = true;
                inCombatMode = true;
            }
            else if (message == "STOP_COMBAT")
            {
                inCombatMode = false;
                combatModeJustStarted = false;
            }
            
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