using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    // Asigna este método al botón "Jugar Solo"
    public async void OnSinglePlayerClicked()
    {
        if (BackendManager.Instance != null)
        {
            Debug.Log("Solicitando modo Single-Player al backend...");
            await BackendManager.Instance.RequestBackendMode(BackendMode.SinglePlayer);
        }
        // Carga la escena del juego principal
        SceneManager.LoadScene("3_Game_Placer");
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
} 