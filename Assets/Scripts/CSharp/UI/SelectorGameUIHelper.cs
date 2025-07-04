using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Helper script para configurar automáticamente la UI del jugador Selector.
/// Coloca este script en el Canvas principal de la escena del Selector.
/// </summary>
public class SelectorGameUIHelper : MonoBehaviour
{
    [Header("Crear UI Automáticamente")]
    [SerializeField] private bool autoCreateUI = true;
    [SerializeField] private Canvas targetCanvas;
    
    [Header("Configuración de Posicionamiento")]
    [SerializeField] private Vector2 gameInfoPosition = new Vector2(-450, 250);
    [SerializeField] private Vector2 turretInfoPosition = new Vector2(450, 250);
    [SerializeField] private Vector2 weaponPanelPosition = new Vector2(0, -250);
    
    void Start()
    {
        if (autoCreateUI)
        {
            CreateSelectorUI();
        }
    }
    
    [ContextMenu("Crear UI del Selector")]
    public void CreateSelectorUI()
    {
        if (targetCanvas == null)
            targetCanvas = FindObjectOfType<Canvas>();
            
        if (targetCanvas == null)
        {
            Debug.LogError("No se encontró Canvas. Crea un Canvas primero.");
            return;
        }
        
        CreateGameInfoPanel();
        CreateTurretInfoPanel();
        CreateWeaponSelectionPanel();
        CreatePhaseIndicator();
        CreateGameOverPanel();
        
        Debug.Log("✅ UI del Selector creada exitosamente!");
    }
    
    private void CreateGameInfoPanel()
    {
        // Panel contenedor para información del juego
        GameObject gameInfoPanel = new GameObject("GameInfoPanel");
        gameInfoPanel.transform.SetParent(targetCanvas.transform, false);
        
        RectTransform panelRect = gameInfoPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0, 1);
        panelRect.anchorMax = new Vector2(0, 1);
        panelRect.pivot = new Vector2(0, 1);
        panelRect.anchoredPosition = gameInfoPosition;
        panelRect.sizeDelta = new Vector2(300, 150);
        
        // Fondo del panel
        Image panelBg = gameInfoPanel.AddComponent<Image>();
        panelBg.color = new Color(0, 0, 0, 0.7f);
        
        // Texto de Oro
        CreateTextElement(gameInfoPanel, "GoldText", "Oro: 100", new Vector2(0, -20), new Vector2(280, 30));
        
        // Texto de Oleada
        CreateTextElement(gameInfoPanel, "WaveText", "Oleada: 1", new Vector2(0, -50), new Vector2(280, 30));
        
        // Texto de Salud
        CreateTextElement(gameInfoPanel, "HealthText", "Salud: 100/100", new Vector2(0, -80), new Vector2(280, 30));
        
