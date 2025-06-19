using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using Photon.Pun;

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

    void Start()
    {
        // Asegúrate de que los botones de rol estén activos al entrar
        selectorButton.interactable = true;
        placerButton.interactable = true;
        
        // El botón de empezar solo lo puede usar el Master Client
        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(PhotonNetwork.IsMasterClient);
        }
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        // Si cambia el Master Client, actualizamos quién ve el botón de empezar
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
            // Photon se encargará de que la carga de la escena esté sincronizada
            PhotonNetwork.LoadLevel("4_Game_Selector");
        }
        else if (localPlayerRole == PlayerRole.Placer)
        {
            await BackendManager.Instance.RequestBackendMode(BackendMode.MultiplayerPlacer);
            PhotonNetwork.LoadLevel("3_Game_Placer");
        }
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
