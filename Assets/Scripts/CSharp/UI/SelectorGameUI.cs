using UnityEngine;
using UnityEngine.UI;
// TMPro removido ya que no se usa en el código

/// <summary>
/// Maneja la UI del juego para el jugador Selector en modo multijugador.
/// Recibe actualizaciones del estado del juego desde el Colocador via Photon.
/// Se configura automáticamente si no se asignan los elementos manualmente.
/// </summary>
public class SelectorGameUI : MonoBehaviour
{
    [Header("UI Elements - Game Info")]
    [SerializeField] private Text goldText;
    [SerializeField] private Text waveText;
    [SerializeField] private Text healthText;
    [SerializeField] private Image healthBarFill;
    
    [Header("UI Elements - Turret Info")]
    [SerializeField] private Text turretNameText;
    [SerializeField] private Text turretCostText;
    [SerializeField] private Text turretDamageText;
    [SerializeField] private Text turretRangeText;
    [SerializeField] private Text turretLevelText;
    [SerializeField] private GameObject turretInfoPanel;
    
    [Header("UI Elements - Weapon Selection")]
    [SerializeField] private Image[] weaponIcons; // Array de imágenes para las 3 armas
    [SerializeField] private GameObject weaponSelectionPanel;
    [SerializeField] private Color selectedWeaponColor = Color.white;
    [SerializeField] private Color unselectedWeaponColor = Color.gray;
    
    [Header("UI Elements - Game Over")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private Text gameOverText;
    
    [Header("UI Elements - Phase Indicators")]
    [SerializeField] private GameObject planningPhasePanel;
    [SerializeField] private GameObject combatPhasePanel;
    [SerializeField] private Text phaseText;
    
    [Header("Auto Configuration")]
    [SerializeField] private bool autoFindUIElements = true;
    
    // Estado del juego
    private int currentGold = 0;
    private int currentWave = 1;
    private int currentHealth = 100;
    private int maxHealth = 100;
    private int selectedWeaponIndex = 0;
    private bool isInCombatPhase = false;
    
    public static SelectorGameUI Instance { get; private set; }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    void Start()
    {
        // Auto-buscar elementos de UI si están marcados para ello
        if (autoFindUIElements)
        {
            AutoFindUIElements();
        }
        
        // Suscribirse a eventos de PhotonManager
        if (PhotonManager.Instance != null)
        {
            PhotonManager.Instance.OnGoldUpdateReceived += UpdateGold;
            PhotonManager.Instance.OnHealthUpdateReceived += UpdateHealth;
            PhotonManager.Instance.OnWaveUpdateReceived += UpdateWave;
            PhotonManager.Instance.OnBaseDestroyedReceived += ShowGameOver;
            PhotonManager.Instance.OnMonsterDeathReceived += OnMonsterDeath;
            PhotonManager.Instance.OnTurretInfoReceived += UpdateTurretInfo;
            Debug.Log("[SelectorGameUI] Suscrito a eventos de PhotonManager");
        }
        else
        {
            Debug.LogError("[SelectorGameUI] PhotonManager.Instance es null. No se pueden suscribir eventos.");
        }
        
        // Inicializar UI
        InitializeUI();
    }
    
    private void AutoFindUIElements()
    {
        // Buscar elementos de texto por nombre común
        Text[] allTexts = GetComponentsInChildren<Text>(true);
        foreach (Text text in allTexts)
        {
            string name = text.gameObject.name.ToLower();
            
            if (name.Contains("gold") || name.Contains("oro"))
                goldText = text;
            else if (name.Contains("wave") || name.Contains("oleada"))
                waveText = text;
            else if (name.Contains("health") || name.Contains("vida") || name.Contains("salud"))
                healthText = text;
            else if (name.Contains("turret") && name.Contains("name"))
                turretNameText = text;
            else if (name.Contains("turret") && name.Contains("cost"))
                turretCostText = text;
            else if (name.Contains("turret") && name.Contains("damage"))
                turretDamageText = text;
            else if (name.Contains("turret") && name.Contains("range"))
                turretRangeText = text;
            else if (name.Contains("turret") && name.Contains("level"))
                turretLevelText = text;
            else if (name.Contains("phase") || name.Contains("fase"))
                phaseText = text;
            else if (name.Contains("gameover"))
                gameOverText = text;
        }
        
        // Buscar elementos de imagen y paneles
        Image[] allImages = GetComponentsInChildren<Image>(true);
        foreach (Image img in allImages)
        {
            string name = img.gameObject.name.ToLower();
            if (name.Contains("health") && name.Contains("fill"))
                healthBarFill = img;
        }
        
        // Buscar paneles
        Transform[] allTransforms = GetComponentsInChildren<Transform>(true);
        foreach (Transform t in allTransforms)
        {
            string name = t.gameObject.name.ToLower();
            if (name.Contains("turret") && name.Contains("info"))
                turretInfoPanel = t.gameObject;
            else if (name.Contains("weapon") && name.Contains("selection"))
                weaponSelectionPanel = t.gameObject;
            else if (name.Contains("planning") && name.Contains("phase"))
                planningPhasePanel = t.gameObject;
            else if (name.Contains("combat") && name.Contains("phase"))
                combatPhasePanel = t.gameObject;
            else if (name.Contains("gameover"))
                gameOverPanel = t.gameObject;
        }
        
        Debug.Log("[SelectorGameUI] Auto-configuración de UI completada");
    }
    
    private void InitializeUI()
    {
        UpdateGoldDisplay();
        UpdateWaveDisplay();
        UpdateHealthDisplay();
        UpdateWeaponSelection();
        SetPhase(false); // Empezar en fase de planificación
        
        // Ocultar panel de game over inicialmente
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(false);
        }
        
        Debug.Log("[SelectorGameUI] UI inicializada correctamente");
    }
    
