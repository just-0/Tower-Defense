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
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
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
    public void SendPhaseChange(string phaseName)
    {
        if (PhotonNetwork.InRoom)
        {
            photonView.RPC("RPC_ReceivePhaseChange", RpcTarget.Others, phaseName);
            Debug.Log($"Sent RPC for phase change: {phaseName}");
        }
    }

    // Called by the Selector player to send a turret selection command.
    public void SendTurretSelection(int turretIndex)
    {
        if (PhotonNetwork.InRoom)
        {
            photonView.RPC("RPC_ReceiveTurretSelection", RpcTarget.Others, turretIndex);
            Debug.Log($"Sent RPC for turret selection: {turretIndex}");
        }
    }

    [PunRPC]
    public void RPC_ReceivePhaseChange(string phaseName)
    {
        Debug.Log($"Received RPC for phase change: {phaseName}");
        OnPhaseChangeReceived?.Invoke(phaseName);
    }

    [PunRPC]
    public void RPC_ReceiveTurretSelection(int turretIndex)
    {
        Debug.Log($"Received RPC for turret selection: {turretIndex}");
        OnTurretSelectReceived?.Invoke(turretIndex);
    }
} 