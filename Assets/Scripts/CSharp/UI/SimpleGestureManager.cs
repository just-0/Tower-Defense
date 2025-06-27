using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Gestiona todos los SimpleHandGesture activos en la escena.
/// Su única responsabilidad es detectar qué gesto corresponde al número de dedos actual,
/// y actualizar su progreso. La ejecución de la acción es responsabilidad de SimpleHandGesture.
/// </summary>
public class SimpleGestureManager : MonoBehaviour
{
    private List<SimpleHandGesture> gestureComponents = new List<SimpleHandGesture>();
    private int currentFingerCount = 0;
    private float timer = 0f;
    private SimpleHandGesture activeGesture = null;

    void Start()
    {
        // La actualización constante se encargará de encontrar los gestos.
        // Debug.Log("✅ SimpleGestureManager activado como sistema principal.");
    }

    void Update()
    {
        // Refrescar la lista de gestos disponibles en caso de que los paneles cambien.
        RefreshActiveGestureComponents();
        
        // Manejar la lógica de detección y progreso.
        HandleGestureLogic();
    }
    
    /// <summary>
    /// Encuentra todos los componentes SimpleHandGesture que están activos en la jerarquía
    /// y actualiza la lista interna de gestos a gestionar.
    /// </summary>
    void RefreshActiveGestureComponents()
    {
        // Buscar todos los gestos en la escena y filtrar solo los que están en un GameObject activo.
        var activeGestures = FindObjectsOfType<SimpleHandGesture>()
            .Where(g => g.gameObject.activeInHierarchy && g.enabled)
            .ToList();

        // Actualizar la lista solo si ha habido un cambio para evitar logs innecesarios.
        if (!activeGestures.SequenceEqual(gestureComponents))
        {
            // Debug.Log($"🔄 Gestos activos cambiaron: {gestureComponents.Count} → {activeGestures.Count}");
            gestureComponents = activeGestures;
        }
    }

    /// <summary>
    /// Contiene la lógica principal para activar, desactivar y actualizar el progreso de los gestos.
    /// </summary>
    void HandleGestureLogic()
    {
        // Buscar el gesto que coincide con los dedos actuales DENTRO de la lista de componentes activos.
        SimpleHandGesture matchingGesture = gestureComponents
            .FirstOrDefault(g => g.GetFingerCount() == currentFingerCount && !g.IsOnCooldown());

        // Si el gesto ha cambiado (o es nuevo/nulo)...
        if (activeGesture != matchingGesture)
        {
            // Desactivar y resetear el gesto anterior si existía.
            if (activeGesture != null)
            {
                activeGesture.onGestureStopped?.Invoke();
                activeGesture.SetActive(false);
                activeGesture.Reset();
            }

            // Establecer el nuevo gesto como activo y reiniciar el temporizador.
            activeGesture = matchingGesture;
            timer = 0f;

            if (activeGesture != null)
            {
                activeGesture.SetActive(true);
                activeGesture.onGestureStarted?.Invoke();
            }
        }

        // Si hay un gesto activo, actualizar su progreso.
        if (activeGesture != null && currentFingerCount > 0)
        {
            // Comprobar el modo de activación del gesto
            if (activeGesture.GetActivationMode() == GestureActivationMode.HoldToComplete)
            {
                // Comportamiento original: llenar la barra de progreso
                timer += Time.deltaTime;
                float progress = timer / activeGesture.GetHoldTime();
                activeGesture.UpdateProgress(progress);

                // Si el gesto se completa, el propio SimpleHandGesture se encargará de la acción.
                // Aquí, simplemente dejamos de considerarlo el gesto "activo" para que pueda empezar uno nuevo.
                if (progress >= 1f)
                {
                    activeGesture = null;
                    timer = 0f;
                }
            }
            else // Es modo Instant
            {
                // Nuevo comportamiento: mantener la barra llena mientras esté activo.
                // El gesto no se "completa" aquí, solo se desactiva cuando cambian los dedos.
                activeGesture.UpdateProgress(1f);
            }
        }
    }

    /// <summary>
    /// Método público para que sistemas externos (como MenuGestureController) actualicen el número de dedos.
    /// </summary>
    public void UpdateFingerCount(int fingerCount)
    {
        if (currentFingerCount != fingerCount)
        {
            currentFingerCount = fingerCount;
        }
    }
} 