        // Barra de Salud
        CreateHealthBar(gameInfoPanel);
    }
    
    private void CreateTurretInfoPanel()
    {
        GameObject turretInfoPanel = new GameObject("TurretInfoPanel");
        turretInfoPanel.transform.SetParent(targetCanvas.transform, false);
        
        RectTransform panelRect = turretInfoPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(1, 1);
        panelRect.anchorMax = new Vector2(1, 1);
        panelRect.pivot = new Vector2(1, 1);
        panelRect.anchoredPosition = turretInfoPosition;
        panelRect.sizeDelta = new Vector2(300, 200);
        
        // Fondo del panel
        Image panelBg = turretInfoPanel.AddComponent<Image>();
        panelBg.color = new Color(0, 0, 0, 0.7f);
        
        // Información de Torreta
        CreateTextElement(turretInfoPanel, "TurretNameText", "Torreta Básica", new Vector2(0, -20), new Vector2(280, 30));
        CreateTextElement(turretInfoPanel, "TurretCostText", "Costo: 50", new Vector2(0, -50), new Vector2(280, 30));
        CreateTextElement(turretInfoPanel, "TurretDamageText", "Daño: 25", new Vector2(0, -80), new Vector2(280, 30));
        CreateTextElement(turretInfoPanel, "TurretRangeText", "Rango: 3.0", new Vector2(0, -110), new Vector2(280, 30));
        CreateTextElement(turretInfoPanel, "TurretLevelText", "Nivel: 1", new Vector2(0, -140), new Vector2(280, 30));
    }
    
    private void CreateWeaponSelectionPanel()
    {
        GameObject weaponPanel = new GameObject("WeaponSelectionPanel");
        weaponPanel.transform.SetParent(targetCanvas.transform, false);
        
        RectTransform panelRect = weaponPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0);
        panelRect.anchorMax = new Vector2(0.5f, 0);
        panelRect.pivot = new Vector2(0.5f, 0);
        panelRect.anchoredPosition = weaponPanelPosition;
        panelRect.sizeDelta = new Vector2(400, 120);
        
        // Fondo del panel
        Image panelBg = weaponPanel.AddComponent<Image>();
        panelBg.color = new Color(0, 0, 0, 0.7f);
        
        // Título
        CreateTextElement(weaponPanel, "WeaponTitle", "Selección de Armas", new Vector2(0, -20), new Vector2(380, 30));
        
        // Crear 3 slots de armas
        for (int i = 0; i < 3; i++)
        {
            CreateWeaponSlot(weaponPanel, i);
        }
    }
    
    private void CreateWeaponSlot(GameObject parent, int index)
    {
        GameObject weaponSlot = new GameObject($"Weapon{index + 1}");
        weaponSlot.transform.SetParent(parent.transform, false);
        
        RectTransform slotRect = weaponSlot.AddComponent<RectTransform>();
        slotRect.anchorMin = new Vector2(0.5f, 0.5f);
        slotRect.anchorMax = new Vector2(0.5f, 0.5f);
        slotRect.pivot = new Vector2(0.5f, 0.5f);
        slotRect.anchoredPosition = new Vector2(-100 + (index * 100), -15);
        slotRect.sizeDelta = new Vector2(80, 60);
        
        // Imagen del arma
        Image weaponImage = weaponSlot.AddComponent<Image>();
        
        // Cargar sprite del arma si existe
        string weaponPath = $"UI/Weapons/w{index + 1}";
        Sprite weaponSprite = Resources.Load<Sprite>(weaponPath);
        
        if (weaponSprite != null)
        {
            weaponImage.sprite = weaponSprite;
        }
        else
        {
            // Color temporal si no hay sprite
            weaponImage.color = index == 0 ? Color.white : Color.gray;
        }
        
        // Texto del número de arma
        CreateTextElement(weaponSlot, $"WeaponNumber", $"{index + 1}", new Vector2(0, -40), new Vector2(80, 20));
    }
    
    private void CreatePhaseIndicator()
    {
        GameObject phasePanel = new GameObject("PhaseIndicator");
        phasePanel.transform.SetParent(targetCanvas.transform, false);
        
        RectTransform panelRect = phasePanel.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 1);
        panelRect.anchorMax = new Vector2(0.5f, 1);
        panelRect.pivot = new Vector2(0.5f, 1);
        panelRect.anchoredPosition = new Vector2(0, -20);
        panelRect.sizeDelta = new Vector2(300, 50);
        
        // Fondo del indicador
        Image phaseBg = phasePanel.AddComponent<Image>();
        phaseBg.color = new Color(0, 0.5f, 0, 0.8f);
        
        // Texto de fase
        CreateTextElement(phasePanel, "PhaseText", "Fase: Planificación", new Vector2(0, 0), new Vector2(280, 40));
        
        // Panel de combate (inicialmente oculto)
        GameObject combatPanel = new GameObject("CombatPhasePanel");
        combatPanel.transform.SetParent(phasePanel.transform, false);
        combatPanel.SetActive(false);
        
        RectTransform combatRect = combatPanel.AddComponent<RectTransform>();
        combatRect.anchorMin = Vector2.zero;
        combatRect.anchorMax = Vector2.one;
        combatRect.offsetMin = Vector2.zero;
        combatRect.offsetMax = Vector2.zero;
        
        Image combatBg = combatPanel.AddComponent<Image>();
        combatBg.color = new Color(0.5f, 0, 0, 0.8f);
    }
    
    private void CreateGameOverPanel()
    {
        GameObject gameOverPanel = new GameObject("GameOverPanel");
        gameOverPanel.transform.SetParent(targetCanvas.transform, false);
        gameOverPanel.SetActive(false); // Inicialmente oculto
        
        RectTransform panelRect = gameOverPanel.AddComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
        
        // Fondo semi-transparente
        Image panelBg = gameOverPanel.AddComponent<Image>();
        panelBg.color = new Color(0, 0, 0, 0.8f);
        
        // Texto de Game Over
        CreateTextElement(gameOverPanel, "GameOverText", "JUEGO TERMINADO", new Vector2(0, 0), new Vector2(400, 80));
    }
    
    private void CreateHealthBar(GameObject parent)
    {
        GameObject healthBarContainer = new GameObject("HealthBarContainer");
        healthBarContainer.transform.SetParent(parent.transform, false);
        
        RectTransform containerRect = healthBarContainer.AddComponent<RectTransform>();
        containerRect.anchorMin = new Vector2(0.5f, 0.5f);
        containerRect.anchorMax = new Vector2(0.5f, 0.5f);
        containerRect.pivot = new Vector2(0.5f, 0.5f);
        containerRect.anchoredPosition = new Vector2(0, -110);
        containerRect.sizeDelta = new Vector2(250, 20);
        
        // Fondo de la barra
        Image healthBarBg = healthBarContainer.AddComponent<Image>();
        healthBarBg.color = new Color(0.3f, 0.3f, 0.3f, 0.8f);
        
        // Barra de salud (fill)
        GameObject healthFill = new GameObject("HealthBarFill");
        healthFill.transform.SetParent(healthBarContainer.transform, false);
        
        RectTransform fillRect = healthFill.AddComponent<RectTransform>();
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = Vector2.one;
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;
        
        Image healthFillImage = healthFill.AddComponent<Image>();
        healthFillImage.color = Color.green;
        healthFillImage.type = Image.Type.Filled;
        healthFillImage.fillMethod = Image.FillMethod.Horizontal;
    }
    
    private GameObject CreateTextElement(GameObject parent, string name, string text, Vector2 position, Vector2 size)
    {
        GameObject textObj = new GameObject(name);
        textObj.transform.SetParent(parent.transform, false);
        
        RectTransform textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = new Vector2(0.5f, 0.5f);
        textRect.anchorMax = new Vector2(0.5f, 0.5f);
        textRect.pivot = new Vector2(0.5f, 0.5f);
        textRect.anchoredPosition = position;
        textRect.sizeDelta = size;
        
        Text textComponent = textObj.AddComponent<Text>();
        textComponent.text = text;
        textComponent.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        textComponent.fontSize = 16;
        textComponent.color = Color.white;
        textComponent.alignment = TextAnchor.MiddleCenter;
        
        return textObj;
    }
} 