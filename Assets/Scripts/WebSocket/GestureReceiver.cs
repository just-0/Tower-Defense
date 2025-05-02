using System;
using UnityEngine;
using UnityEngine.UI;
using NativeWebSocket;
using System.Text;
public class GestureReceiver : MonoBehaviour
{
    [SerializeField] private RawImage gestureImage;

    private WebSocket websocket;
    private Texture2D receivedTexture;
    public int dedos = 0;
    // Clase interna solo para representar los datos
    [Serializable]
    private class GestureData
    {
        public int fingers;
        public string image;
    }

    async void Start()
    {
        websocket = new WebSocket("ws://localhost:8765");

        websocket.OnMessage += (bytes) =>
        {
            string json = Encoding.UTF8.GetString(bytes);
            GestureData data = JsonUtility.FromJson<GestureData>(json);
            dedos = data.fingers;
            // Imprimir el número de dedos detectados
            Debug.Log($"Dedos detectados: {data.fingers}");

            byte[] imageBytes = Convert.FromBase64String(data.image);

            if (receivedTexture == null)
                receivedTexture = new Texture2D(2, 2);

            receivedTexture.LoadImage(imageBytes);
            gestureImage.texture = receivedTexture;
        };

        await websocket.Connect();
    }

    void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        websocket.DispatchMessageQueue();
#endif
    }

    private async void OnApplicationQuit()
    {
        await websocket.Close();
    }
}

