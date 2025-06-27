using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class HandGestureData
{
    [Tooltip("Número de dedos requerido para activar este gesto")]
    public int fingerCount;
    
    [Tooltip("Imagen de la mano mostrando la cantidad de dedos")]
    public Sprite handSprite;
    
    [Tooltip("Texto descriptivo de la acción")]
    public string actionText = "Acción";
    
    [Tooltip("Tiempo en segundos que se debe mantener el gesto")]
    public float holdTimeRequired = 2.5f;

    [Tooltip("Tiempo en segundos que este gesto estará inactivo después de completarse. 0 para sin cooldown.")]
    public float cooldownAfterCompletion = 0f;
}

public class HandGestureIndicator : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Image handImage;
    [SerializeField] private Image circularProgressBar;
    [SerializeField] private Text actionText;
    [SerializeField] private CanvasGroup canvasGroup;
    
    [Header("Visual Settings")]
    [SerializeField] private Color startColor = Color.green;
    [SerializeField] private Color endColor = Color.red;
    [SerializeField] private float inactiveAlpha = 0.3f;
    [SerializeField] private float activeAlpha = 1f;
    [SerializeField] private float transitionSpeed = 5f;
    
    [Header("Animation Settings")]
    [SerializeField] private bool enablePulseAnimation = true;
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float pulseIntensity = 0.1f;
    
    // Private fields
    private HandGestureData gestureData;
    private float currentProgress = 0f;
    private bool isActive = false;
    private bool isDetected = false;
    private bool isOnCooldown = false;
    private float cooldownTimer = 0f;
    private float targetAlpha;
    private Vector3 originalScale;
    
    // Events
    public System.Action<HandGestureIndicator> OnGestureCompleted;
    public System.Action<HandGestureIndicator> OnGestureStarted;
    public System.Action<HandGestureIndicator> OnGestureCanceled;

    void Awake()
    {
        originalScale = transform.localScale;
        targetAlpha = inactiveAlpha;
        
        if (canvasGroup == null)
            canvasGroup = GetComponent<CanvasGroup>();
        
        if (canvasGroup == null)
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    void Start()
    {
        // Initialize circular progress bar
        if (circularProgressBar != null)
        {
            circularProgressBar.type = Image.Type.Filled;
            circularProgressBar.fillMethod = Image.FillMethod.Radial360;
            circularProgressBar.fillOrigin = 2; // Top
            circularProgressBar.fillAmount = 0f;
        }
        
        SetProgress(0f);
        SetActive(false);
    }

    void Update()
    {
        // Si está en cooldown, gestionarlo y no hacer nada más.
        if (isOnCooldown)
        {
            cooldownTimer -= Time.deltaTime;
            if (gestureData != null && gestureData.cooldownAfterCompletion > 0)
            {
                if (circularProgressBar != null)
                {
                    circularProgressBar.fillAmount = 1 - (cooldownTimer / gestureData.cooldownAfterCompletion);
                    circularProgressBar.color = Color.blue; // Color de cooldown
                }
            }

            if (cooldownTimer <= 0)
            {
                isOnCooldown = false;
                SetProgress(0f); // Resetear visuales
            }
            return;
        }

        // Smooth alpha transition
        if (canvasGroup != null)
        {
            canvasGroup.alpha = Mathf.Lerp(canvasGroup.alpha, targetAlpha, 
                transitionSpeed * Time.deltaTime);
        }
        
        // Pulse animation when detected but not active
        if (isDetected && !isActive && enablePulseAnimation)
        {
            float pulse = 1f + pulseIntensity * Mathf.Sin(Time.time * pulseSpeed);
            transform.localScale = originalScale * pulse;
        }
        else if (!isDetected)
        {
            transform.localScale = Vector3.Lerp(transform.localScale, originalScale, 
                transitionSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Configura este indicador con los datos del gesto
    /// </summary>
    public void SetupGesture(HandGestureData data)
    {
        gestureData = data;
        
        if (handImage != null && data.handSprite != null)
        {
            handImage.sprite = data.handSprite;
        }
        
        if (actionText != null)
        {
            actionText.text = data.actionText;
        }
    }

    /// <summary>
    /// Actualiza el progreso del gesto (0 a 1)
    /// </summary>
    public void SetProgress(float progress)
    {
        currentProgress = Mathf.Clamp01(progress);
        
        if (circularProgressBar != null)
        {
            circularProgressBar.fillAmount = currentProgress;
            circularProgressBar.color = Color.Lerp(startColor, endColor, currentProgress);
        }
        
        // Trigger completion event
        if (currentProgress >= 1f && isActive && !isDetected)
        {
            CompleteGesture();
        }
    }

    /// <summary>
    /// Activa/desactiva la detección del gesto
    /// </summary>
    public void SetActive(bool active)
    {
        // No se puede activar si está en cooldown.
        if (isOnCooldown)
        {
            isActive = false;
            return;
        }

        isActive = active;
        targetAlpha = active ? activeAlpha : inactiveAlpha;
        
        if (active && !isDetected)
        {
            OnGestureStarted?.Invoke(this);
        }
        else if (!active && isDetected)
        {
            CancelGesture();
        }
    }

    /// <summary>
    /// Marca que el gesto ha sido detectado (pero no completado)
    /// </summary>
    public void SetDetected(bool detected)
    {
        isDetected = detected;
        
        if (!detected && isActive)
        {
            CancelGesture();
        }
    }

    /// <summary>
    /// Completa el gesto y dispara el evento correspondiente
    /// </summary>
    private void CompleteGesture()
    {
        isDetected = false;
        isActive = false;

        if (gestureData != null && gestureData.cooldownAfterCompletion > 0)
        {
            isOnCooldown = true;
            cooldownTimer = gestureData.cooldownAfterCompletion;
        }

        OnGestureCompleted?.Invoke(this);
    }

    /// <summary>
    /// Cancela el gesto actual
    /// </summary>
    private void CancelGesture()
    {
        SetProgress(0f);
        isDetected = false;
        OnGestureCanceled?.Invoke(this);
    }

    /// <summary>
    /// Resetea el indicador a su estado inicial
    /// </summary>
    public void Reset()
    {
        SetProgress(0f);
        SetActive(false);
        SetDetected(false);
    }

    public bool IsOnCooldown() => isOnCooldown;

    /// <summary>
    /// Obtiene el número de dedos asociado a este gesto
    /// </summary>
    public int GetFingerCount()
    {
        return gestureData?.fingerCount ?? 0;
    }

    /// <summary>
    /// Obtiene el tiempo requerido para completar el gesto
    /// </summary>
    public float GetHoldTimeRequired()
    {
        return gestureData?.holdTimeRequired ?? 2.5f;
    }

    /// <summary>
    /// Activa un efecto visual de éxito
    /// </summary>
    public void ShowSuccessEffect()
    {
        // Efecto SÍNCRONO sin corrutinas
        DoImmediateSuccessEffect();
    }

    private void DoImmediateSuccessEffect()
    {
        if (gameObject.activeInHierarchy)
        {
            Debug.Log($"💫 Efecto de éxito inmediato");
            // Efecto rápido sin corrutina
            Vector3 targetScale = originalScale * 1.2f;
            transform.localScale = targetScale;
            // Restaurar inmediatamente
            transform.localScale = originalScale;
        }
        else
        {
            Debug.Log($"⚠️ HandGestureIndicator inactivo - Sin efectos visuales");
        }
    }
} 