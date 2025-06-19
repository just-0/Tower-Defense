using UnityEngine;
using UnityEngine.SceneManagement;

public class Bootstrapper : MonoBehaviour
{
    void Start()
    {
        // Carga la escena del menú principal por su nombre
        SceneManager.LoadScene("1_MainMenu");
    }
} 