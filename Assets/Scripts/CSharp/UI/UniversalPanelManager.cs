using UnityEngine;
using System.Collections.Generic;
using System;

[Serializable]
public class PanelConfig
{
    [Header("Panel Configuration")]
    public string panelName = "MyPanel";
    public GameObject mainPanel;           // El panel principal
    public GameObject fingerPanel;         // Los dedos/gestos asociados
    public bool isDefaultPanel = false;    // Panel que se muestra al inicio
    
    [Header("Optional")]
    public string displayName = "";        // Nombre para mostrar en logs
    
    public string GetDisplayName() => string.IsNullOrEmpty(displayName) ? panelName : displayName;
}

public class UniversalPanelManager : MonoBehaviour
{
    [Header("Panel System Configuration")]
    [SerializeField] private List<PanelConfig> allPanels = new List<PanelConfig>();
    
    [Header("Auto-detection")]
    [SerializeField] private bool enableAutoDetection = true;
    [SerializeField] private bool enableDetailedLogs = true;
    
    [Header("Navigation")]
    [SerializeField] private bool allowPanelStacking = false; // Para navegación hacia atrás
    
    // Singleton
    public static UniversalPanelManager Instance { get; private set; }
    
    // Estado actual
    private PanelConfig currentPanel;
    private Stack<PanelConfig> panelHistory = new Stack<PanelConfig>();
    
