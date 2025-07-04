using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Gestiona la lógica de navegación del panel de tutoriales del menú principal.
/// Se encarga de cargar las escenas de tutorial y de cerrar el panel.
/// Este script interactúa con UniversalPanelManager para volver al menú anterior.
/// </summary>
public class TutorialControllerMenu : MonoBehaviour
{
    private const string COLOCADOR_TUTORIAL_SCENE = "7_Tutorial_Colocador";
    private const string SELECTOR_TUTORIAL_SCENE = "6_Tutorial_Selector";

    /// <summary>
    /// Carga la escena del tutorial para el "Colocador".
    /// Este método está diseñado para ser llamado desde el evento OnClick de un botón de UI,
    /// que a su vez es activado por un SimpleHandGesture.
    /// </summary>
    public void LoadColocadorTutorial()
    {
        Debug.Log($"Cargando escena del tutorial del Colocador: {COLOCADOR_TUTORIAL_SCENE}");
        SceneManager.LoadScene(COLOCADOR_TUTORIAL_SCENE);
    }

    /// <summary>
    /// Carga la escena del tutorial para el "Selector".
    /// Este método está diseñado para ser llamado desde el evento OnClick de un botón de UI,
    /// que a su vez es activado por un SimpleHandGesture.
    /// </summary>
    public void LoadSelectorTutorial()
    {
        Debug.Log($"Cargando escena del tutorial del Selector: {SELECTOR_TUTORIAL_SCENE}");
        SceneManager.LoadScene(SELECTOR_TUTORIAL_SCENE);
    }

    /// <summary>
    /// Cierra el panel de tutorial actual y vuelve al panel anterior (generalmente el menú principal).
    /// Utiliza el UniversalPanelManager para gestionar la transición.
    /// </summary>
    public void CloseTutorialPanel()
    {
        Debug.Log("Cerrando panel de tutorial y volviendo al panel anterior.");
        // UniversalPanelManager se encarga de la lógica de volver atrás.
        if (UniversalPanelManager.Instance != null)
        {
            UniversalPanelManager.Instance.GoBack();
        }
        else
        {
            Debug.LogError("No se encontró una instancia de UniversalPanelManager.");
        }
    }
} 