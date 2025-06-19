using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Photon.Pun;
using NativeWebSocket; // Asegurarnos de que usamos la librería correcta

public class LobbyController : MonoBehaviourPunCallbacks
{
    public enum PlayerRole { None, Selector, Placer }
    private PlayerRole localPlayerRole = PlayerRole.None; // Rol del jugador local

    [Header("UI References")]
    [Tooltip("El botón para empezar el juego. Solo visible para el Master Client.")]
    [SerializeField] private Button startGameButton;
    [Tooltip("Botón para elegir el rol de Selector.")]
    [SerializeField] private Button selectorButton;
    [Tooltip("Botón para elegir el rol de Colocador.")]
    [SerializeField] private Button placerButton;

    [Header("Game Start Settings")]
    [Tooltip("Segundos de espera para que el backend del 'Colocador' se inicie antes de cargar la escena. Aumentar si la conexión falla.")]
    [SerializeField] private float placerBackendStartDelay = 5.0f; // 5 segundos por defecto
    [Tooltip("Tiempo máximo en segundos que se intentará conectar con el backend del Colocador antes de rendirse.")]
    [SerializeField] private float placerConnectionTimeout = 15f;

    void Start()
    {
        // Asegúrate de que los botones de rol estén activos al entrar
        selectorButton.interactable = true;
        placerButton.interactable = true;
        
        // Desactivamos el botón al inicio por defecto.
        // Se activará en OnJoinedRoom o OnMasterClientSwitched si somos el Master.
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(false);
        }
    }

    // Este callback se ejecuta CUANDO el jugador local se une a una sala.
    // ¡Este es el lugar correcto para hacer la primera revisión!
    public override void OnJoinedRoom()
    {
        Debug.Log("Te has unido a una sala. Revisando si eres el Master Client...");
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        }
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        // Si cambia el Master Client, actualizamos quién ve el botón de empezar.
        // Esto es crucial si el Master original se desconecta.
        Debug.Log("El Master Client ha cambiado. Actualizando visibilidad del botón.");
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        }
    }

    // Asigna este método al botón "Ser Selector"
    public void OnSelectRoleSelector()
    {
        localPlayerRole = PlayerRole.Selector;
        Debug.Log("Has elegido el rol: Selector");
        // Deshabilitamos los botones para no poder cambiar de rol
        selectorButton.interactable = false;
        placerButton.interactable = false;
    }

    // Asigna este método al botón "Ser Colocador"
    public void OnSelectRolePlacer()
    {
        localPlayerRole = PlayerRole.Placer;
        Debug.Log("Has elegido el rol: Colocador");
        // Deshabilitamos los botones
        selectorButton.interactable = false;
        placerButton.interactable = false;
    }
    
    // Asigna este método al botón "Empezar Partida"
    public void OnStartGameClicked()
    {
        // El Master Client le dice a todos que el juego va a empezar
        photonView.RPC("StartGameSequence", RpcTarget.All);
    }

    [PunRPC]
    private async void StartGameSequence()
    {
        // Cada cliente (incluido el Master) revisa su rol LOCAL y actúa.
        if (localPlayerRole == PlayerRole.None)
        {
            Debug.LogError("Este jugador no eligió un rol, no se puede cargar la escena.");
            return;
        }

        if (localPlayerRole == PlayerRole.Selector)
        {
            await BackendManager.Instance.RequestBackendMode(BackendMode.MultiplayerSelector);
            PhotonNetwork.LoadLevel("4_Game_Selector");
        }
        else if (localPlayerRole == PlayerRole.Placer)
        {
            await BackendManager.Instance.RequestBackendMode(BackendMode.MultiplayerPlacer);
            
            // --- NUEVA LÓGICA DE SONDEO INTELIGENTE ---
            Debug.Log("Intentando conectar con el backend del Colocador...");
            bool isBackendReady = await PingServerUntilReady("ws://localhost:8767", placerConnectionTimeout);

            if (isBackendReady)
            {
                Debug.Log("¡Backend del Colocador listo! Cargando escena...");
                PhotonNetwork.LoadLevel("3_Game_Placer");
            }
            else
            {
                Debug.LogError($"El backend del Colocador no respondió en {placerConnectionTimeout} segundos. Abortando.");
                // Aquí podrías mostrar un mensaje de error en la UI y volver al menú.
                OnLeaveLobby(); 
            }
        }
    }

    /// <summary>
    /// Intenta conectar a una URL de WebSocket en un bucle hasta que tenga éxito o se agote el tiempo.
    /// </summary>
    /// <param name="url">La URL del servidor WebSocket a sondear.</param>
    /// <param name="timeout">El tiempo máximo de espera en segundos.</param>
    /// <returns>True si la conexión fue exitosa, false si se agotó el tiempo.</returns>
    private async System.Threading.Tasks.Task<bool> PingServerUntilReady(string url, float timeout)
    {
        float startTime = Time.time;

        while (Time.time - startTime < timeout)
        {
            // Usamos 'var' para que el tipo se infiera a NativeWebSocket.WebSocket
            var testSocket = new WebSocket(url);
            
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();

            // Definimos los delegados con la firma correcta de NativeWebSocket
            testSocket.OnOpen += () => 
            {
                tcs.TrySetResult(true);
            };

            testSocket.OnError += (string e) =>
            {
                // Imprimimos el error para depuración, pero no lo consideramos fatal aquí.
                // Simplemente significa que el servidor no está listo todavía.
                // Debug.Log($"Ping fallido (esperado): {e}");
                tcs.TrySetResult(false);
            };
            
            // Si el socket se cierra antes de abrirse, es un fallo.
            testSocket.OnClose += (WebSocketCloseCode code) =>
            {
                tcs.TrySetResult(false);
            };

            testSocket.Connect();

            // Esperamos el resultado de OnOpen u OnError/OnClose
            bool success = await tcs.Task;
            
            if (success)
            {
                await testSocket.Close(); // Cerramos la conexión de prueba
                return true; // ¡Éxito!
            }

            // Si falla, esperamos un poco antes de reintentar para no sobrecargar.
            await System.Threading.Tasks.Task.Delay(250); 
        }

        return false; // Se agotó el tiempo.
    }
    
    public void OnLeaveLobby()
    {
         // Para salir del lobby y volver al menú
         if (PhotonNetwork.InRoom)
         {
            PhotonNetwork.LeaveRoom();
         }
         SceneManager.LoadScene("1_MainMenu");
    }
}
