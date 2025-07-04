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
    [Header("Configuration")]
    [SerializeField] private bool isEnabled = true;
    [SerializeField] private bool debugMode = false;
    
    private List<SimpleHandGesture> gestureComponents = new List<SimpleHandGesture>();
    private int currentFingerCount = 0;
    private int lastFingerCount = -1; // Para detectar cambios
    private float timer = 0f;
    private SimpleHandGesture activeGesture = null;
    private bool isInitialized = false;

    void Start()
    {
        if (debugMode)
            Debug.Log("✅ SimpleGestureManager activado como sistema principal");
        
        isInitialized = true;
        RefreshActiveGestureComponents();
    }

    void Update()
    {
        if (!isEnabled || !isInitialized) return;
        
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
            .Where(g => g != null && g.gameObject.activeInHierarchy && g.enabled)
            .ToList();

        // Actualizar la lista solo si ha habido un cambio para evitar logs innecesarios.
        if (!activeGestures.SequenceEqual(gestureComponents))
        {
            if (debugMode)
                Debug.Log($"🔄 Gestos activos cambiaron: {gestureComponents.Count} → {activeGestures.Count}");
            
            gestureComponents = activeGestures;
            
            // Si se eliminaron gestos, resetear el gesto activo si ya no existe
            if (activeGesture != null && !gestureComponents.Contains(activeGesture))
            {
                ResetActiveGesture();
            }
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
                if (debugMode)
                    Debug.Log($"🔄 Desactivando gesto anterior: {activeGesture.GetFingerCount()} dedos");
                
                activeGesture.onGestureStopped?.Invoke();
                activeGesture.SetActive(false);
                activeGesture.Reset();
            }

            // Establecer el nuevo gesto como activo y reiniciar el temporizador.
            activeGesture = matchingGesture;
            timer = 0f;

            if (activeGesture != null)
            {
                if (debugMode)
                    Debug.Log($"✅ Activando nuevo gesto: {activeGesture.GetFingerCount()} dedos");
                
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
                    if (debugMode)
                        Debug.Log($"✅ Gesto completado: {activeGesture.GetFingerCount()} dedos");
                    
                    ResetActiveGesture();
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
    /// Resetea el gesto activo de manera limpia
    /// </summary>
    private void ResetActiveGesture()
    {
        activeGesture = null;
        timer = 0f;
    }

    /// <summary>
    /// Método público para que sistemas externos (como MenuGestureController) actualicen el número de dedos.
    /// </summary>
    public void UpdateFingerCount(int fingerCount)
    {
        if (currentFingerCount != fingerCount)
        {
            lastFingerCount = currentFingerCount;
            currentFingerCount = fingerCount;
            
            if (debugMode && fingerCount > 0)
                Debug.Log($"👆 SimpleGestureManager: Dedos actualizados {lastFingerCount} → {currentFingerCount}");
        }
    }
    
    /// <summary>
    /// Permite habilitar/deshabilitar el sistema de gestos externamente
    /// </summary>
    public void SetEnabled(bool enabled)
    {
        if (isEnabled != enabled)
        {
            isEnabled = enabled;
            
            if (!enabled)
            {
                // Si se deshabilita, resetear cualquier gesto activo
                if (activeGesture != null)
                {
                    activeGesture.SetActive(false);
                    activeGesture.Reset();
                    ResetActiveGesture();
                }
                
                if (debugMode)
                    Debug.Log("❌ SimpleGestureManager deshabilitado");
            }
            else
            {
                if (debugMode)
                    Debug.Log("✅ SimpleGestureManager habilitado");
            }
        }
    }
    
    /// <summary>
    /// Obtiene información del estado actual para debugging
    /// </summary>
    public string GetStatusInfo()
    {
        return $"Dedos: {currentFingerCount}, Gestos activos: {gestureComponents.Count}, " +
               $"Gesto actual: {(activeGesture != null ? activeGesture.GetFingerCount().ToString() : "ninguno")}, " +
               $"Habilitado: {isEnabled}";
    }
    
    /// <summary>
    /// Fuerza la actualización de la lista de gestos (útil cuando se cambian paneles)
    /// </summary>
    public void ForceRefresh()
    {
        if (debugMode)
            Debug.Log("🔄 SimpleGestureManager: Forzando actualización de gestos");
        
        RefreshActiveGestureComponents();
    }

    void OnDisable()
    {
        // Limpiar cuando se desactiva
        if (activeGesture != null)
        {
            activeGesture.SetActive(false);
            activeGesture.Reset();
            ResetActiveGesture();
        }
    }
} 