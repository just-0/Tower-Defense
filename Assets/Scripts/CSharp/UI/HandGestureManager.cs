using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class HandGestureManager : MonoBehaviour
{
    [Header("Gesture Configuration")]
    [SerializeField] private List<HandGestureData> availableGestures = new List<HandGestureData>();
    
    [Header("UI References")]
    [SerializeField] private Transform gestureContainer;
    [SerializeField] private GameObject gestureIndicatorPrefab;
    
    [Header("Settings")]
    [SerializeField] private bool autoCreateIndicators = true;
    [SerializeField] private float gestureCheckInterval = 0.1f;
    
    // Private fields
    private List<HandGestureIndicator> gestureIndicators = new List<HandGestureIndicator>();
    private int currentFingerCount = 0;
    private float gestureTimer = 0f;
    private HandGestureIndicator activeGestureIndicator = null;
    private float lastGestureCheckTime = 0f;
    
    // Events
    public System.Action<int, string> OnGestureCompleted; // fingerCount, actionText
    public System.Action<int> OnGestureStarted; // fingerCount
    public System.Action<int> OnGestureCanceled; // fingerCount
    public System.Action<int, float> OnGestureProgress; // fingerCount, progress (0-1)

    void Start()
    {
        if (autoCreateIndicators)
        {
            CreateGestureIndicators();
        }
        
        SetupGestureIndicators();
    }

    void Update()
    {
        // Limit gesture checking frequency for performance
        if (Time.time - lastGestureCheckTime >= gestureCheckInterval)
        {
            HandleGestureLogic();
            lastGestureCheckTime = Time.time;
        }
    }

    /// <summary>
    /// Crea automáticamente los indicadores de gestos basados en la configuración
    /// </summary>
    private void CreateGestureIndicators()
    {
        if (gestureContainer == null || gestureIndicatorPrefab == null)
        {
            Debug.LogError("HandGestureManager: gestureContainer o gestureIndicatorPrefab no están asignados");
            return;
        }

        // Limpiar indicadores existentes
        foreach (Transform child in gestureContainer)
        {
            if (Application.isPlaying)
                Destroy(child.gameObject);
            else
                DestroyImmediate(child.gameObject);
        }
        
        gestureIndicators.Clear();

        // Crear un indicador para cada gesto configurado
        foreach (var gestureData in availableGestures)
        {
            GameObject indicatorObj = Instantiate(gestureIndicatorPrefab, gestureContainer);
            HandGestureIndicator indicator = indicatorObj.GetComponent<HandGestureIndicator>();
            
            if (indicator != null)
            {
                indicator.SetupGesture(gestureData);
                gestureIndicators.Add(indicator);
            }
            else
            {
                Debug.LogError("HandGestureManager: El prefab no tiene el componente HandGestureIndicator");
                Destroy(indicatorObj);
            }
        }
    }

    /// <summary>
    /// Configura los eventos de los indicadores de gestos
    /// </summary>
    private void SetupGestureIndicators()
    {
        foreach (var indicator in gestureIndicators)
        {
            indicator.OnGestureCompleted += OnIndicatorGestureCompleted;
            indicator.OnGestureStarted += OnIndicatorGestureStarted;
            indicator.OnGestureCanceled += OnIndicatorGestureCanceled;
        }
    }

    /// <summary>
    /// Actualiza el número de dedos detectado desde el sistema de gestos
    /// </summary>
    public void UpdateFingerCount(int fingerCount)
    {
        currentFingerCount = fingerCount;
    }

    /// <summary>
    /// Maneja la lógica principal de detección de gestos
    /// </summary>
    private void HandleGestureLogic()
    {
        // Buscar el indicador que corresponde al número actual de dedos
        HandGestureIndicator matchingIndicator = gestureIndicators
            .FirstOrDefault(indicator => indicator.GetFingerCount() == currentFingerCount && !indicator.IsOnCooldown());

        // Si hay un gesto activo diferente al que debería estar activo
        if (activeGestureIndicator != null && activeGestureIndicator != matchingIndicator)
        {
            // Cancelar el gesto activo
            CancelCurrentGesture();
        }

        // Si encontramos un indicador que coincide y no hay uno activo
        if (matchingIndicator != null && activeGestureIndicator == null)
        {
            StartGesture(matchingIndicator);
        }
        // Si hay un gesto activo y coincide con el dedo detectado
        else if (activeGestureIndicator != null && activeGestureIndicator == matchingIndicator)
        {
            UpdateGestureProgress();
        }
        // Si no hay dedos detectados o el gesto no coincide
        else if (currentFingerCount == 0 || matchingIndicator == null)
        {
            CancelCurrentGesture();
        }
    }

    /// <summary>
    /// Inicia un nuevo gesto
    /// </summary>
    private void StartGesture(HandGestureIndicator indicator)
    {
        activeGestureIndicator = indicator;
        gestureTimer = 0f;
        
        indicator.SetActive(true);
        indicator.SetDetected(true);
        
        OnGestureStarted?.Invoke(indicator.GetFingerCount());
    }

    /// <summary>
    /// Actualiza el progreso del gesto activo
    /// </summary>
    private void UpdateGestureProgress()
    {
        if (activeGestureIndicator == null) return;

        gestureTimer += Time.deltaTime;
        float progress = gestureTimer / activeGestureIndicator.GetHoldTimeRequired();
        progress = Mathf.Clamp01(progress);
        
        activeGestureIndicator.SetProgress(progress);
        OnGestureProgress?.Invoke(activeGestureIndicator.GetFingerCount(), progress);
        
        // Completar gesto si se alcanzó el tiempo requerido
        if (progress >= 1f)
        {
            CompleteCurrentGesture();
        }
    }

    /// <summary>
    /// Completa el gesto activo
    /// </summary>
    private void CompleteCurrentGesture()
    {
        if (activeGestureIndicator == null) return;

        int fingerCount = activeGestureIndicator.GetFingerCount();
        string actionText = availableGestures
            .FirstOrDefault(g => g.fingerCount == fingerCount)?.actionText ?? "Acción Desconocida";

        activeGestureIndicator.ShowSuccessEffect();
        OnGestureCompleted?.Invoke(fingerCount, actionText);
        
        ResetCurrentGesture();
    }

    /// <summary>
    /// Cancela el gesto activo
    /// </summary>
    private void CancelCurrentGesture()
    {
        if (activeGestureIndicator == null) return;

        int fingerCount = activeGestureIndicator.GetFingerCount();
        activeGestureIndicator.SetActive(false);
        activeGestureIndicator.SetDetected(false);
        
        OnGestureCanceled?.Invoke(fingerCount);
        
        ResetCurrentGesture();
    }

    /// <summary>
    /// Resetea el estado del gesto activo
    /// </summary>
    private void ResetCurrentGesture()
    {
        if (activeGestureIndicator != null)
        {
            activeGestureIndicator.Reset();
        }
        
        activeGestureIndicator = null;
        gestureTimer = 0f;
    }

    /// <summary>
    /// Añade un nuevo gesto a la configuración
    /// </summary>
    public void AddGesture(HandGestureData gestureData)
    {
        if (availableGestures.Any(g => g.fingerCount == gestureData.fingerCount))
        {
            Debug.LogWarning($"HandGestureManager: Ya existe un gesto para {gestureData.fingerCount} dedos");
            return;
        }

        availableGestures.Add(gestureData);
        
        if (autoCreateIndicators && Application.isPlaying)
        {
            CreateGestureIndicators();
            SetupGestureIndicators();
        }
    }

    /// <summary>
    /// Remueve un gesto de la configuración
    /// </summary>
    public void RemoveGesture(int fingerCount)
    {
        availableGestures.RemoveAll(g => g.fingerCount == fingerCount);
        
        if (autoCreateIndicators && Application.isPlaying)
        {
            CreateGestureIndicators();
            SetupGestureIndicators();
        }
    }

    /// <summary>
    /// Actualiza la configuración de un gesto existente
    /// </summary>
    public void UpdateGesture(int fingerCount, HandGestureData newData)
    {
        var existingGesture = availableGestures.FirstOrDefault(g => g.fingerCount == fingerCount);
        if (existingGesture != null)
        {
            int index = availableGestures.IndexOf(existingGesture);
            availableGestures[index] = newData;
            
            // Actualizar el indicador correspondiente
            var indicator = gestureIndicators.FirstOrDefault(i => i.GetFingerCount() == fingerCount);
            indicator?.SetupGesture(newData);
        }
    }

    /// <summary>
    /// Habilita o deshabilita todos los gestos
    /// </summary>
    public void SetGesturesEnabled(bool enabled)
    {
        this.enabled = enabled;
        
        if (!enabled)
        {
            CancelCurrentGesture();
        }
    }

    /// <summary>
    /// Obtiene el progreso actual del gesto (0-1)
    /// </summary>
    public float GetCurrentProgress()
    {
        if (activeGestureIndicator == null) return 0f;
        return gestureTimer / activeGestureIndicator.GetHoldTimeRequired();
    }

    /// <summary>
    /// Verifica si hay un gesto activo
    /// </summary>
    public bool IsGestureActive()
    {
        return activeGestureIndicator != null;
    }

    /// <summary>
    /// Obtiene el número de dedos del gesto activo
    /// </summary>
    public int GetActiveFingerCount()
    {
        return activeGestureIndicator?.GetFingerCount() ?? 0;
    }

    // Event handlers for gesture indicators
    private void OnIndicatorGestureCompleted(HandGestureIndicator indicator)
    {
        // This is handled by the gesture logic, but we can add additional effects here
    }

    private void OnIndicatorGestureStarted(HandGestureIndicator indicator)
    {
        // Additional start effects can be added here
    }

    private void OnIndicatorGestureCanceled(HandGestureIndicator indicator)
    {
        // Additional cancel effects can be added here
    }

    void OnDestroy()
    {
        // Clean up event subscriptions
        foreach (var indicator in gestureIndicators)
        {
            if (indicator != null)
            {
                indicator.OnGestureCompleted -= OnIndicatorGestureCompleted;
                indicator.OnGestureStarted -= OnIndicatorGestureStarted;
                indicator.OnGestureCanceled -= OnIndicatorGestureCanceled;
            }
        }
    }
} 