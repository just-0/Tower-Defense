using UnityEngine;
using UnityEngine.SceneManagement;

public class Bootstrapper : MonoBehaviour
{
    void Start()
    {
        // Carga la escena de verificación de cámara primero
        SceneManager.LoadScene("0_CameraCheck");
    }
} 