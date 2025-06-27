using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

/// <summary>
/// Define cómo se activa y se completa un gesto.
/// </summary>
public enum GestureActivationMode
{
    [Tooltip("El gesto debe mantenerse por un tiempo para completarse y ejecutar una acción (ej. un botón).")]
    HoldToComplete,
    [Tooltip("El gesto está activo instantáneamente y dispara eventos de inicio/parada.")]
    Instant
}

public class SimpleHandGesture : MonoBehaviour
{
    [Header("Configuración")]
    [SerializeField] private int fingerCount = 1;
    [SerializeField] private float holdTime = 2.5f;
    [Tooltip("Define si el gesto se completa con el tiempo o está activo mientras se mantiene.")]
    [SerializeField] private GestureActivationMode activationMode = GestureActivationMode.HoldToComplete;
    
    [Header("Cooldown")]
    [Tooltip("Tiempo en segundos que este gesto estará inactivo después de completarse. Por defecto es 0 (sin cooldown).")]
    [SerializeField] private float cooldownAfterCompletion = 0f;
    
    [Header("Acción a Ejecutar (para modo HoldToComplete)")]
    [Tooltip("Botón que se presionará cuando se complete el gesto")]
    [SerializeField] private Button targetButton;
    
    [Tooltip("Función personalizada que se ejecutará cuando se complete el gesto")]
    [SerializeField] private UnityEvent onGestureCompleted;

    [Header("Eventos de Estado (Nuevo)")]
    [Tooltip("Se ejecuta cuando este gesto comienza a ser detectado.")]
    [SerializeField] public UnityEvent onGestureStarted;

    [Tooltip("Se ejecuta cuando este gesto deja de ser detectado.")]
    [SerializeField] public UnityEvent onGestureStopped;
    
    [Header("Visuales")]
    [SerializeField] private Color startColor = Color.green;
    [SerializeField] private Color endColor = Color.red;
    [SerializeField] private float activeAlpha = 1.0f;
    [SerializeField] private float inactiveAlpha = 0.5f;
    
    // Referencias internas
    private Image handImage;
    private Image progressBar;
    private CanvasGroup canvasGroup;
    
    // Estado
    private float currentProgress = 0f;
    private bool isActive = false;
    private bool isOnCooldown = false;
    private float cooldownTimer = 0f;
    private Vector3 originalScale;
    
    void Awake()
    {
        SetupGesture();
    }

    void Start()
    {
        originalScale = transform.localScale;
        LoadHandSprite();
        SetActive(false); // Asegurar estado inicial inactivo
    }

    void SetupGesture()
    {
        // Obtener la imagen principal (debe existir)
        handImage = GetComponent<Image>();
        if (handImage == null)
        {
            Debug.LogError("SimpleHandGesture debe estar en un GameObject con Image");
            return;
        }

        // Crear CanvasGroup
        canvasGroup = gameObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        // Crear la barra de progreso circular
        CreateProgressBar();
    }

    void CreateProgressBar()
    {
        GameObject progressObj = new GameObject("ProgressBar");
        progressObj.transform.SetParent(transform, false);
        
        // Configurar RectTransform
        RectTransform progressRect = progressObj.AddComponent<RectTransform>();
        progressRect.anchorMin = Vector2.zero;
        progressRect.anchorMax = Vector2.one;
        progressRect.offsetMin = Vector2.one * -20f; // 20px más grande que la imagen
        progressRect.offsetMax = Vector2.one * 20f;
        progressRect.SetAsFirstSibling(); // Que esté detrás de la imagen
        
        // Configurar Image
        progressBar = progressObj.AddComponent<Image>();
        
        // Intentar cargar el sprite circular (sin extensión)
        Sprite circularSprite = Resources.Load<Sprite>("UI/Hands/circular_sprite.png");
        if (circularSprite == null)
        {
            Debug.LogWarning("No se encontró circular_sprite.png - creando sprite automático");
            progressBar.sprite = CreateCircularSprite();
        }
        else
        {
            Debug.Log("Sprite circular cargado correctamente");
            progressBar.sprite = circularSprite;
        }
        
        // Forzar que sea visible
        progressBar.color = Color.white;
        
        progressBar.type = Image.Type.Filled;
        progressBar.fillMethod = Image.FillMethod.Radial360;
        progressBar.fillOrigin = 2; // Top
        progressBar.fillAmount = 0f; // Empezar vacío
        progressBar.raycastTarget = false;
        
        // Debug.Log($"Barra de progreso creada para {fingerCount} dedos");
    }

