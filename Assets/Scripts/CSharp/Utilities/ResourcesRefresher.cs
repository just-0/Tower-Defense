using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class ResourcesRefresher : MonoBehaviour
{
    [Header("Debug Tools")]
    [SerializeField] private bool refreshOnStart = true;
    [SerializeField] private bool enableDetailedLogs = true;
    
    void Start()
    {
        if (refreshOnStart)
        {
            RefreshResources();
        }
    }
    
    public void RefreshResources()
    {
        DebugLog("🔄 Forzando refresh de Resources...");
        
        #if UNITY_EDITOR
        AssetDatabase.Refresh();
        DebugLog("✅ AssetDatabase.Refresh() ejecutado");
        #endif
        
        // Forzar descarga/recarga de recursos
        Resources.UnloadUnusedAssets();
        DebugLog("✅ Resources.UnloadUnusedAssets() ejecutado");
        
        // Verificar que las imágenes se pueden cargar
        TestImageLoading();
    }
    
    void TestImageLoading()
    {
        DebugLog("🧪 Probando carga de imágenes...");
        
        string[] imageNames = { "1", "2", "3", "4", "5", "circular_sprite" };
        
        foreach (string imageName in imageNames)
        {
            // Probar carga sin extensión
            Sprite sprite1 = Resources.Load<Sprite>($"UI/Hands/{imageName}");
            
            // Probar carga con extensión
            Sprite sprite2 = Resources.Load<Sprite>($"UI/Hands/{imageName}.png");
            
            // Probar carga como Texture2D
            Texture2D texture = Resources.Load<Texture2D>($"UI/Hands/{imageName}");
            
            string status = "";
            if (sprite1 != null) status += "✅Sprite ";
            if (sprite2 != null) status += "✅Sprite.png ";
            if (texture != null) status += "✅Texture ";
            
            if (string.IsNullOrEmpty(status))
            {
                status = "❌ No encontrado";
            }
            
            DebugLog($"   {imageName}: {status}");
        }
    }
    
    void DebugLog(string message)
    {
        if (enableDetailedLogs)
        {
            // Debug.Log($"[ResourcesRefresher] {message}");
        }
    }
    
    [ContextMenu("Refresh Resources Now")]
    public void ForceRefresh()
    {
        RefreshResources();
    }
    
    [ContextMenu("Test Image Loading")]
    public void ForceTestImages()
    {
        TestImageLoading();
    }
} 