    // --- MÉTODOS DE ACTUALIZACIÓN DE UI ---
    
    private void UpdateGold(int newGold)
    {
        currentGold = newGold;
        UpdateGoldDisplay();
        Debug.Log($"[SelectorGameUI] Oro actualizado: {newGold}");
    }
    
    private void UpdateHealth(int health, int maxHp)
    {
        currentHealth = health;
        maxHealth = maxHp;
        UpdateHealthDisplay();
        Debug.Log($"[SelectorGameUI] Salud actualizada: {health}/{maxHp}");
    }
    
    private void UpdateWave(int wave)
    {
        currentWave = wave;
        UpdateWaveDisplay();
        Debug.Log($"[SelectorGameUI] Oleada actualizada: {wave}");
    }
    
    private void UpdateTurretInfo(string name, int cost, float damage, float range, int level)
    {
        if (turretNameText != null) turretNameText.text = name;
        if (turretCostText != null) turretCostText.text = $"Costo: {cost} oro";
        if (turretDamageText != null) turretDamageText.text = $"Daño: {damage:F0}";
        if (turretRangeText != null) turretRangeText.text = $"Rango: {range:F1}";
        if (turretLevelText != null) turretLevelText.text = $"Nivel: {level}";
        
        // Mostrar panel de información de torreta si está oculto
        if (turretInfoPanel != null)
        {
            turretInfoPanel.SetActive(true);
        }
        
        Debug.Log($"[SelectorGameUI] Info de torreta actualizada: {name}");
    }
    
    // --- MÉTODOS DE VISUALIZACIÓN ---
    
    private void UpdateGoldDisplay()
    {
        if (goldText != null)
        {
            goldText.text = $"💰 Oro: {currentGold}";
        }
    }
    
    private void UpdateWaveDisplay()
    {
        if (waveText != null)
        {
            waveText.text = $"🌊 Oleada: {currentWave}";
        }
    }
    
    private void UpdateHealthDisplay()
    {
        if (healthText != null)
        {
            healthText.text = $"🏠 Base: {currentHealth}/{maxHealth}";
        }
        
        if (healthBarFill != null)
        {
            float healthPercentage = maxHealth > 0 ? (float)currentHealth / maxHealth : 0f;
            healthBarFill.fillAmount = healthPercentage;
            
            // Cambiar color según la salud
            if (healthPercentage > 0.6f)
                healthBarFill.color = Color.green;
            else if (healthPercentage > 0.3f)
                healthBarFill.color = Color.yellow;
            else
                healthBarFill.color = Color.red;
        }
    }
    
    private void UpdateWeaponSelection()
    {
        if (weaponIcons != null)
        {
            for (int i = 0; i < weaponIcons.Length; i++)
            {
                if (weaponIcons[i] != null)
                {
                    weaponIcons[i].color = (i == selectedWeaponIndex) ? selectedWeaponColor : unselectedWeaponColor;
                }
            }
        }
    }
    
    // --- MÉTODOS PÚBLICOS PARA CONTROL EXTERNO ---
    
