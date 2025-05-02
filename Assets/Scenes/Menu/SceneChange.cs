using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChange : MonoBehaviour
{
    [Tooltip("Nombre de la escena a cargar")]
    public string nombreDeEscena;

    public void CambiarEscena()
    {
        if (!string.IsNullOrEmpty(nombreDeEscena))
        {
            SceneManager.LoadScene(nombreDeEscena);
        }
        else
        {
            Debug.LogWarning("El nombre de la escena está vacío.");
        }
    }
}