using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
#endif

[System.Serializable]
public class HandGestureIndicatorSettings
{
    [Header("Layout Settings")]
    public Vector2 indicatorSize = new Vector2(200, 200);
    public Vector2 handImageSize = new Vector2(120, 120);
    public Vector2 circularBarSize = new Vector2(180, 180);
    public Vector2 textSize = new Vector2(200, 50);
    
    [Header("Colors")]
    public Color progressStartColor = Color.green;
    public Color progressEndColor = Color.red;
    public Color textColor = Color.white;
    public Color backgroundTint = new Color(0, 0, 0, 0.5f);
    
    [Header("Fonts and Sprites")]
    public Font textFont;
    public Sprite circularBarSprite; // Un sprite circular blanco para la barra
    public Sprite backgroundSprite; // Sprite opcional para el fondo
}

public class HandGestureIndicatorSetup : MonoBehaviour
{
    [Header("Configuration")]
    [SerializeField] private HandGestureIndicatorSettings settings = new HandGestureIndicatorSettings();
    
    [Header("Preview")]
    [SerializeField] private bool previewInEditor = true;
    
#if UNITY_EDITOR
    [Header("Editor Tools")]
    [SerializeField] private bool autoSetupHierarchy = false;
    
    [ContextMenu("Create Gesture Indicator Prefab")]
    public void CreateGestureIndicatorPrefab()
    {
        GameObject prefab = CreateGestureIndicatorGameObject("HandGestureIndicator");
        
        // Crear el prefab en la carpeta Resources
        string prefabPath = "Assets/Resources/UI/HandGestureIndicator.prefab";
        
        // Asegurar que el directorio existe
        string directory = System.IO.Path.GetDirectoryName(prefabPath);
        if (!System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }
        
        PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        Debug.Log($"Prefab creado en: {prefabPath}");
        
        // Opcional: eliminar el objeto temporal de la escena
        if (!Application.isPlaying)
        {
            DestroyImmediate(prefab);
        }
    }
    
    [ContextMenu("Setup Current GameObject")]
    public void SetupCurrentGameObject()
    {
        SetupGestureIndicatorComponents(gameObject);
        Debug.Log("Componentes configurados en el GameObject actual");
    }
#endif

    /// <summary>
    /// Crea un GameObject completo con el HandGestureIndicator configurado
    /// </summary>
    public GameObject CreateGestureIndicatorGameObject(string name = "HandGestureIndicator")
    {
        GameObject indicatorObject = new GameObject(name);
        SetupGestureIndicatorComponents(indicatorObject);
        return indicatorObject;
    }

    /// <summary>
    /// Configura todos los componentes necesarios en un GameObject existente
    /// </summary>
    public void SetupGestureIndicatorComponents(GameObject targetObject)
    {
        // Asegurar que tiene RectTransform
        RectTransform mainRect = targetObject.GetComponent<RectTransform>();
        if (mainRect == null)
        {
            mainRect = targetObject.AddComponent<RectTransform>();
        }
        mainRect.sizeDelta = settings.indicatorSize;

        // Agregar CanvasGroup si no existe
        CanvasGroup canvasGroup = targetObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = targetObject.AddComponent<CanvasGroup>();
        }

        // Crear fondo opcional
        if (settings.backgroundSprite != null)
        {
            GameObject background = CreateChildObject(targetObject, "Background");
            SetupImageComponent(background, settings.backgroundSprite, settings.backgroundTint);
            SetRectTransform(background, Vector2.zero, settings.indicatorSize);
        }

        // Crear la barra circular de progreso
        GameObject circularBar = CreateChildObject(targetObject, "CircularProgressBar");
        Image barImage = SetupImageComponent(circularBar, settings.circularBarSprite, settings.progressStartColor);
        SetRectTransform(circularBar, Vector2.zero, settings.circularBarSize);
        
        // Configurar la imagen como barra de progreso radial
        if (barImage != null)
        {
            barImage.type = Image.Type.Filled;
            barImage.fillMethod = Image.FillMethod.Radial360;
            barImage.fillOrigin = 2; // Top
            barImage.fillAmount = 0f;
        }

