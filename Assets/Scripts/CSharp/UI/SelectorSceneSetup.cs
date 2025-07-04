using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// Script que configura automáticamente toda la escena del Selector
/// Debe ser colocado en un GameObject vacío en la escena
/// </summary>
public class SelectorSceneSetup : MonoBehaviour
{
    [Header("Configuración Automática")]
    [SerializeField] private bool autoSetupOnStart = true;
    
    [Header("Referencias de la Escena")]
    [SerializeField] private Canvas mainCanvas;
    [SerializeField] private SelectorController selectorController;
    [SerializeField] private SelectorGameUI selectorGameUI;
    
    void Start()
    {
        if (autoSetupOnStart)
        {
            SetupSelectorScene();
        }
    }
    
    [ContextMenu("Configurar Escena del Selector")]
    public void SetupSelectorScene()
    {
        Debug.Log("🚀 Configurando escena del Selector...");
        
        // 1. Encontrar o crear Canvas
        if (mainCanvas == null)
        {
            mainCanvas = FindObjectOfType<Canvas>();
            if (mainCanvas == null)
            {
                CreateMainCanvas();
            }
        }
        
        // 2. Configurar SelectorController
        SetupSelectorController();
        
        // 3. Configurar SelectorGameUI
        SetupSelectorGameUI();
        
        // 4. Configurar sistemas adicionales
        SetupAdditionalSystems();
        
        Debug.Log("✅ Escena del Selector configurada correctamente!");
    }
    
    private void CreateMainCanvas()
    {
        GameObject canvasObj = new GameObject("Main Canvas");
        mainCanvas = canvasObj.AddComponent<Canvas>();
        mainCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        mainCanvas.sortingOrder = 0;
        
        // Añadir CanvasScaler para responsive design
        CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        
        // Añadir GraphicRaycaster
        canvasObj.AddComponent<GraphicRaycaster>();
        
        // Crear EventSystem si no existe
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystemObj = new GameObject("EventSystem");
            eventSystemObj.AddComponent<EventSystem>();
            eventSystemObj.AddComponent<StandaloneInputModule>();
        }
        
        // Añadir el helper automático
        mainCanvas.gameObject.AddComponent<SelectorGameUIHelper>();
        
        Debug.Log("📱 Canvas principal creado con configuración responsive");
    }
    
    private void SetupSelectorController()
    {
        // Buscar SelectorController existente
        if (selectorController == null)
        {
            selectorController = FindObjectOfType<SelectorController>();
        }
        
        // Si no existe, crear uno nuevo
        if (selectorController == null)
        {
            GameObject controllerObj = new GameObject("SelectorController");
            selectorController = controllerObj.AddComponent<SelectorController>();
            Debug.Log("🎮 SelectorController creado");
        }
        
        // Asegurar que tenga las referencias necesarias
        // El SelectorController ya se auto-detecta SelectorGameUI en su código
    }
    
    private void SetupSelectorGameUI()
    {
        // Buscar SelectorGameUI existente
        if (selectorGameUI == null)
        {
            selectorGameUI = FindObjectOfType<SelectorGameUI>();
        }
        
        // Si no existe, añadirlo al Canvas
        if (selectorGameUI == null && mainCanvas != null)
        {
            selectorGameUI = mainCanvas.gameObject.AddComponent<SelectorGameUI>();
            Debug.Log("🖥️ SelectorGameUI añadido al Canvas");
        }
        
        // Configurar auto-find de elementos UI
        if (selectorGameUI != null)
        {
            // El SelectorGameUI ya tiene auto-find habilitado por defecto
            Debug.Log("🔗 SelectorGameUI configurado para auto-detectar elementos UI");
        }
    }
    
    private void SetupAdditionalSystems()
    {
        // Asegurar que PhotonManager esté disponible
        if (PhotonManager.Instance == null)
        {
            Debug.LogWarning("⚠️ PhotonManager no encontrado. Asegúrate de que el prefab _Managers esté en la escena o en Resources.");
        }
        else
        {
            Debug.Log("📡 PhotonManager encontrado y disponible");
        }
        
        // Añadir UnityMainThreadDispatcher si no existe
        if (FindObjectOfType<UnityMainThreadDispatcher>() == null)
        {
            GameObject dispatcherObj = new GameObject("UnityMainThreadDispatcher");
            dispatcherObj.AddComponent<UnityMainThreadDispatcher>();
            Debug.Log("🔄 UnityMainThreadDispatcher añadido");
        }
        
        // Configurar la cámara para UI
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            GameObject cameraObj = new GameObject("Main Camera");
            mainCamera = cameraObj.AddComponent<Camera>();
            mainCamera.tag = "MainCamera";
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);
            Debug.Log("📷 Cámara principal creada");
        }
    }
    
    [ContextMenu("Verificar Configuración")]
    public void VerifySetup()
    {
        Debug.Log("🔍 Verificando configuración de la escena...");
        
        bool allGood = true;
        
        // Verificar Canvas
        if (FindObjectOfType<Canvas>() == null)
        {
            Debug.LogError("❌ No se encontró Canvas en la escena");
            allGood = false;
        }
        else Debug.Log("✅ Canvas encontrado");
        
        // Verificar SelectorController
        if (FindObjectOfType<SelectorController>() == null)
        {
            Debug.LogError("❌ No se encontró SelectorController en la escena");
            allGood = false;
        }
        else Debug.Log("✅ SelectorController encontrado");
        
        // Verificar SelectorGameUI
        if (FindObjectOfType<SelectorGameUI>() == null)
        {
            Debug.LogError("❌ No se encontró SelectorGameUI en la escena");
            allGood = false;
        }
        else Debug.Log("✅ SelectorGameUI encontrado");
        
        // Verificar PhotonManager
        if (PhotonManager.Instance == null)
        {
            Debug.LogWarning("⚠️ PhotonManager no disponible (puede estar en otra escena)");
        }
        else Debug.Log("✅ PhotonManager disponible");
        
        if (allGood)
        {
            Debug.Log("🎉 ¡Configuración de la escena del Selector COMPLETA!");
        }
        else
        {
            Debug.Log("🔧 Hay elementos que faltan. Ejecuta 'Configurar Escena del Selector'");
        }
    }
} 