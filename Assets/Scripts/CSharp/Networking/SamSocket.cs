using UnityEngine;
using UnityEngine.UI;
using System.Text;
// using NativeWebSocket; // Eliminado, MainWebSocketClient lo maneja
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using UnityEngine.SceneManagement;
using Photon.Realtime;
using Photon.Chat;

// Cambiamos el nombre de la clase de SAMController a SAMSystemController
public class SAMSystemController : MonoBehaviour // Anteriormente SAMController
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

    [Serializable]
    public struct ProgressUpdate
    {
        public string step;
        public float progress;
    }

    [Header("Cursor y Rango")]
    [Tooltip("La escala del objeto visual que sigue al dedo del jugador.")]
    [SerializeField] private float cursorScale = 0.6f;
    [Tooltip("La cantidad de segmentos usados para dibujar el círculo de rango. Más segmentos significa un círculo más suave.")]
    [SerializeField] private int rangeIndicatorSegments = 50;
    private GameObject rangeIndicatorObject;
    private LineRenderer rangeIndicatorRenderer;

    [Header("Componentes UI y Visuales")]
    [SerializeField] private RawImage cameraDisplay;
    [SerializeField] private RawImage maskDisplay;
    [SerializeField] private GameObject gridCursor; // Cursor visual para la posición del dedo
    [SerializeField] private Material validPositionMaterial; // Material verde para posición válida
    [SerializeField] private Material invalidPositionMaterial; // Material rojo para posición inválida
    
    [Header("Referencias a Otros Sistemas")]
    [SerializeField] private GestureReceiver gestureReceiver; // Referencia a GestureReceiver para notificar respuestas del servidor
    [SerializeField] private MonsterManager monsterManager; // Referencia al MonsterManager para las oleadas de monstruos
    [SerializeField] private MainWebSocketClient mainWebSocketClient; // Referencia al nuevo cliente WebSocket
    [SerializeField] private TurretManager turretManager;
    private PhotonManager photonManager; // Referencia al gestor de Photon
    
    private Texture2D cameraTexture;
    private Texture2D maskTexture;
    // private WebSocket websocket; // Eliminado, MainWebSocketClient lo maneja
    // private bool isConnected = false; // Eliminado, se usa mainWebSocketClient.IsConnected
    private bool processingFrame = false;
    private bool inCombatMode = false;
    private bool combatModeJustStarted = false; // Para detectar el primer frame de combate
    // private string serverUrl = "ws://localhost:8767"; // Eliminado, MainWebSocketClient lo maneja
    private List<Vector3> storedWorldPath; // Para almacenar el path hasta que se inicie combate
    private List<GameObject> pathSpheres = new List<GameObject>();

    [Header("Configuración de Conexión")]
    [SerializeField] private float connectionTimeout = 5f;
    private float connectionTimer = 0f;
    private bool connectionChecked = false;
    private bool mainCameraConnected = false; // Relacionado con la recepción de frames de la cámara principal

    // Evento para notificar cuando se selecciona una posición válida
    public delegate void GridPositionSelected(Vector3 worldPosition);
    public event GridPositionSelected OnGridPositionSelected;

    // private Queue<byte[]> messageQueue = new Queue<byte[]>(); // Eliminado, MainWebSocketClient lo maneja
    // private object queueLock = new object(); // Eliminado
    // private bool isProcessingQueue = false; // Eliminado
    private Vector3 targetCursorPosition;
    private float cursorSmoothSpeed = 20f;
    private bool debugSpheresCreated = false; // Para crear las esferas de debug solo una vez
    private bool isCursorVisible = false;

    void Start()
    {
        Debug.Log("Iniciando SAMSystemController");
        
        // Obtener la instancia de PhotonManager
        photonManager = PhotonManager.Instance;
        
        // Verificar componentes críticos
        if (gridCursor == null) Debug.LogError("¡gridCursor no está asignado en el Inspector!");
        if (validPositionMaterial == null) Debug.LogError("¡validPositionMaterial no está asignado en el Inspector!");
        if (invalidPositionMaterial == null) Debug.LogError("¡invalidPositionMaterial no está asignado en el Inspector!");
        if (mainWebSocketClient == null) Debug.LogError("¡mainWebSocketClient no está asignado en el Inspector!");
        
        cameraTexture = new Texture2D(1, 1);
        maskTexture = new Texture2D(1, 1);

        if (cameraDisplay != null) cameraDisplay.texture = cameraTexture;
        if (maskDisplay != null) maskDisplay.texture = maskTexture;

        if (cameraDisplay != null) cameraDisplay.enabled = true;
        if (cameraDisplay != null) cameraDisplay.color = Color.white;

        if (maskDisplay != null) maskDisplay.enabled = false;
        if (maskDisplay != null) maskDisplay.color = Color.white;
        
        if (gridCursor != null)
        {
            gridCursor.transform.localScale = Vector3.one * cursorScale;
            targetCursorPosition = gridCursor.transform.position;
            CreateRangeIndicator();
        }

        // Suscribirse a los eventos del MainWebSocketClient
        if (mainWebSocketClient != null)
        {
            mainWebSocketClient.OnConnectionOpened += HandleConnectionOpened;
            mainWebSocketClient.OnCameraMessageReceived += HandleCameraMessage;
            mainWebSocketClient.OnProcessingCompleteReceived += HandleProcessingComplete;
            mainWebSocketClient.OnSamMaskReceived += HandleSamMaskMessage;
            mainWebSocketClient.OnPathPointsReceived += HandlePathPointsMessage;
            mainWebSocketClient.OnGridPositionReceived += HandleGridPosition;
            mainWebSocketClient.OnGridPositionConfirmed += HandleGridConfirmation;
            mainWebSocketClient.OnProgressUpdateReceived += HandleProgressUpdate;
            // Podríamos también suscribirnos a OnError y OnClose si necesitamos lógica específica aquí
        }
        else
        {
            Debug.LogError("SAMSystemController: MainWebSocketClient no está asignado. No se pueden suscribir eventos.");
        }

        if (turretManager == null)
        {
            Debug.LogError("SamSocket: TurretManager no está asignado. Buscando en la escena...");
            turretManager = FindObjectOfType<TurretManager>();
            if (turretManager == null) {
                 Debug.LogError("SamSocket: No se pudo encontrar TurretManager en la escena.");
            }
        }

        SetCursorVisibility(false);
    }

    // Los métodos ConnectToServer, ProcessIncomingMessage, ProcessMessageFromQueue han sido eliminados
    // ya que MainWebSocketClient ahora maneja la conexión y la cola de mensajes.
    // La lógica de ProcessMessageFromQueue se distribuye en los nuevos manejadores de eventos.

    private void HandleConnectionOpened()
    {
        Debug.Log("SAMSystemController: Conectado al servidor a través de MainWebSocketClient.");
        // Ahora que estamos conectados, podemos enviar el mensaje START_CAMERA
        SendMessage("START_CAMERA");
    }

    private void HandleCameraMessage(byte[] messageData)
    {
        try {
            if (cameraTexture == null) cameraTexture = new Texture2D(2, 2);
            
            cameraTexture.LoadImage(messageData);
            cameraTexture.Apply();
            
            if (cameraDisplay != null) cameraDisplay.enabled = true;
            if (maskDisplay != null) maskDisplay.enabled = false;
            
            mainCameraConnected = true;
            
            if (combatModeJustStarted && gestureReceiver != null)
            {
                gestureReceiver.OnCombatModeStarted();
                combatModeJustStarted = false;
            }
        } catch (Exception e) {
            Debug.LogError($"Error al cargar imagen de cámara: {e.Message}");
        }
    }

    private void HandleProcessingComplete()
    {
        processingFrame = false;
        if (cameraDisplay != null) cameraDisplay.enabled = true;
        if (maskDisplay != null) maskDisplay.enabled = false;
    }

    private void HandleSamMaskMessage(byte[] messageData)
    {
        try {
            if (maskTexture == null) maskTexture = new Texture2D(2, 2);
            
            maskTexture.LoadImage(messageData);
            if (maskDisplay != null) maskDisplay.texture = maskTexture;
            
            if (gestureReceiver != null)
            {
                gestureReceiver.OnServerResponseReceived();
            }

            // Actualizamos el progreso pero no ocultamos la pantalla todavía
            if (LoadingManager.Instance != null)
            {
                LoadingManager.Instance.UpdateProgress("Máscara de segmentación recibida", 85f);
            }
        } catch (Exception e) {
            Debug.LogError($"Error al cargar máscara: {e.Message}");
        }
    }

    private void HandlePathPointsMessage(byte[] messageData)
    {
        try 
        {
            string jsonPath = Encoding.UTF8.GetString(messageData);
            Vector2Serializable[] pathPoints = JsonHelper.FromJson<Vector2Serializable>(jsonPath);
            
            if (pathPoints != null && pathPoints.Length > 0) 
            {
                DrawPath(pathPoints); // La lógica de DrawPath se mantiene
            }
            
            if (gestureReceiver != null)
            {
                gestureReceiver.OnServerResponseReceived();
            }

            // Ahora que la ruta (el último paso) ha llegado, ocultamos la pantalla de carga
            if (LoadingManager.Instance != null)
            {
                LoadingManager.Instance.Hide();
            }

            // Notificar a otros jugadores que el procesamiento ha terminado.
            if (photonManager != null && photonManager.IsMasterClient())
            {
                photonManager.BroadcastSamComplete();
            }
        }
        catch (Exception e) 
        {
            Debug.LogError($"JSON parsing error en PathPoints: {e.Message}");
            // Si hay un error, también ocultamos la pantalla para no bloquear al jugador
            if (LoadingManager.Instance != null)
            {
                LoadingManager.Instance.Hide();
            }
        }
    }

    private void HandleProgressUpdate(byte[] messageData)
    {
        if (LoadingManager.Instance == null) return;

        try
        {
            string json = Encoding.UTF8.GetString(messageData);
            ProgressUpdate update = JsonUtility.FromJson<ProgressUpdate>(json);
            
            // Actualizar la UI de carga localmente
            LoadingManager.Instance.UpdateProgress(update.step, update.progress);

            // Si estamos en modo multijugador (comprobando si photonManager no es nulo y estamos en una sala),
            // y somos el MasterClient (el "Colocador"), retransmitimos el progreso.
            if (photonManager != null && photonManager.IsMasterClient())
            {
                photonManager.BroadcastProgressUpdate(update.step, update.progress);
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error procesando el mensaje de progreso: {e}");
        }
    }

    private void HandleGridPosition(byte[] messageData)
    {
        if (!isCursorVisible) SetCursorVisibility(true);

        string json = Encoding.UTF8.GetString(messageData);
        GridPosition gridPos = JsonUtility.FromJson<GridPosition>(json);
        UpdateGridCursor(gridPos);
    }

    private void HandleGridConfirmation(byte[] messageData)
    {
        string json = Encoding.UTF8.GetString(messageData);
        GridPosition gridPos = JsonUtility.FromJson<GridPosition>(json);

        if (gridPos.valid && turretManager != null)
            {
            Vector3 worldPosition = ConvertGridToWorld(gridPos.x, gridPos.y);
            turretManager.PlaceSelectedTurret(worldPosition);
            Debug.Log($"Solicitud para colocar la torreta seleccionada en: {worldPosition}");
            }
        }

    private Vector3 ConvertGridToWorld(float gridX, float gridY)
        {
        if (Camera.main == null) {
            Debug.LogError("ConvertGridToWorld: Camera.main es nula.");
            return Vector3.zero;
        }

        float orthoHeight = Camera.main.orthographicSize;
        float orthoWidth = orthoHeight * Camera.main.aspect;
        float originalWidth = 640f;  // Ancho de la cámara de Python
        float originalHeight = 480f; // Alto de la cámara de Python
        float scaleX = (orthoWidth * 2f) / originalWidth;
        float scaleY = (orthoHeight * 2f) / originalHeight;

        float worldX = (gridX - originalWidth / 2f) * scaleX;
        float worldY = -(gridY - originalHeight / 2f) * scaleY;

        // La posición Z puede necesitar ajuste dependiendo de tu configuración de cámara
        return new Vector3(worldX, worldY, 0); 
    }

    private void DrawPath(Vector2Serializable[] pathPoints) 
    {
        // Clear previous path spheres
        ClearPathSpheres(); // Usa el método existente para limpiar esferas
        
        if (pathPoints == null || pathPoints.Length == 0)
        {
            Debug.LogWarning("No se recibió un path válido para dibujar.");
            return;
        }
        
        storedWorldPath = ConvertPathToWorldCoordinates(pathPoints);
        
        if(!inCombatMode)
        {
            DrawPathSpheres(pathPoints); // Este método ya existe
        }
        else 
        {
            Debug.Log($"Path procesado y almacenado con {storedWorldPath.Count} puntos mientras está en modo combate.");
        }
    }
    
    private void DrawPathSpheres(Vector2Serializable[] pathPoints)
    {
        if (Camera.main == null) {
            Debug.LogError("DrawPathSpheres: Camera.main es nula.");
            return;
        }
        float orthoHeight = Camera.main.orthographicSize;
        float orthoWidth = orthoHeight * Camera.main.aspect;
        float originalWidth = 640f;
        float originalHeight = 480f;
        float scaleX = (orthoWidth * 2f) / originalWidth;
        float scaleY = (orthoHeight * 2f) / originalHeight;
        float zPos = -5f;
        int step = 15; 

        for (int i = 0; i < pathPoints.Length; i += step)
        {
            var point = pathPoints[i];
            float worldX = (point.x - originalWidth / 2f) * scaleX;
            float worldY = -(point.y - originalHeight / 2f) * scaleY;
            CreateSphere(new Vector3(worldX, worldY, zPos), Color.cyan);
        }

        if (pathPoints.Length > 0)
        {
            float firstX = (pathPoints[0].x - originalWidth / 2f) * scaleX;
            float firstY = -(pathPoints[0].y - originalHeight / 2f) * scaleY;
            Debug.Log($"First path point: Screen=({pathPoints[0].x}, {pathPoints[0].y}) -> World=({firstX}, {firstY})");
        }
    }

    private List<Vector3> ConvertPathToWorldCoordinates(Vector2Serializable[] pathPoints)
    {
        List<Vector3> worldPath = new List<Vector3>();
        if (Camera.main == null) {
            Debug.LogError("ConvertPathToWorldCoordinates: Camera.main es nula.");
            return worldPath; // Devuelve lista vacía
        }
        float orthoHeight = Camera.main.orthographicSize;
        float orthoWidth = orthoHeight * Camera.main.aspect;
        float originalWidth = 640f;
        float originalHeight = 480f;
        float scaleX = (orthoWidth * 2f) / originalWidth;
        float scaleY = (orthoHeight * 2f) / originalHeight;
        float zPos = -3f;
        
        foreach (var point in pathPoints)
        {
            float worldX = (point.x - originalWidth / 2f) * scaleX;
            float worldY = -(point.y - originalHeight / 2f) * scaleY;
            worldPath.Add(new Vector3(worldX, worldY, zPos));
        }
        return worldPath;
    }
    
    private void CreateDebugPathSpheres(List<Vector3> worldPath)
    {
        if (worldPath == null || worldPath.Count == 0) return;
        int step = Mathf.Max(1, worldPath.Count / 10);
        for (int i = 0; i < worldPath.Count; i += step)
        {
            CreateSphere(worldPath[i], Color.yellow, 0.2f);
        }
        if (worldPath.Count > 0)
        {
            CreateSphere(worldPath[0], Color.green, 0.3f);
            CreateSphere(worldPath[worldPath.Count - 1], Color.red, 0.3f);
        }
    }

    private void CreateSphere(Vector3 position, Color color, float scale = 0.3f)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * scale;
        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderer != null) 
        {
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = color;
        }
        // Asegurarse que PulsingSphere exista o manejar su ausencia
        if (sphere.GetComponent<PulsingSphere>() == null && typeof(PulsingSphere).IsSubclassOf(typeof(Component)))
        {
            sphere.AddComponent<PulsingSphere>();
        }
        else if (typeof(PulsingSphere) == null)
        {
            Debug.LogWarning("CreateSphere: El script PulsingSphere no se encontró o no es un componente.");
        }

        pathSpheres.Add(sphere);
    }

    private GameObject CreateDebugSphere(Vector3 position, Color color)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position = position;
        sphere.transform.localScale = Vector3.one * 0.35f; 
        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = new Material(Shader.Find("Standard"));
            renderer.material.color = color;
        }
        return sphere;
    }

    void DrawDebugEdgeSpheres()
    {
        if (Camera.main == null)
        {
            Debug.LogError("DrawDebugEdgeSpheres: Camera.main no está disponible.");
            return;
        }
        // La lógica para crear las esferas en los bordes se mantiene aquí
        // Este método debe ser revisado para asegurar que su lógica de dibujo sea correcta
        // y que cree los GameObjects como se espera (ej. usando CreateDebugSphere).
        // Por ahora, el cuerpo del método está vacío como en el original, pero necesita implementación.
        float orthoHeight = Camera.main.orthographicSize;
        float orthoWidth = orthoHeight * Camera.main.aspect;
        float originalWidth = 640f; 
        float originalHeight = 480f;
        float scaleX = (orthoWidth * 2f) / originalWidth;
        float scaleY = (orthoHeight * 2f) / originalHeight;
        float zPos = -5f; 
        // Ejemplo de cómo podrías crear las esferas:
        // CreateDebugSphere(new Vector3(-orthoWidth + (0 - originalWidth / 2f) * scaleX, -(0 - originalHeight / 2f) * scaleY, zPos), Color.magenta); // Top-left
        // CreateDebugSphere(new Vector3(-orthoWidth + (originalWidth - originalWidth / 2f) * scaleX, -(0 - originalHeight / 2f) * scaleY, zPos), Color.magenta); // Top-right
        // CreateDebugSphere(new Vector3(-orthoWidth + (0 - originalWidth / 2f) * scaleX, -(originalHeight - originalHeight / 2f) * scaleY, zPos), Color.magenta); // Bottom-left
        // CreateDebugSphere(new Vector3(-orthoWidth + (originalWidth - originalWidth / 2f) * scaleX, -(originalHeight - originalHeight / 2f) * scaleY, zPos), Color.magenta); // Bottom-right
         Debug.Log("DrawDebugEdgeSpheres fue llamado pero no tiene implementación para crear esferas.");
    }

    private void UpdateGridCursor(GridPosition gridPos)
    {
        if (gridCursor == null) return;
        if (Camera.main == null) {
            Debug.LogError("UpdateGridCursor: Camera.main es nula.");
            return;
        }

        float orthoHeight = Camera.main.orthographicSize;
        float orthoWidth = orthoHeight * Camera.main.aspect;
        float originalWidth = 640f;
        float originalHeight = 480f;
        float scaleX = (orthoWidth * 2f) / originalWidth;
        float scaleY = (orthoHeight * 2f) / originalHeight;

        float worldX = (gridPos.x - originalWidth / 2f) * scaleX;
        float worldY = -(gridPos.y - originalHeight / 2f) * scaleY;
        
        targetCursorPosition = new Vector3(worldX, worldY, -5f);

        Renderer renderer = gridCursor.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = gridPos.valid ? validPositionMaterial : invalidPositionMaterial;
        }

        if (gridPos.valid)
        {
            OnGridPositionSelected?.Invoke(targetCursorPosition);
        }
    }

    private void ClearPathSpheres()
    {
        foreach (var sphere in pathSpheres)
        {
            if (sphere != null) Destroy(sphere);
        }
        pathSpheres.Clear();
    }

    private void RedrawStoredPath()
    {
        if (storedWorldPath == null || storedWorldPath.Count == 0) return;
        ClearPathSpheres(); // Limpiar esferas existentes antes de redibujar
        int step = Mathf.Max(1, storedWorldPath.Count / 15);
        for (int i = 0; i < storedWorldPath.Count; i += step)
        {
            Vector3 spherePos = storedWorldPath[i];
            spherePos.z = -5f;
            CreateSphere(spherePos, Color.cyan);
        }
        Debug.Log($"Path redibujado con {pathSpheres.Count} esferas");
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
        
        UpdateRangeIndicator();

        // ProcessQueuedMessages(); // Eliminado, MainWebSocketClient lo maneja
        
        // #if !UNITY_WEBGL || UNITY_EDITOR // Eliminado, MainWebSocketClient lo maneja
        // if (websocket != null)
        // {
        //     websocket.DispatchMessageQueue();
        // }
        // #endif

        // Dibujar esferas de depuración una vez que la cámara principal esté conectada
        if (mainCameraConnected && !debugSpheresCreated && Camera.main != null) // Añadida comprobación de Camera.main
        {
            DrawDebugEdgeSpheres(); // Asumiendo que DrawDebugEdgeSpheres usa Camera.main
            debugSpheresCreated = true;
        }

        // Verificar si las cámaras están conectadas
        if (!connectionChecked)
        {
            connectionTimer += Time.deltaTime;
            
            if (connectionTimer >= connectionTimeout)
            {
                connectionChecked = true;
                
                if (!mainCameraConnected) // mainCameraConnected es actualizado por HandleCameraMessage
                {
                    Debug.LogWarning("No se detectó conexión con la cámara principal (desde SAMSystemController). Redirigiendo a la escena de verificación.");
                    // Asegurarse que InitialSceneLoader exista antes de llamar
                    if (FindObjectOfType<InitialSceneLoader>() != null) 
                    {
                         // Esta lógica de redirección quizás debería ser más centralizada o manejada por un game manager
                        SceneManager.LoadScene("CameraVerification");
                    }
                    else
                    {
                        Debug.LogWarning("InitialSceneLoader no encontrado. No se puede redirigir.");
                    }
                }
            }
        }

        // Comprobar mainWebSocketClient.IsConnected en lugar de isConnected
        if (Input.GetKeyDown(KeyCode.Space) && mainWebSocketClient != null && mainWebSocketClient.IsConnected && !processingFrame)
        {
            SendMessage("PROCESS_SAM");
        }

        // Tecla C para alternar modo combate
        if (Input.GetKeyDown(KeyCode.C) && mainWebSocketClient != null && mainWebSocketClient.IsConnected)
        {
            // inCombatMode se actualiza dentro de SendMessage cuando es START_COMBAT o STOP_COMBAT
            SendMessage(inCombatMode ? "STOP_COMBAT" : "START_COMBAT");
        }
    }
    
    // ProcessQueuedMessages(); // Ya está eliminado arriba, pero por si acaso se repite en el diff

    // Modificar SendMessage para usar mainWebSocketClient
    public async void SendMessage(string message) // Cambiado a public async void para mantener la firma pero Task sería mejor
    {
        if (mainWebSocketClient == null || !mainWebSocketClient.IsConnected)
        {
            Debug.LogWarning($"SAMSystemController: No se puede enviar mensaje '{message}'. MainWebSocketClient no está disponible o conectado.");
            return;
        }

        // La lógica de combatModeJustStarted, inCombatMode, etc., se mantiene aquí
        // ya que es específica del sistema SAM y no del cliente WebSocket genérico.
        if (message == "PROCESS_SAM")
        {
            if (processingFrame)
            {
                Debug.LogWarning("SAMSystemController: Se ha ignorado la solicitud 'PROCESS_SAM' porque ya hay una en curso.");
                return;
            }
            processingFrame = true;
            if (LoadingManager.Instance != null)
            {
                LoadingManager.Instance.Show("Procesando el escenario...", true);
            }
        }
        else if (message == "START_COMBAT")
        {
            combatModeJustStarted = true;
            inCombatMode = true;
            
            ClearPathSpheres();
            Debug.Log("Esferas del camino eliminadas al entrar en modo combate");
            
            if (monsterManager != null && storedWorldPath != null && storedWorldPath.Count > 0)
            {
                monsterManager.SetPath(storedWorldPath);
                Debug.Log($"Modo combate iniciado - Path enviado al MonsterManager con {storedWorldPath.Count} puntos");
            }
            else if (monsterManager == null) Debug.LogWarning("MonsterManager no está asignado en SAMSystemController");
            else Debug.LogWarning("No hay path válido almacenado para iniciar modo combate. Ejecuta PROCESS_SAM primero.");
        }
        else if (message == "STOP_COMBAT")
        {
            inCombatMode = false;
            combatModeJustStarted = false;
            
            if (monsterManager != null) monsterManager.StopAllWaves();
            Debug.Log("Oleadas de monstruos detenidas al salir del modo combate");
            
            if (storedWorldPath != null && storedWorldPath.Count > 0)
            {
                RedrawStoredPath();
                Debug.Log("Path redibujado al salir del modo combate");
            }
        }
        
        // Enviar el mensaje a través del MainWebSocketClient
        await mainWebSocketClient.SendMessageAsync(message);
    }

    // OnApplicationQuit ya no necesita manejar el WebSocket, MainWebSocketClient lo hace.
    // private async void OnApplicationQuit()
    // {
    //     if (websocket != null && websocket.State == WebSocketState.Open)
    //     {
    //         await websocket.Close();
    //     }
    // }

    void OnDestroy()
    {
        // Darse de baja de los eventos para evitar errores si MainWebSocketClient se destruye después
        if (mainWebSocketClient != null)
        {
            mainWebSocketClient.OnConnectionOpened -= HandleConnectionOpened;
            mainWebSocketClient.OnCameraMessageReceived -= HandleCameraMessage;
            mainWebSocketClient.OnProcessingCompleteReceived -= HandleProcessingComplete;
            mainWebSocketClient.OnSamMaskReceived -= HandleSamMaskMessage;
            mainWebSocketClient.OnPathPointsReceived -= HandlePathPointsMessage;
            mainWebSocketClient.OnGridPositionReceived -= HandleGridPosition;
            mainWebSocketClient.OnGridPositionConfirmed -= HandleGridConfirmation;
            mainWebSocketClient.OnProgressUpdateReceived -= HandleProgressUpdate;
        }
    }

    private void CreateRangeIndicator()
    {
        if (gridCursor == null) return;

        rangeIndicatorObject = new GameObject("RangeIndicator");
        rangeIndicatorObject.transform.SetParent(gridCursor.transform, false);
        rangeIndicatorObject.transform.localPosition = Vector3.zero;

        rangeIndicatorRenderer = rangeIndicatorObject.AddComponent<LineRenderer>();
        rangeIndicatorRenderer.material = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
        rangeIndicatorRenderer.startColor = new Color(1, 0, 0, 0.6f);
        rangeIndicatorRenderer.endColor = new Color(1, 0, 0, 0.6f);
        rangeIndicatorRenderer.startWidth = 0.1f;
        rangeIndicatorRenderer.endWidth = 0.1f;
        rangeIndicatorRenderer.positionCount = rangeIndicatorSegments + 1;
        rangeIndicatorRenderer.useWorldSpace = false;
        rangeIndicatorRenderer.loop = true;
    }

    private void UpdateRangeIndicator()
    {
        if (rangeIndicatorObject == null || rangeIndicatorRenderer == null || turretManager == null)
        {
            return;
        }

        TurretData selectedTurret = turretManager.GetSelectedTurretData();
        bool shouldBeVisible = isCursorVisible && selectedTurret != null;

        if (rangeIndicatorObject.activeSelf != shouldBeVisible)
        {
            rangeIndicatorObject.SetActive(shouldBeVisible);
        }

        if (!shouldBeVisible)
        {
            return;
        }

        float radius = selectedTurret.range;
        if (rangeIndicatorRenderer.positionCount != rangeIndicatorSegments + 1)
        {
            rangeIndicatorRenderer.positionCount = rangeIndicatorSegments + 1;
        }

        Vector3[] points = new Vector3[rangeIndicatorSegments + 1];
        for (int i = 0; i <= rangeIndicatorSegments; i++)
        {
            float angle = ((float)i / (float)rangeIndicatorSegments) * 2.0f * Mathf.PI;
            float x = radius * Mathf.Cos(angle);
            float y = radius * Mathf.Sin(angle);
            points[i] = new Vector3(x, y, 0);
        }
        rangeIndicatorRenderer.SetPositions(points);
    }

    public void SetCursorVisibility(bool visible)
    {
        isCursorVisible = visible;
        if (gridCursor != null)
        {
            gridCursor.SetActive(visible);
        }
    }
} 