using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using System;

public class PhotonManager : MonoBehaviourPunCallbacks
{
    private static PhotonManager _instance;
    public static PhotonManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindObjectOfType<PhotonManager>();
                if (_instance == null)
                {
                    GameObject managerPrefab = Resources.Load<GameObject>("_Managers");
                    if (managerPrefab != null)
                    {
                        GameObject managerObject = Instantiate(managerPrefab);
                        _instance = managerObject.GetComponent<PhotonManager>();
                        managerObject.name = "_Managers (Auto-Instanciado)";
                    }
                    else
                    {
                        Debug.LogError("¡No se pudo encontrar el prefab '_Managers' en la carpeta Resources! PhotonManager no puede funcionar.");
                        return null;
                    }
                }
            }
            return _instance;
        }
    }

    // Event invoked when the local player successfully joins a room.
    public event Action OnJoinedRoomEvent;

    // Event to notify the game controller about a phase change request from the selector.
    public event Action<string> OnPhaseChangeReceived;
    
    // Event to notify the game controller about a turret selection from the selector.
    public event Action<int> OnTurretSelectReceived;

    // --- NUEVOS EVENTOS PARA PANTALLA DE CARGA ---
    // Notifica a los clientes (principalmente al Selector) sobre una actualización de progreso.
    public event Action<string, float> OnProgressUpdateReceived;
    // Notifica a los clientes que el proceso SAM ha finalizado y la pantalla de carga debe ocultarse.
    public event Action OnSamProcessingComplete;
    // Notifica a los clientes sobre errores que requieren mostrar mensaje temporal.
    public event Action<string, string> OnErrorReceived;
    // --- FIN NUEVOS EVENTOS ---

    private PhotonView photonView; // Añadir referencia al PhotonView

    private void Awake()
    {
        if (_instance == null)
        {
            // Si no existe, nos convertimos en la instancia única.
            _instance = this;
            DontDestroyOnLoad(gameObject); // ¡No destruir este objeto al cargar nuevas escenas!
            photonView = GetComponent<PhotonView>(); // Obtener el componente PhotonView
            if (photonView == null)
            {
                Debug.LogError("¡PhotonManager necesita un componente PhotonView para funcionar!");
            }
        }
        else if (_instance != this)
        {
            // Si ya existe una instancia (de una escena anterior), destruimos esta nueva.
            Debug.Log("Destruyendo instancia duplicada de PhotonManager.");
            Destroy(gameObject);
        }
    }

    public override void OnEnable()
    {
        base.OnEnable();
        // Registrar el método para recibir eventos RPC
        PhotonNetwork.AddCallbackTarget(this);
    }

    public override void OnDisable()
    {
        base.OnDisable();
        // Quitar el registro para evitar errores
        PhotonNetwork.RemoveCallbackTarget(this);
    }

    // AÑADIMOS UN MÉTODO DE CONEXIÓN PÚBLICO
    public void ConnectToPhoton()
    {
        if (!PhotonNetwork.IsConnected)
        {
            Debug.Log("Conectando al Master de Photon...");
            PhotonNetwork.ConnectUsingSettings(); // Usa la configuración de tu archivo PhotonServerSettings
        }
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("¡Conectado al Master de Photon! Ahora se pueden crear o unir a salas.");
        // A diferencia de antes, no nos unimos a un lobby automáticamente.
        // Lo haremos desde la UI del Lobby.
    }

    public override void OnJoinedLobby()
    {
        Debug.Log("Joined Lobby.");
        // For simplicity, we can try to join a room or create one if it doesn't exist.
        // In a real game, you'd have UI for this.
        PhotonNetwork.JoinOrCreateRoom("GlobalGestureRoom", new RoomOptions { MaxPlayers = 2 }, TypedLobby.Default);
    }

    public override void OnJoinedRoom()
    {
        Debug.Log($"Joined room: {PhotonNetwork.CurrentRoom.Name}");
        Debug.Log($"Players in room: {PhotonNetwork.CurrentRoom.PlayerCount}");

        // Ahora que estamos en una sala, podemos identificar al master client
        if (IsMasterClient())
        {
            Debug.Log("Este cliente es el Master Client (Colocador).");
        }
        else
        {
            Debug.Log("Este cliente es un cliente regular (Selector).");
        }

        OnJoinedRoomEvent?.Invoke();
    }
    
    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        Debug.LogError($"Join Room Failed. Code: {returnCode}, Message: {message}");
    }
    
    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        Debug.Log($"A new player joined: {newPlayer.NickName}");
        Debug.Log($"Players in room: {PhotonNetwork.CurrentRoom.PlayerCount}");
    }

    // Called by the Selector player to send a phase change command.
    public void SendPhaseChange(string phaseCommand)
    {
        photonView.RPC("RPC_ReceivePhaseChange", RpcTarget.All, phaseCommand);
    }

    // Called by the Selector player to send a turret selection command.
    public void SendTurretSelect(int turretIndex)
    {
        photonView.RPC("RPC_ReceiveTurretSelect", RpcTarget.All, turretIndex);
    }

    // --- NUEVOS MÉTODOS DE RED PARA PROGRESO ---
    // Llamado por el Colocador para enviar actualizaciones de progreso a otros.
    public void SendProgressUpdate(string step, float progress)
    {
        photonView.RPC("RPC_ReceiveProgressUpdate", RpcTarget.Others, step, progress);
    }

    // Llamado por el Colocador cuando el proceso SAM ha terminado completamente.
    public void SendSamComplete()
    {
        photonView.RPC("RPC_ReceiveSamComplete", RpcTarget.Others);
    }
    // --- FIN NUEVOS MÉTODOS ---

    // --- NUEVOS MÉTODOS PÚBLICOS PARA RETRANSMISIÓN ---
    
    /// <summary>
    /// Retransmite una actualización de progreso a otros jugadores en la sala.
    /// Solo el Master Client (Colocador) debería llamar a esto.
    /// </summary>
    public void BroadcastProgressUpdate(string step, float progress)
    {
        if (PhotonNetwork.InRoom && IsMasterClient())
        {
            // Usa el RPC existente para enviar la información a los otros jugadores.
            photonView.RPC("RPC_ReceiveProgressUpdate", RpcTarget.Others, step, progress);
        }
    }

    /// <summary>
    /// Notifica a otros jugadores que el procesamiento SAM ha terminado.
    /// Solo el Master Client (Colocador) debería llamar a esto.
    /// </summary>
    public void BroadcastSamComplete()
    {
        if (PhotonNetwork.InRoom && IsMasterClient())
        {
            Debug.Log("[PhotonManager] Enviando RPC_ReceiveSamComplete a otros jugadores...");
            // Usa el RPC existente para notificar a los otros jugadores.
            photonView.RPC("RPC_ReceiveSamComplete", RpcTarget.Others);
        }
        else
        {
            Debug.LogWarning($"[PhotonManager] No se puede enviar BroadcastSamComplete - InRoom: {PhotonNetwork.InRoom}, IsMasterClient: {IsMasterClient()}");
        }
    }

    /// <summary>
    /// Retransmite un mensaje de error a otros jugadores en la sala.
    /// Solo el Master Client (Colocador) debería llamar a esto.
    /// </summary>
    public void BroadcastError(string errorMessage, string errorCode)
    {
        if (PhotonNetwork.InRoom && IsMasterClient())
        {
            // Usa el RPC para enviar el error a los otros jugadores.
            photonView.RPC("RPC_ReceiveError", RpcTarget.Others, errorMessage, errorCode);
        }
    }
    
    /// <summary>
    /// Comprueba si el cliente actual es el Master Client de la sala.
    /// </summary>
    /// <returns>True si es el Master Client, false en caso contrario.</returns>
    public bool IsMasterClient()
    {
        return PhotonNetwork.IsMasterClient;
    }

    // --- FIN NUEVOS MÉTODOS PÚBLICOS ---

    [PunRPC]
    private void RPC_ReceivePhaseChange(string phaseCommand)
    {
        Debug.Log($"[PhotonManager] RPC de cambio de fase recibido: {phaseCommand}");
        OnPhaseChangeReceived?.Invoke(phaseCommand);
    }

    [PunRPC]
    private void RPC_ReceiveTurretSelect(int turretIndex)
    {
        Debug.Log($"[PhotonManager] RPC de selección de torreta recibido: {turretIndex}");
        OnTurretSelectReceived?.Invoke(turretIndex);
    }

    // --- NUEVOS RPCs PARA MANEJAR EL PROGRESO ---
    [PunRPC]
    private void RPC_ReceiveProgressUpdate(string step, float progress)
    {
        Debug.Log($"[PhotonManager] RPC de progreso recibido: {step} - {progress}%");
        OnProgressUpdateReceived?.Invoke(step, progress);
    }

    [PunRPC]
    private void RPC_ReceiveSamComplete()
    {
        Debug.Log("[PhotonManager] RPC de finalización de SAM recibido. Invocando OnSamProcessingComplete...");
        OnSamProcessingComplete?.Invoke();
        Debug.Log("[PhotonManager] OnSamProcessingComplete invocado exitosamente.");
    }

    [PunRPC]
    private void RPC_ReceiveError(string errorMessage, string errorCode)
    {
        Debug.Log($"[PhotonManager] RPC de error recibido: {errorCode} - {errorMessage}");
        OnErrorReceived?.Invoke(errorMessage, errorCode);
    }
    // --- FIN NUEVOS RPCs ---
} 