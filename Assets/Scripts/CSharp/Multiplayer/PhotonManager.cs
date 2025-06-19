using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using System;

public class PhotonManager : MonoBehaviourPunCallbacks
{
    public static PhotonManager Instance { get; private set; }

    // Event invoked when the local player successfully joins a room.
    public event Action OnJoinedRoomEvent;

    // Event to notify the game controller about a phase change request from the selector.
    public event Action<string> OnPhaseChangeReceived;
    
    // Event to notify the game controller about a turret selection from the selector.
    public event Action<int> OnTurretSelectReceived;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            // Si ya existe una instancia (de una escena anterior), destruimos esta nueva.
            Debug.Log("Destruyendo instancia duplicada de PhotonManager.");
            Destroy(gameObject);
            return;
        }
        // Si no existe, nos convertimos en la instancia única.
        Instance = this;
        DontDestroyOnLoad(gameObject); // ¡No destruir este objeto al cargar nuevas escenas!
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

    void Start()
    {
        Debug.Log("Connecting to Photon...");
        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        Debug.Log("Connected to Photon Master Server.");
        // Automatically join a lobby. You can create or join rooms after this.
        PhotonNetwork.JoinLobby();
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
} 