    public void SetSelectedWeapon(int weaponIndex)
    {
        if (weaponIndex >= 0 && weaponIndex < 3)
        {
            selectedWeaponIndex = weaponIndex;
            UpdateWeaponSelection();
            Debug.Log($"[SelectorGameUI] Arma seleccionada: {weaponIndex + 1}");
        }
    }
    
    public void SetPhase(bool combatPhase)
    {
        isInCombatPhase = combatPhase;
        
        if (planningPhasePanel != null)
        {
            planningPhasePanel.SetActive(!combatPhase);
        }
        
        if (combatPhasePanel != null)
        {
            combatPhasePanel.SetActive(combatPhase);
        }
        
        if (weaponSelectionPanel != null)
        {
            weaponSelectionPanel.SetActive(combatPhase);
        }
        
        if (phaseText != null)
        {
            phaseText.text = combatPhase ? "⚔️ FASE: COMBATE" : "🛠️ FASE: PLANIFICACIÓN";
        }
        
        Debug.Log($"[SelectorGameUI] Fase cambiada a: {(combatPhase ? "Combate" : "Planificación")}");
    }
    
    // --- EVENTOS DEL JUEGO ---
    
    private void OnMonsterDeath()
    {
        Debug.Log("[SelectorGameUI] Monstruo eliminado - Oro actualizado");
    }
    
    private void ShowGameOver()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }
        
        if (gameOverText != null)
        {
            gameOverText.text = "💀 ¡GAME OVER!\nLa base ha sido destruida";
        }
        
        Debug.Log("[SelectorGameUI] Game Over mostrado");
    }
    
    // --- MÉTODOS PÚBLICOS PARA CONFIGURACIÓN MANUAL ---
    
    [System.Obsolete("Use SetupUI() method instead")]
    public void ConfigureUI(Text gold, Text wave, Text health, Image healthBar, 
                          Text turretName, Text turretCost, Text turretDamage, Text turretRange, Text turretLevel,
                          GameObject turretPanel, Image[] weapons, GameObject weaponPanel,
                          GameObject gameOverPnl, Text gameOverTxt, Text phaseTxt)
    {
        SetupUI(gold, wave, health, healthBar, turretName, turretCost, turretDamage, turretRange, turretLevel,
               turretPanel, weapons, weaponPanel, gameOverPnl, gameOverTxt, phaseTxt);
    }
    
    public void SetupUI(Text gold, Text wave, Text health, Image healthBar, 
                       Text turretName, Text turretCost, Text turretDamage, Text turretRange, Text turretLevel,
                       GameObject turretPanel, Image[] weapons, GameObject weaponPanel,
                       GameObject gameOverPnl, Text gameOverTxt, Text phaseTxt)
    {
        goldText = gold;
        waveText = wave;
        healthText = health;
        healthBarFill = healthBar;
        turretNameText = turretName;
        turretCostText = turretCost;
        turretDamageText = turretDamage;
        turretRangeText = turretRange;
        turretLevelText = turretLevel;
        turretInfoPanel = turretPanel;
        weaponIcons = weapons;
        weaponSelectionPanel = weaponPanel;
        gameOverPanel = gameOverPnl;
        gameOverText = gameOverTxt;
        phaseText = phaseTxt;
        
        InitializeUI();
        Debug.Log("[SelectorGameUI] UI configurada manualmente");
    }
    
    // --- GETTERS PARA INFORMACIÓN ACTUAL ---
    
    public int GetCurrentGold() => currentGold;
    public int GetCurrentWave() => currentWave;
    public int GetCurrentHealth() => currentHealth;
    public int GetMaxHealth() => maxHealth;
    public int GetSelectedWeapon() => selectedWeaponIndex;
    public bool IsInCombatPhase() => isInCombatPhase;
    
    // --- CLEANUP ---
    
    void OnDestroy()
    {
        // Desuscribirse de eventos para evitar memory leaks
        if (PhotonManager.Instance != null)
        {
            PhotonManager.Instance.OnGoldUpdateReceived -= UpdateGold;
            PhotonManager.Instance.OnHealthUpdateReceived -= UpdateHealth;
            PhotonManager.Instance.OnWaveUpdateReceived -= UpdateWave;
            PhotonManager.Instance.OnBaseDestroyedReceived -= ShowGameOver;
            PhotonManager.Instance.OnMonsterDeathReceived -= OnMonsterDeath;
            PhotonManager.Instance.OnTurretInfoReceived -= UpdateTurretInfo;
        }
        
        if (Instance == this)
        {
            Instance = null;
        }
        
        Debug.Log("[SelectorGameUI] Cleanup completado");
    }
} 