    // Eventos
    public static event Action<string> OnPanelChanged;
    public static event Action<string, string> OnPanelTransition; // from, to
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // Aplicar DontDestroyOnLoad al objeto raíz para que todo el prefab de Managers persista.
            DontDestroyOnLoad(transform.root.gameObject);
        }
        else
        {
            // Si ya existe una instancia (porque volvimos a la escena del menú),
            // destruimos solo el objeto raíz duplicado.
            Destroy(transform.root.gameObject);
            return;
        }
        
        InitializeSystem();
    }
    
    void Start()
    {
        ShowDefaultPanel();
    }
    
    void InitializeSystem()
    {
        if (enableAutoDetection)
        {
            AutoDetectPanels();
        }
        
        ValidatePanelConfiguration();
        LogSystemStatus();
    }
    
    void AutoDetectPanels()
    {
        // DebugLog("🔍 Iniciando auto-detección de paneles...");
        
        // Si no hay paneles configurados, buscar automáticamente
        if (allPanels.Count == 0)
        {
            // Buscar patrones comunes
            AutoAddPanel("MenuPanel", "FingerImages", "Menú Principal", true);
            AutoAddPanel("MainMenuPanel", "MainMenuFingers", "Menú Principal", true);
            AutoAddPanel("SettingsPanel", "SettingsFingers", "Configuración", false);
            AutoAddPanel("ConfigPanel", "ConfigFingers", "Configuración", false);
            AutoAddPanel("TutorialPanel", "TutorialFingers", "Tutorial", false);
            AutoAddPanel("OptionsPanel", "OptionsFingers", "Opciones", false);
        }
        else
        {
            // Auto-completar paneles existentes que tengan campos vacíos
            foreach (var panel in allPanels)
            {
                if (panel.mainPanel == null)
                {
                    panel.mainPanel = GameObject.Find(panel.panelName);
                }
                
                if (panel.fingerPanel == null && panel.mainPanel != null)
                {
                    // Buscar patrones comunes para dedos
                    string[] patterns = { 
                        panel.panelName.Replace("Panel", "Fingers"),
                        panel.panelName.Replace("Panel", "Gestures"),
                        panel.panelName + "Fingers",
                        panel.panelName + "Gestures"
                    };
                    
                    foreach (string pattern in patterns)
                    {
                        GameObject found = GameObject.Find(pattern);
                        if (found != null)
                        {
                            panel.fingerPanel = found;
                            // DebugLog($"✅ Auto-detectado {pattern} para {panel.panelName}");
                            break;
                        }
                    }
                }
            }
        }
    }
    
    void AutoAddPanel(string panelName, string fingerName, string displayName, bool isDefault)
    {
        GameObject mainPanel = GameObject.Find(panelName);
        if (mainPanel != null)
        {
            GameObject fingerPanel = GameObject.Find(fingerName);
            
            PanelConfig config = new PanelConfig
            {
                panelName = panelName,
                mainPanel = mainPanel,
                fingerPanel = fingerPanel,
                displayName = displayName,
                isDefaultPanel = isDefault && GetDefaultPanel() == null
            };
            
            allPanels.Add(config);
            // DebugLog($"✅ Auto-agregado panel: {displayName} ({panelName})");
        }
    }
    
    void ValidatePanelConfiguration()
    {
        List<PanelConfig> validPanels = new List<PanelConfig>();
        
        foreach (var panel in allPanels)
        {
            if (panel.mainPanel != null)
            {
                validPanels.Add(panel);
            }
            else
            {
                // DebugLog($"⚠️ Panel inválido: {panel.panelName} (mainPanel es null)");
            }
        }
        
        allPanels = validPanels;
        
        // Asegurar que hay al menos un panel por defecto
        if (GetDefaultPanel() == null && allPanels.Count > 0)
        {
            allPanels[0].isDefaultPanel = true;
            // DebugLog($"📌 Panel por defecto establecido: {allPanels[0].GetDisplayName()}");
        }
    }
    
    void LogSystemStatus()
    {
        if (!enableDetailedLogs) return;
        
        // DebugLog($"🎛️ Sistema de Paneles Universal inicializado:");
        // DebugLog($"   📊 Total de paneles: {allPanels.Count}");
        
        foreach (var panel in allPanels)
        {
            string status = $"   📱 {panel.GetDisplayName()}";
            status += panel.isDefaultPanel ? " (DEFAULT)" : "";
            status += panel.fingerPanel != null ? " + Gestos" : " (Sin gestos)";
            // DebugLog(status);
        }
    }
    
    // ===== MÉTODOS PÚBLICOS PRINCIPALES =====
    
    public void ShowPanel(string panelName)
    {
        PanelConfig targetPanel = GetPanelByName(panelName);
        if (targetPanel != null)
        {
            ShowPanel(targetPanel);
        }
        else
        {
            DebugLog($"❌ Panel no encontrado: {panelName}");
        }
    }
    
    public void ShowPanel(PanelConfig targetPanel)
    {
        if (targetPanel == null || targetPanel.mainPanel == null)
        {
            DebugLog("❌ Panel inválido");
            return;
        }
        
        // Si ya estamos en el mismo panel, no hacer nada
        if (currentPanel != null && currentPanel == targetPanel)
        {
            // DebugLog($"⚠️ Ya estamos en el panel: {targetPanel.GetDisplayName()}");
            return;
        }
        
        string fromPanel = currentPanel?.GetDisplayName() ?? "None";
        string toPanel = targetPanel.GetDisplayName();
        
        DebugLog($"🔄 Transición: {fromPanel} → {toPanel}");
        
        // Guardar en historial si está habilitado
        if (allowPanelStacking && currentPanel != null && currentPanel != targetPanel)
        {
            panelHistory.Push(currentPanel);
        }
        
        // Ocultar panel actual
        if (currentPanel != null)
        {
            HidePanel(currentPanel);
        }
        
        // Mostrar nuevo panel
        ActivatePanel(targetPanel);
        
        // Actualizar estado
        currentPanel = targetPanel;
        
        // Disparar eventos
        OnPanelChanged?.Invoke(toPanel);
        OnPanelTransition?.Invoke(fromPanel, toPanel);
    }
    
    public void GoBack()
    {
        if (allowPanelStacking && panelHistory.Count > 0)
        {
            PanelConfig previousPanel = panelHistory.Pop();
            ShowPanel(previousPanel);
            // DebugLog($"⬅️ Volviendo a: {previousPanel.GetDisplayName()}");
        }
        else
        {
            DebugLog("⚠️ No hay historial de paneles para volver");
            ShowDefaultPanel();
        }
    }
    
    public void ShowDefaultPanel()
    {
        PanelConfig defaultPanel = GetDefaultPanel();
        if (defaultPanel != null)
        {
            ShowPanel(defaultPanel);
        }
        else if (allPanels.Count > 0)
        {
            ShowPanel(allPanels[0]);
        }
        else
        {
            DebugLog("❌ No hay paneles configurados");
        }
    }
    
    // ===== MÉTODOS DE UTILIDAD =====
    
    void HidePanel(PanelConfig panel)
    {
        if (panel.mainPanel != null)
        {
            panel.mainPanel.SetActive(false);
            // DebugLog($"❌ {panel.GetDisplayName()} panel oculto");
        }
        
        if (panel.fingerPanel != null)
        {
            panel.fingerPanel.SetActive(false);
            // DebugLog($"❌ {panel.GetDisplayName()} gestos ocultos");
        }
    }
    
    void ActivatePanel(PanelConfig panel)
    {
        if (panel.mainPanel != null)
        {
            panel.mainPanel.SetActive(true);
            // DebugLog($"✅ {panel.GetDisplayName()} panel activado");
        }
        
        if (panel.fingerPanel != null)
        {
            panel.fingerPanel.SetActive(true);
            // DebugLog($"✅ {panel.GetDisplayName()} gestos activados");
        }
    }
    
    PanelConfig GetPanelByName(string name)
    {
        return allPanels.Find(p => 
            p.panelName.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            p.GetDisplayName().Equals(name, StringComparison.OrdinalIgnoreCase));
    }
    
    PanelConfig GetDefaultPanel()
    {
        return allPanels.Find(p => p.isDefaultPanel);
    }
    
    // ===== MÉTODOS PÚBLICOS DE INFORMACIÓN =====
    
    public List<string> GetAllPanelNames()
    {
        List<string> names = new List<string>();
        foreach (var panel in allPanels)
        {
            names.Add(panel.panelName);
        }
        return names;
    }
    
    public string GetCurrentPanelName()
    {
        return currentPanel?.panelName ?? "None";
    }
    
    public bool IsCurrentPanel(string panelName)
    {
        return currentPanel != null && 
               (currentPanel.panelName.Equals(panelName, StringComparison.OrdinalIgnoreCase) ||
                currentPanel.GetDisplayName().Equals(panelName, StringComparison.OrdinalIgnoreCase));
    }
    
    // ===== MÉTODOS PARA INTEGRACIÓN CON GESTOS =====
    
    public void RegisterPanelNavigation(string fromPanel, string toPanel, int fingerCount)
    {
        // Este método puede ser usado para configurar navegación automática
        // DebugLog($"🔗 Navegación registrada: {fromPanel} --{fingerCount}dedos--> {toPanel}");
    }
    
    // ===== DEBUG Y TESTING =====
    
    void DebugLog(string message)
    {
        if (enableDetailedLogs)
        {
            // Debug.Log($"[UniversalPanelManager] {message}");
        }
    }
    
    [ContextMenu("Test: Show All Panels Info")]
    void TestShowAllPanelsInfo()
    {
        LogSystemStatus();
    }
    
    [ContextMenu("Test: Show Default Panel")]
    void TestShowDefaultPanel()
    {
        ShowDefaultPanel();
    }
    
    [ContextMenu("Test: Go Back")]
    void TestGoBack()
    {
        GoBack();
    }
    
    // ===== MÉTODOS ESTÁTICOS DE CONVENIENCIA =====
    
    public static void ShowPanelStatic(string panelName)
    {
        Instance?.ShowPanel(panelName);
    }
    
    public static void GoBackStatic()
    {
        Instance?.GoBack();
    }
    
    public static void ShowDefaultPanelStatic()
    {
        Instance?.ShowDefaultPanel();
    }
} 