    void LoadHandSprite()
    {
        if (handImage != null)
        {
            // Intentar cargar sin extensión primero (método estándar de Unity)
            Sprite handSprite = Resources.Load<Sprite>($"UI/Hands/{fingerCount}");
            
            if (handSprite == null)
            {
                // Si no funciona, intentar con extensión .png
                handSprite = Resources.Load<Sprite>($"UI/Hands/{fingerCount}.png");
            }
            
            if (handSprite != null)
            {
                handImage.sprite = handSprite;
                // Debug.Log($"✅ Cargado sprite para {fingerCount} dedos");
            }
            else
            {
                Debug.LogWarning($"❌ No se encontró sprite para {fingerCount} dedos. Verifica:");
                Debug.LogWarning($"   1. Que {fingerCount}.png esté en Assets/Resources/UI/Hands/");
                Debug.LogWarning($"   2. Que la imagen esté configurada como Sprite en Unity");
                Debug.LogWarning($"   3. Que Unity haya importado correctamente la imagen");
                
                // Crear sprite temporal para debug
                CreateTemporarySprite();
            }
        }
    }
    
    void CreateTemporarySprite()
    {
        // Crear un sprite temporal de color para debug
        Texture2D tempTexture = new Texture2D(100, 100);
        Color[] colors = new Color[100 * 100];
        
        // Cada dedo un color diferente para debug
        Color debugColor = Color.white;
        switch (fingerCount)
        {
            case 1: debugColor = Color.red; break;
            case 2: debugColor = Color.green; break;
            case 3: debugColor = Color.blue; break;
            case 4: debugColor = Color.yellow; break;
            case 5: debugColor = Color.magenta; break;
            default: debugColor = Color.white; break;
        }
        
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = debugColor;
        }
        
        tempTexture.SetPixels(colors);
        tempTexture.Apply();
        
        Sprite tempSprite = Sprite.Create(tempTexture, new Rect(0, 0, 100, 100), new Vector2(0.5f, 0.5f));
        handImage.sprite = tempSprite;
        
