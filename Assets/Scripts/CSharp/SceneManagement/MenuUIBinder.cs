using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Este componente se encarga de encontrar los elementos de la UI en su escena
/// y los registra con el singleton persistente MenuGestureController.
/// </summary>
public class MenuUIBinder : MonoBehaviour
{
    [Header("UI de la Escena")]
    [Tooltip("El RawImage que muestra la cámara en esta escena.")]
    [SerializeField] private RawImage cameraFeed;

    [Tooltip("El Texto que muestra el contador de dedos en esta escena.")]
    [SerializeField] private Text fingerCountText;

    [Tooltip("El panel de carga de esta escena.")]
    [SerializeField] private GameObject loadingPanel;

    void Start()
    {
        // Al iniciar esta escena, busca el gestor persistente y entrégale nuestras referencias de UI.
        if (MenuGestureController.Instance != null)
        {
            MenuGestureController.Instance.RegisterUI(cameraFeed, fingerCountText, loadingPanel);
        }
        else
        {
            Debug.LogError("MenuUIBinder: No se pudo encontrar la instancia de MenuGestureController. Asegúrate de que se cargue en una escena anterior.");
        }
    }

    void OnDisable()
    {
        // Opcional pero recomendado: Cuando salimos de la escena, le decimos al gestor
        // que olvide estas referencias para que no intente actualizar objetos destruidos.
        if (MenuGestureController.Instance != null)
        {
            MenuGestureController.Instance.UnregisterUI(this);
        }
    }
} 