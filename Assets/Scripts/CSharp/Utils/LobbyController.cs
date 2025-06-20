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
    [Tooltip("Botón para unirse a una sala aleatoria.")]
    [SerializeField] private Button joinRoomButton;
    [SerializeField] private Text statusText; // Un texto para mostrar el estado

    [Header("Game Start Settings")]
    [Tooltip("Tiempo máximo en segundos que se intentará conectar con el backend del Colocador antes de rendirse.")]
    [SerializeField] private float placerConnectionTimeout = 15f;
    
    // --- ESTA ES LA PARTE NUEVA PARA LA INSTANCIACIÓN ---
    private static LobbyController _instance;
    
    void Awake()
    {
        _instance = this; // Guardamos la referencia a esta instancia
    }
    // ---------------------------------------------------

    void Start()
    {
        // Al empezar, todo deshabilitado hasta que nos conectemos al Master
        joinRoomButton.interactable = false;
        selectorButton.interactable = false;
        placerButton.interactable = false;
        startGameButton.gameObject.SetActive(false); // Oculto por defecto
        statusText.text = "Conectando a Photon...";

        // Si no estamos conectados, el PhotonManager debería conectarnos.
        // Si ya lo estamos, se llamará a OnConnectedToMaster inmediatamente.
        if (!PhotonNetwork.IsConnected)
        {
             PhotonManager.Instance.ConnectToPhoton();
        }
    }

    public override void OnConnectedToMaster()
    {
        statusText.text = "¡Conectado! Listo para unirse a una sala.";
        joinRoomButton.interactable = true; // Ahora sí se puede unir
    }

    public void JoinRandomRoom()
    {
         if (PhotonNetwork.IsConnected)
         {
             statusText.text = "Buscando sala...";
             joinRoomButton.interactable = false; // Deshabilitar mientras busca
             PhotonNetwork.JoinRandomRoom();
         }
    }
    
    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        statusText.text = "No se encontraron salas. Creando una nueva...";
        PhotonNetwork.CreateRoom(null, new Photon.Realtime.RoomOptions { MaxPlayers = 2 });
    }

    public override void OnJoinedRoom()
    {
        statusText.text = $"¡Unido a la sala! Elige un rol. Jugadores: {PhotonNetwork.CurrentRoom.PlayerCount}";
        joinRoomButton.gameObject.SetActive(false); // Ocultamos el botón de unirse
        selectorButton.interactable = true; // Activamos los botones de rol
        placerButton.interactable = true;
        UpdateStartButtonVisibility();
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        statusText.text = $"¡Se unió un jugador! Jugadores: {PhotonNetwork.CurrentRoom.PlayerCount}";
        UpdateStartButtonVisibility();
    }
    
    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        statusText.text = $"Un jugador se fue. Jugadores: {PhotonNetwork.CurrentRoom.PlayerCount}";
        UpdateStartButtonVisibility();
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        UpdateStartButtonVisibility();
    }

    private void UpdateStartButtonVisibility()
    {
        // El botón de empezar solo lo puede usar el Master Client y cuando hay 2 jugadores
        bool shouldBeVisible = PhotonNetwork.IsMasterClient && PhotonNetwork.CurrentRoom.PlayerCount == 2;
        startGameButton.gameObject.SetActive(shouldBeVisible);
    }
    
    public void OnSelectRoleSelector()
    {
        localPlayerRole = PlayerRole.Selector;
        selectorButton.interactable = false;
        placerButton.interactable = false;
    }

    public void OnSelectRolePlacer()
    {
        localPlayerRole = PlayerRole.Placer;
        selectorButton.interactable = false;
        placerButton.interactable = false;
    }
    
    public void OnStartGameClicked()
    {
        if (localPlayerRole == PlayerRole.None)
        {
            statusText.text = "¡Error: Debes elegir un rol primero!";
            return;
        }
        // El Master Client le dice a todos que el juego va a empezar
        photonView.RPC("StartGameSequence", RpcTarget.All, localPlayerRole);
    }

    [PunRPC]
    private async void StartGameSequence(PlayerRole roleForClient)
    {
        // CADA CLIENTE REVISA SU PROPIO ROL LOCAL
        if (this.localPlayerRole == PlayerRole.None) return;

        // Desactivamos la UI del lobby para que no se pueda hacer clic mientras carga
        selectorButton.interactable = false;
        placerButton.interactable = false;
        startGameButton.gameObject.SetActive(false);
        statusText.text = "Iniciando partida...";

        if (this.localPlayerRole == PlayerRole.Selector)
        {
            await BackendManager.Instance.RequestBackendMode(BackendMode.MultiplayerSelector);
            
            statusText.text = "Iniciando servicios del selector... por favor espera.";
            // El selector necesita el servidor de gestos en el puerto 8768
            bool isBackendReady = await PingServerUntilReady("ws://localhost:8768", placerConnectionTimeout);

            if (isBackendReady)
            {
                PhotonNetwork.LoadLevel("4_Game_Selector");
            }
            else
            {
                statusText.text = $"Error: El backend de gestos no respondió en {placerConnectionTimeout}s.";
                // Aquí podrías añadir un botón para volver al menú
            }
        }
        else if (this.localPlayerRole == PlayerRole.Placer)
        {
            await BackendManager.Instance.RequestBackendMode(BackendMode.MultiplayerPlacer);
            
            statusText.text = "Backend del colocador iniciando... por favor espera.";
            // El colocador necesita el servidor SAM en el puerto 8767
            bool isBackendReady = await PingServerUntilReady("ws://localhost:8767", placerConnectionTimeout);

            if (isBackendReady)
            {
                PhotonNetwork.LoadLevel("3_Game_Placer");
            }
            else
            {
                statusText.text = $"Error: El backend no respondió en {placerConnectionTimeout}s.";
                // Aquí podrías añadir un botón para volver al menú
            }
        }
    }

    private async System.Threading.Tasks.Task<bool> PingServerUntilReady(string url, float timeout)
    {
        float startTime = Time.time;

        while (Time.time - startTime < timeout)
        {
            var testSocket = new WebSocket(url);
            var tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();

            testSocket.OnOpen += () => { tcs.TrySetResult(true); };
            testSocket.OnError += (string e) => { tcs.TrySetResult(false); };
            testSocket.OnClose += (WebSocketCloseCode code) => { tcs.TrySetResult(false); };

            testSocket.Connect();

            bool success = await tcs.Task;
            
            if (success)
            {
                await testSocket.Close();
                return true;
            }
            await System.Threading.Tasks.Task.Delay(250); 
        }
        return false;
    }
    
    public async void OnLeaveLobby()
    {
         if (BackendManager.Instance != null)
         {
            await BackendManager.Instance.RequestBackendMode(BackendMode.Menu);
         }
         if (PhotonNetwork.InRoom) { PhotonNetwork.LeaveRoom(); }
         SceneManager.LoadScene("1_MainMenu");
    }
}
