using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Helper para diagnosticar problemas con el sistema de gestos
/// </summary>
public class GestureDebugHelper : MonoBehaviour
{
    [Header("UI Debug")]
    [SerializeField] private Text debugText;
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private float updateInterval = 1f;
    
    [Header("Auto-Fix")]
    [SerializeField] private bool autoFixGestures = true;
    [SerializeField] private float checkInterval = 5f;
    
    private float lastUpdateTime = 0f;
    private float lastCheckTime = 0f;
    
    void Start()
    {
        if (debugText == null)
        {
            // Intentar encontrar un Text component automáticamente
            debugText = GetComponent<Text>();
            if (debugText == null)
            {
                debugText = FindObjectOfType<Text>();
            }
        }
        
        StartCoroutine(DebugRoutine());
    }
    
    private IEnumerator DebugRoutine()
    {
        while (true)
        {
            if (showDebugInfo)
            {
                UpdateDebugInfo();
            }
            
            if (autoFixGestures && Time.time - lastCheckTime > checkInterval)
            {
                CheckAndFixGestures();
                lastCheckTime = Time.time;
            }
            
            yield return new WaitForSeconds(updateInterval);
        }
    }
    
    private void UpdateDebugInfo()
    {
        if (debugText == null) return;
        
        string debugInfo = "=== GESTURE DEBUG INFO ===\n";
        
        // MenuGestureController status
        if (MenuGestureController.Instance != null)
        {
            debugInfo += "✅ MenuGestureController: OK\n";
        }
        else
        {
            debugInfo += "❌ MenuGestureController: NOT FOUND\n";
        }
        
        // SimpleGestureManager status
        SimpleGestureManager gestureManager = FindObjectOfType<SimpleGestureManager>();
        if (gestureManager != null)
        {
            debugInfo += "✅ SimpleGestureManager: OK\n";
            debugInfo += $"   Status: {gestureManager.GetStatusInfo()}\n";
        }
        else
        {
            debugInfo += "❌ SimpleGestureManager: NOT FOUND\n";
        }
        
        // SimpleHandGesture components
        SimpleHandGesture[] gestures = FindObjectsOfType<SimpleHandGesture>();
        debugInfo += $"👆 Active Gestures: {gestures.Length}\n";
        
        foreach (var gesture in gestures)
        {
            if (gesture.gameObject.activeInHierarchy)
            {
                debugInfo += $"   {gesture.GetFingerCount()} dedos: {gesture.name}\n";
            }
        }
        
        // UniversalPanelManager status
        if (UniversalPanelManager.Instance != null)
        {
            debugInfo += $"📱 Current Panel: {UniversalPanelManager.Instance.GetCurrentPanelName()}\n";
        }
        
        debugText.text = debugInfo;
    }
    
    private void CheckAndFixGestures()
    {
        // Verificar si MenuGestureController necesita reconexión
        if (MenuGestureController.Instance != null)
        {
            MenuGestureController.Instance.ReconnectIfNeeded();
        }
        
        // Verificar si SimpleGestureManager necesita refresh
        SimpleGestureManager gestureManager = FindObjectOfType<SimpleGestureManager>();
        if (gestureManager != null)
        {
            gestureManager.ForceRefresh();
        }
        
        // Verificar MenuUIBinder
        MenuUIBinder binder = FindObjectOfType<MenuUIBinder>();
        if (binder != null)
        {
            binder.ForceReregister();
        }
        
        Debug.Log("GestureDebugHelper: Auto-fix completado");
    }
    
    [ContextMenu("Force Fix Gestures")]
    public void ForceFixGestures()
    {
        CheckAndFixGestures();
        Debug.Log("GestureDebugHelper: Fix forzado ejecutado");
    }
    
    [ContextMenu("Reset All Gesture Systems")]
    public void ResetAllGestureSystems()
    {
        // Resetear todos los gestos activos
        SimpleHandGesture[] gestures = FindObjectsOfType<SimpleHandGesture>();
        foreach (var gesture in gestures)
        {
            gesture.Reset();
        }
        
        // Forzar reconexión del MenuGestureController
        if (MenuGestureController.Instance != null)
        {
            MenuGestureController.Instance.ReconnectIfNeeded();
        }
        
        Debug.Log("GestureDebugHelper: Todos los sistemas de gestos reseteados");
    }
    
    [ContextMenu("Show Gesture Status")]
    public void ShowGestureStatus()
    {
        UpdateDebugInfo();
        Debug.Log($"Current Gesture Status:\n{debugText.text}");
    }
} 