        // Crear la imagen de la mano
        GameObject handImage = CreateChildObject(targetObject, "HandImage");
        SetupImageComponent(handImage, null, Color.white);
        SetRectTransform(handImage, Vector2.zero, settings.handImageSize);

        // Crear el texto de acción
        GameObject actionText = CreateChildObject(targetObject, "ActionText");
        SetupTextComponent(actionText, "Acción", settings.textColor);
        SetRectTransform(actionText, new Vector2(0, -settings.indicatorSize.y * 0.6f), settings.textSize);

        // Agregar el componente HandGestureIndicator
        HandGestureIndicator indicator = targetObject.GetComponent<HandGestureIndicator>();
        if (indicator == null)
        {
            indicator = targetObject.AddComponent<HandGestureIndicator>();
        }

        // Configurar las referencias del componente usando reflexión para acceso a campos privados
        SetPrivateField(indicator, "handImage", handImage.GetComponent<Image>());
        SetPrivateField(indicator, "circularProgressBar", barImage);
        SetPrivateField(indicator, "actionText", actionText.GetComponent<Text>());
        SetPrivateField(indicator, "canvasGroup", canvasGroup);
        SetPrivateField(indicator, "startColor", settings.progressStartColor);
        SetPrivateField(indicator, "endColor", settings.progressEndColor);

        Debug.Log($"HandGestureIndicator configurado en {targetObject.name}");
    }

    /// <summary>
    /// Crea un objeto hijo con RectTransform
    /// </summary>
    private GameObject CreateChildObject(GameObject parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent.transform, false);
        
        RectTransform childRect = child.AddComponent<RectTransform>();
        childRect.anchorMin = Vector2.one * 0.5f;
        childRect.anchorMax = Vector2.one * 0.5f;
        childRect.pivot = Vector2.one * 0.5f;
        
        return child;
    }

    /// <summary>
    /// Configura un componente Image en un GameObject
    /// </summary>
    private Image SetupImageComponent(GameObject target, Sprite sprite, Color color)
    {
        Image imageComponent = target.GetComponent<Image>();
        if (imageComponent == null)
        {
            imageComponent = target.AddComponent<Image>();
        }

        imageComponent.sprite = sprite;
        imageComponent.color = color;
        imageComponent.raycastTarget = false; // Los indicadores no necesitan recibir eventos

        return imageComponent;
    }

    /// <summary>
    /// Configura un componente Text en un GameObject
    /// </summary>
    private Text SetupTextComponent(GameObject target, string text, Color color)
    {
        Text textComponent = target.GetComponent<Text>();
        if (textComponent == null)
        {
            textComponent = target.AddComponent<Text>();
        }

        textComponent.text = text;
        textComponent.color = color;
        textComponent.alignment = TextAnchor.MiddleCenter;
        textComponent.raycastTarget = false;

        if (settings.textFont != null)
        {
            textComponent.font = settings.textFont;
        }
        else
        {
            // Usar fuente por defecto
            textComponent.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        }

        return textComponent;
    }

    /// <summary>
    /// Configura el RectTransform de un GameObject
    /// </summary>
    private void SetRectTransform(GameObject target, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        RectTransform rect = target.GetComponent<RectTransform>();
        if (rect != null)
        {
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }
    }

    /// <summary>
    /// Establece un campo privado usando reflexión (para configurar el HandGestureIndicator)
    /// </summary>
    private void SetPrivateField(object target, string fieldName, object value)
    {
        System.Type type = target.GetType();
        System.Reflection.FieldInfo field = type.GetField(fieldName, 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        if (field != null)
        {
            field.SetValue(target, value);
        }
        else
        {
            Debug.LogWarning($"No se pudo encontrar el campo privado: {fieldName}");
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (previewInEditor && autoSetupHierarchy)
        {
            SetupCurrentGameObject();
        }
    }
#endif
} 