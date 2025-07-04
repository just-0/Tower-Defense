using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Componente que muestra una guía visual (rectángulo rojo) para indicar al usuario
/// dónde debe colocar las hojas de colores y el código ArUco durante la fase de planificación.
/// </summary>
public class ArUcoPlacementGuide : MonoBehaviour
{
    [Header("Configuración Visual")]
    [SerializeField] private Color guideColor = new Color(1f, 0f, 0f, 0.3f); // Rojo semi-transparente
    [SerializeField] private Color borderColor = new Color(1f, 0f, 0f, 0.8f); // Rojo para el borde
    [SerializeField] private float borderWidth = 5f;
    [SerializeField] private bool animatePulse = true;
    [SerializeField] private float pulseSpeed = 2f;
    [SerializeField] private float pulseIntensity = 0.3f;
    
    [Header("Área de Colocación")]
    [SerializeField] private Vector2 guideSize = new Vector2(400, 300); // Tamaño del rectángulo guía
    [SerializeField] private Vector2 guidePosition = new Vector2(0, 0); // Posición relativa al centro
    
    [Header("Textos Informativos")]
    [SerializeField] private bool showInstructions = true;
    [SerializeField] private string planningPhaseText = "📄 COLOCA AQUÍ LAS HOJAS DE COLOR Y EL CÓDIGO ARUCO";
    [SerializeField] private string combatPhaseText = "🎯 ZONA DE JUEGO ACTIVA";
    [SerializeField] private Font instructionFont;
    [SerializeField] private int fontSize = 20;
    
    // Referencias internas
    private Canvas parentCanvas;
    private GameObject guidePanel;
    private Image guideBackground;
    private Image guideBorder;
    private Text instructionText;
    private CanvasGroup canvasGroup;
    
    // Control de estado
    private bool isVisible = false;
    private bool isInPlanningPhase = true;
    private float originalAlpha;
    private RectTransform guideRect;

    public static ArUcoPlacementGuide Instance { get; private set; }

    void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        SetupGuideUI();
        originalAlpha = guideColor.a;
        
