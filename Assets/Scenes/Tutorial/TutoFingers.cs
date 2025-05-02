using System;
using UnityEngine;
using UnityEngine.UI;
using NativeWebSocket;
using System.Text;
using System.Collections;
public class Tutofingers : MonoBehaviour
{
    [SerializeField] private RawImage gestureImage;
    [SerializeField] public GameObject boton;
    private WebSocket websocket;
    private Texture2D receivedTexture;
    public int dedos = 0;
    public bool tutofinished = false;
    public bool tutoavalible = false;
    // Clase interna solo para representar los datos
    [Serializable]
    private class GestureData
    {
        public int fingers;
        public string image;
    }

    async void Start()
    {
        StartCoroutine(CambiarGlobalDespuesDe3Segundos());
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
        if (dedos == 2 && tutoavalible==true)
        {
            Debug.Log("DEDOS=2");
            if (tutofinished == false)
            {
                Debug.Log("What");

                tutofinished = true;
                boton.SetActive(true);
            }
        }

    }
    private IEnumerator CambiarGlobalDespuesDe3Segundos()
    {
        // Espera 3 segundos
        yield return new WaitForSeconds(2f);

        // Cambia la variable global
        tutoavalible = true;
    }


    private async void OnApplicationQuit()
    {
        await websocket.Close();
    }
}

