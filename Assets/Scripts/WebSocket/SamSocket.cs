using UnityEngine;
using UnityEngine.UI;
using System.Text;
using NativeWebSocket;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;

public class SAMController : MonoBehaviour
{
    [Serializable]
    public struct Vector2Serializable
    {
        public float x;
        public float y;
        
        public override string ToString() => $"({x}, {y})";
    }

    [SerializeField] private RawImage cameraDisplay;
    [SerializeField] private RawImage maskDisplay;
    
    private Texture2D cameraTexture;
    private Texture2D maskTexture;
    private WebSocket websocket;
    private bool isConnected = false;
    private bool processingFrame = false;
    private string serverUrl = "ws://localhost:8767";
    private List<GameObject> pathSpheres = new List<GameObject>();

    async void Start()
    {
        cameraTexture = new Texture2D(1, 1);
        maskTexture = new Texture2D(1, 1);

        cameraDisplay.texture = cameraTexture;
        maskDisplay.texture = maskTexture;

        cameraDisplay.enabled = true;
        cameraDisplay.color = Color.white;

        maskDisplay.enabled = false;
        maskDisplay.color = Color.white;

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
            Buffer.BlockCopy(bytes, 1, imageData, 0, bytes.Length - 1);

            switch (messageType)
            {
                case 1: // Camera
                    cameraTexture.LoadImage(imageData);
                    cameraTexture.Apply();
                     
                    cameraDisplay.enabled = true;
                    maskDisplay.enabled = false; 
                    break;

                case 2: // Processing complete
                    processingFrame = false;
                    cameraDisplay.enabled = true;
                    maskDisplay.enabled = false;
                    break;

                case 3: // SAM mask
                    maskTexture = new Texture2D(2, 2);
                    maskTexture.LoadImage(imageData);
                    maskDisplay.texture = maskTexture;
                    //maskDisplay.enabled = false;
                    break;
                    
                case 4: // A* path points
                    try 
                    {
                        string jsonPath = Encoding.UTF8.GetString(imageData);
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
        
        // Draw each point
        foreach (var point in pathPoints)
        {
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
        sphere.GetComponent<Renderer>().material.color = color;
        pathSpheres.Add(sphere);
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