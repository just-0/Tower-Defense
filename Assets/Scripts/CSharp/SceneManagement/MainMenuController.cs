using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI; // Necesario para el texto de estado
using NativeWebSocket; // Necesario para el sondeo
using System.Threading.Tasks; // Necesario para async

public class MainMenuController : MonoBehaviour
{
    [Header("UI Feedback")]
    [Tooltip("Panel que contiene los botones para desactivarlo durante la carga.")]
    [SerializeField] private GameObject buttonPanel;

    // Asigna este método al botón "Jugar Solo"
    public async void OnSinglePlayerClicked()
    {
        // Desactivar UI para evitar clics múltiples y dar feedback
        if (buttonPanel != null) buttonPanel.SetActive(false);
        LoadingManager.Instance.Show("Iniciando modo Un Jugador...");

        if (BackendManager.Instance != null)
        {
            Debug.Log("Solicitando modo Single-Player al backend...");
            await BackendManager.Instance.RequestBackendMode(BackendMode.SinglePlayer);
        }

        // --- SONDEO INTELIGENTE PARA SINGLE PLAYER ---
        LoadingManager.Instance.Show("Esperando al servidor del juego (SAM)...");
        bool gameServerReady = await PingServerUntilReady("ws://localhost:8767", 20f);
        
        LoadingManager.Instance.Show("Esperando al servidor de gestos...");
        bool gestureServerReady = await PingServerUntilReady("ws://localhost:8768", 10f);
        // ---------------------------------------------

        if (gameServerReady && gestureServerReady)
        {
            LoadingManager.Instance.Show("¡Todo listo! Cargando...");
            SceneManager.LoadSceneAsync("5_SinglePlayer");
        }
        else
        {
            Debug.LogError("Uno o más servidores no respondieron a tiempo. Volviendo al menú.");
            LoadingManager.Instance.Hide(); // Ocultamos la pantalla de carga
            if (buttonPanel != null) buttonPanel.SetActive(true); // Reactivar UI
        }
    }

    // Asigna este método al botón "Multijugador"
    public void OnMultiplayerClicked()
    {
        // Primero, le decimos a Photon que inicie la conexión
        if(PhotonManager.Instance != null)
        {
            PhotonManager.Instance.ConnectToPhoton();
        }
        
        // Navega al Lobby para la configuración multijugador
        SceneManager.LoadScene("2_Lobby");
    }

    // Asigna este método al botón "Salir"
    public void OnQuitClicked()
    {
        Debug.Log("Saliendo de la aplicación...");
        Application.Quit();
    }

    private async Task<bool> PingServerUntilReady(string url, float timeout)
    {
        float startTime = Time.time;
        while (Time.time - startTime < timeout)
        {
            var testSocket = new WebSocket(url);
            var tcs = new TaskCompletionSource<bool>();

            testSocket.OnOpen += () => tcs.TrySetResult(true);
            testSocket.OnError += (e) => tcs.TrySetResult(false);
            testSocket.OnClose += (code) => {
                // Si la tarea aún no se ha completado (por ejemplo, si el servidor cierra la conexión al instante),
                // la marcamos como fallida para evitar un bloqueo.
                tcs.TrySetResult(false);
            };

            testSocket.Connect();
            
            bool success = await tcs.Task;
            
            if (success)
            {
                // ¡Éxito! La conexión se estableció.
                // Cerramos el socket de prueba pero NO esperamos (await) la respuesta.
                // Esto evita la excepción "Operation Aborted" si el servidor no responde al cierre a tiempo.
                testSocket.Close();
                return true;
            }

            // Pequeña espera antes del siguiente intento para no saturar.
            await Task.Delay(250);
        }

        Debug.LogWarning($"El sondeo a {url} falló tras {timeout} segundos.");
        return false;
    }
} 