        // Comenzar oculto
        SetVisible(false);
    }

    void Update()
    {
        if (isVisible && animatePulse)
        {
            UpdatePulseAnimation();
        }
    }

    private void SetupGuideUI()
    {
        // Buscar o crear Canvas
        parentCanvas = FindObjectOfType<Canvas>();
        if (parentCanvas == null)
        {
            GameObject canvasObj = new GameObject("ArUco Guide Canvas");
            parentCanvas = canvasObj.AddComponent<Canvas>();
            parentCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            parentCanvas.sortingOrder = 100; // Alto para estar encima de otros elementos
            canvasObj.AddComponent<GraphicRaycaster>();
        }

        // Crear panel contenedor principal
        guidePanel = new GameObject("ArUco Placement Guide");
        guidePanel.transform.SetParent(parentCanvas.transform, false);

        // Configurar RectTransform
        guideRect = guidePanel.AddComponent<RectTransform>();
        guideRect.anchorMin = new Vector2(0.5f, 0.5f);
        guideRect.anchorMax = new Vector2(0.5f, 0.5f);
        guideRect.pivot = new Vector2(0.5f, 0.5f);
        guideRect.anchoredPosition = guidePosition;
        guideRect.sizeDelta = guideSize;

        // Añadir CanvasGroup para controlar la visibilidad
        canvasGroup = guidePanel.AddComponent<CanvasGroup>();

        // Crear fondo del rectángulo
        CreateGuideBackground();
        
        // Crear borde del rectángulo
        CreateGuideBorder();
        
        // Crear texto de instrucciones
        CreateInstructionText();

        Debug.Log("🎯 ArUco Placement Guide configurado correctamente");
    }

    private void CreateGuideBackground()
    {
        GameObject backgroundObj = new GameObject("Guide Background");
        backgroundObj.transform.SetParent(guidePanel.transform, false);

        RectTransform bgRect = backgroundObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.offsetMin = Vector2.zero;
        bgRect.offsetMax = Vector2.zero;

        guideBackground = backgroundObj.AddComponent<Image>();
        guideBackground.color = guideColor;
        guideBackground.raycastTarget = false; // No bloquear interacciones
    }

    private void CreateGuideBorder()
    {
        // Crear 4 líneas para formar el borde
        CreateBorderLine("Border Top", new Vector2(0, 0.5f), new Vector2(1, 0.5f), new Vector2(0, borderWidth));
        CreateBorderLine("Border Bottom", new Vector2(0, -0.5f), new Vector2(1, -0.5f), new Vector2(0, borderWidth));
        CreateBorderLine("Border Left", new Vector2(-0.5f, 0), new Vector2(-0.5f, 1), new Vector2(borderWidth, 0));
        CreateBorderLine("Border Right", new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(borderWidth, 0));
    }

    private void CreateBorderLine(string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 sizeDelta)
    {
        GameObject borderLine = new GameObject(name);
        borderLine.transform.SetParent(guidePanel.transform, false);

        RectTransform lineRect = borderLine.AddComponent<RectTransform>();
        lineRect.anchorMin = anchorMin;
        lineRect.anchorMax = anchorMax;
        lineRect.sizeDelta = sizeDelta;
        lineRect.anchoredPosition = Vector2.zero;

        Image lineImage = borderLine.AddComponent<Image>();
        lineImage.color = borderColor;
        lineImage.raycastTarget = false;
    }

    private void CreateInstructionText()
    {
        if (!showInstructions) return;

        GameObject textObj = new GameObject("Instruction Text");
        textObj.transform.SetParent(guidePanel.transform, false);

        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0f);
        textRect.anchorMax = new Vector2(0.5f, 0f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = new Vector2(0, -20); // Justo debajo del rectángulo
        textRect.sizeDelta = new Vector2(guideSize.x + 100, 60);

        instructionText = textObj.AddComponent<Text>();
        instructionText.text = planningPhaseText;
        instructionText.font = instructionFont ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        instructionText.fontSize = fontSize;
        instructionText.color = borderColor;
        instructionText.alignment = TextAnchor.MiddleCenter;
        instructionText.raycastTarget = false;

        // Añadir outline para mejor legibilidad
        Outline textOutline = textObj.AddComponent<Outline>();
        textOutline.effectColor = Color.black;
        textOutline.effectDistance = new Vector2(2, 2);
    }

    private void UpdatePulseAnimation()
    {
        float pulse = Mathf.Sin(Time.time * pulseSpeed) * pulseIntensity;
        float currentAlpha = Mathf.Clamp01(originalAlpha + pulse);
        
        if (guideBackground != null)
        {
            Color newColor = guideColor;
            newColor.a = currentAlpha;
            guideBackground.color = newColor;
        }
    }

    /// <summary>
    /// Muestra u oculta la guía de colocación del ArUco
    /// </summary>
    /// <param name="visible">Si debe ser visible</param>
    public void SetVisible(bool visible)
    {
        isVisible = visible;
        
        if (canvasGroup != null)
        {
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = false; // Nunca debe ser interactuable
            canvasGroup.blocksRaycasts = false; // No debe bloquear interacciones
        }

        if (guidePanel != null)
        {
            guidePanel.SetActive(visible);
        }

        Debug.Log($"🎯 ArUco Placement Guide: {(visible ? "VISIBLE" : "OCULTO")}");
    }

    /// <summary>
    /// Cambia entre fase de planificación y combate
    /// </summary>
    /// <param name="isPlanningPhase">True para fase de planificación, false para combate</param>
    public void SetPhase(bool isPlanningPhase)
    {
        isInPlanningPhase = isPlanningPhase;
        
        if (instructionText != null)
        {
            instructionText.text = isPlanningPhase ? planningPhaseText : combatPhaseText;
        }

        // Cambiar color según la fase
        Color phaseColor = isPlanningPhase ? 
            new Color(1f, 0f, 0f, originalAlpha) :  // Rojo para planificación
            new Color(0f, 1f, 0f, originalAlpha);   // Verde para combate

        if (guideBackground != null)
        {
            guideColor = phaseColor;
            guideBackground.color = phaseColor;
        }

        Debug.Log($"🎯 ArUco Guide: Fase cambiada a {(isPlanningPhase ? "PLANIFICACIÓN" : "COMBATE")}");
    }

    /// <summary>
    /// Muestra la guía durante la fase de planificación
    /// </summary>
    public void ShowPlanningGuide()
    {
        SetPhase(true);
        SetVisible(true);
    }

    /// <summary>
    /// Muestra la guía durante la fase de combate
    /// </summary>
    public void ShowCombatGuide()
    {
        SetPhase(false);
        SetVisible(true);
    }

    /// <summary>
    /// Oculta completamente la guía
    /// </summary>
    public void HideGuide()
    {
        SetVisible(false);
    }

    /// <summary>
    /// Actualiza el tamaño y posición de la guía
    /// </summary>
    /// <param name="newSize">Nuevo tamaño del rectángulo</param>
    /// <param name="newPosition">Nueva posición del rectángulo</param>
    public void UpdateGuideArea(Vector2 newSize, Vector2 newPosition)
    {
        guideSize = newSize;
        guidePosition = newPosition;
        
        if (guideRect != null)
        {
            guideRect.sizeDelta = guideSize;
            guideRect.anchoredPosition = guidePosition;
        }

        if (instructionText != null)
        {
            RectTransform textRect = instructionText.GetComponent<RectTransform>();
            textRect.sizeDelta = new Vector2(guideSize.x + 100, 60);
        }
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }
} 