        // Debug.Log($"🔧 Sprite temporal creado para {fingerCount} dedos (color: {debugColor.ToString()})");
    }

    Sprite CreateCircularSprite()
    {
        // Crear un sprite circular simple programáticamente
        Texture2D texture = new Texture2D(200, 200);
        Color[] colors = new Color[200 * 200];
        Vector2 center = new Vector2(100, 100);
        
        for (int y = 0; y < 200; y++)
        {
            for (int x = 0; x < 200; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                // Hacer un anillo más grueso y visible
                if (distance <= 95 && distance >= 80)
                {
                    colors[y * 200 + x] = Color.white;
                }
                else
                {
                    colors[y * 200 + x] = Color.clear;
                }
            }
        }
        
        texture.SetPixels(colors);
        texture.Apply();
        
        // Debug.Log("Sprite circular creado programáticamente");
        return Sprite.Create(texture, new Rect(0, 0, 200, 200), new Vector2(0.5f, 0.5f));
    }

    public void UpdateProgress(float progress)
    {
        currentProgress = Mathf.Clamp01(progress);
        
        if (progressBar != null)
        {
            progressBar.fillAmount = currentProgress;
            progressBar.color = Color.Lerp(startColor, endColor, currentProgress);
        }
        
        if (currentProgress >= 1f && isActive)
        {
            // El gesto solo se "completa" y ejecuta su acción si está en el modo correcto.
            if (activationMode == GestureActivationMode.HoldToComplete)
            {
                CompleteGesture();
            }
        }
    }

    public void SetActive(bool active)
    {
        // No se puede activar si está en cooldown.
        if (isOnCooldown)
        {
            isActive = false;
            return;
        }

        isActive = active;
        
        if (canvasGroup != null)
        {
            canvasGroup.alpha = active ? activeAlpha : inactiveAlpha;
        }
        
        // NO MÁS CORRUTINAS - Efecto visual directo en Update
        if (!active)
        {
            transform.localScale = originalScale;
        }
    }

    void Update()
    {
        // Prioridad 1: Si el gesto está en cooldown, gestionarlo y no hacer nada más.
        if (isOnCooldown)
        {
            cooldownTimer -= Time.deltaTime;
            if (progressBar != null && cooldownAfterCompletion > 0)
            {
                // Mostrar una barra de progreso que se vacía, usando un color distintivo.
                progressBar.fillAmount = cooldownTimer / cooldownAfterCompletion;
                progressBar.color = Color.Lerp(endColor, Color.blue, currentProgress);
            }

            if (cooldownTimer <= 0)
            {
                isOnCooldown = false;
                UpdateProgress(0); // Resetear visualmente la barra
            }
            return; // Importante: Salir para no procesar otros efectos.
        }

        // Efecto de pulso síncrono cuando está activo
        if (isActive && gameObject.activeInHierarchy)
        {
            float pulse = 1f + 0.1f * Mathf.Sin(Time.time * 3f);
            transform.localScale = originalScale * pulse;
        }
        else if (!isActive)
        {
            transform.localScale = originalScale;
        }
    }

    void CompleteGesture()
    {
        isActive = false;
        
        // ⚠️ DETENER TODAS LAS CORRUTINAS INMEDIATAMENTE
        StopAllCoroutines();
        
        // EJECUTAR ACCIONES INMEDIATAMENTE
        ExecuteGestureActions();
        
        // Iniciar el cooldown si está configurado
        if (cooldownAfterCompletion > 0)
        {
            isOnCooldown = true;
            cooldownTimer = cooldownAfterCompletion;
        }
        
        // NO USAR CORRUTINAS - Solo efecto visual síncrono
        DoImmediateEffect();
    }
    
    void ExecuteGestureActions()
    {
        // Debug.Log($"🎯 Ejecutando acción para {fingerCount} dedos");
        
        // Ejecutar el botón asignado
        if (targetButton != null)
        {
            // Debug.Log($"Ejecutando botón asignado para {fingerCount} dedos");
            targetButton.onClick.Invoke();
        }
        
        // Ejecutar UnityEvent personalizado
        if (onGestureCompleted != null)
        {
            // Debug.Log($"Ejecutando evento personalizado para {fingerCount} dedos");
            onGestureCompleted.Invoke();
        }
        
        UpdateProgress(0f);
        SetActive(false);
    }
    
    void DoImmediateEffect()
    {
        // Efecto visual SÍNCRONO (sin corrutinas)
        if (gameObject.activeInHierarchy && handImage != null)
        {
            // Debug.Log($"💫 Efecto inmediato para {fingerCount} dedos");
            // Flash rápido sin corrutina
            Color original = handImage.color;
            handImage.color = Color.white;
            // Restaurar inmediatamente (sin wait)
            handImage.color = original;
        }
        else
        {
            // Debug.Log($"⚠️ Objeto {fingerCount} dedos inactivo - Sin efectos visuales");
        }
        
        // Reset inmediato
        UpdateProgress(0f);
        SetActive(false);
    }

    // Métodos públicos para integración
    public int GetFingerCount() => fingerCount;
    public float GetHoldTime() => holdTime;
    public GestureActivationMode GetActivationMode() => activationMode;
    public bool IsOnCooldown() => isOnCooldown;
    public void Reset()
    {
        UpdateProgress(0f);
        SetActive(false);
    }

    void OnValidate()
    {
        if (Application.isPlaying && handImage != null)
        {
            LoadHandSprite();
        }
    }
} 