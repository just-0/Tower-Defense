using UnityEngine;

/// <summary>
/// GUÍA DE COLOCACIÓN ARUCO - INSTRUCCIONES DE USO
/// 
/// Este sistema muestra un rectángulo rojo en pantalla para indicar al jugador "Colocador" 
/// dónde debe colocar las hojas de color y el código ArUco durante las diferentes fases del juego.
/// 
/// == CÓMO CONFIGURAR ==
/// 
/// 1. CONFIGURACIÓN AUTOMÁTICA:
///    - El sistema se configura automáticamente al iniciar el juego
///    - El SAMSystemController crea la guía automáticamente si no existe
/// 
/// 2. CONFIGURACIÓN MANUAL:
///    - Añadir el componente ArUcoPlacementGuide a cualquier GameObject
///    - Asignar la referencia en el SAMSystemController
///    - Configurar los parámetros visuales en el inspector
/// 
/// == FASES DEL JUEGO ==
/// 
/// 🔴 FASE DE PLANIFICACIÓN:
///    - Muestra rectángulo ROJO semi-transparente
///    - Texto: "📄 COLOCA AQUÍ LAS HOJAS DE COLOR Y EL CÓDIGO ARUCO"
///    - Se muestra al inicio del juego
///    - Se oculta durante procesamiento SAM
///    - Vuelve a aparecer tras procesamiento exitoso o error
/// 
/// 🟢 FASE DE COMBATE:
///    - Muestra rectángulo VERDE semi-transparente
///    - Texto: "🎯 ZONA DE JUEGO ACTIVA"
///    - Se muestra cuando se inicia el combate
///    - Indica que la zona sigue siendo importante para el juego
/// 
/// == INTEGRACIÓN CON OTROS SISTEMAS ==
/// 
/// 1. SAMSystemController:
///    - Controla automáticamente la visibilidad según las fases
///    - Oculta durante procesamiento SAM
///    - Muestra según la fase actual (planificación/combate)
/// 
/// 2. UIManager:
///    - Proporciona métodos públicos para control manual
///    - ShowArUcoGuideForPlanning()
///    - ShowArUcoGuideForCombat()
///    - HideArUcoGuide()
///    - SetArUcoGuideEnabled(bool)
/// 
/// 3. GestureReceiver:
///    - Puede usar UIManager para controlar la guía según gestos
///    - Complementa el control automático del SAMSystemController
/// 
/// == PERSONALIZACIÓN ==
/// 
/// En el inspector del ArUcoPlacementGuide puedes modificar:
/// - Colores del rectángulo y borde
/// - Tamaño y posición del área
/// - Textos de instrucciones
/// - Animaciones de pulso
/// - Fuente y tamaño del texto
/// 
/// == EVENTOS Y CALLBACKS ==
/// 
/// El sistema responde automáticamente a:
/// - Inicio del juego (mostrar planificación)
/// - Procesamiento SAM (ocultar)
/// - Fin procesamiento SAM (mostrar planificación)
/// - Inicio combate (mostrar combate)
/// - Fin combate (mostrar planificación)
/// - Errores y timeouts (mostrar planificación)
/// 
/// == SOLUCIÓN DE PROBLEMAS ==
/// 
/// Si la guía no aparece:
/// 1. Verificar que autoCreateGuide esté habilitado en SAMSystemController
/// 2. Revisar que enableArUcoGuide esté habilitado en UIManager
/// 3. Comprobar que existe un Canvas en la escena
/// 4. Revisar la consola para mensajes de debug (🎯)
/// 
/// Si la guía aparece en posición incorrecta:
/// 1. Ajustar guidePosition y guideSize en el inspector
/// 2. Usar UpdateGuideArea() desde código para cambios dinámicos
/// 3. Verificar que el Canvas esté en modo Screen Space - Overlay
/// 
/// == CÓDIGO DE EJEMPLO ==
/// 
/// // Mostrar guía manualmente
/// if (ArUcoPlacementGuide.Instance != null)
/// {
///     ArUcoPlacementGuide.Instance.ShowPlanningGuide();
/// }
/// 
/// // Usar a través del UIManager
/// if (UIManager.Instance != null)
/// {
///     UIManager.Instance.ShowArUcoGuideForPlanning();
/// }
/// 
/// // Cambiar área dinámicamente
/// if (ArUcoPlacementGuide.Instance != null)
/// {
///     ArUcoPlacementGuide.Instance.UpdateGuideArea(new Vector2(500, 400), new Vector2(0, 50));
/// }
/// 
/// </summary>
public class ArUcoGuideReadme : MonoBehaviour
{
    [Header("Este script es solo documentación")]
    [TextArea(10, 20)]
    [SerializeField] private string instrucciones = @"
Este script contiene toda la documentación sobre cómo usar el sistema de guía ArUco.

Mira el código fuente de este script para ver las instrucciones completas.

Los logs del sistema aparecen en la consola con el prefijo 🎯

El sistema funciona automáticamente, no necesitas hacer nada manual.
";

    [Header("Estado del Sistema")]
    [SerializeField] private bool systemStatus = true;
    [SerializeField] private ArUcoPlacementGuide guideReference;
    [SerializeField] private SAMSystemController samController;
    [SerializeField] private UIManager uiManager;

    void Start()
    {
        // Verificar que todos los componentes estén configurados
        CheckSystemStatus();
    }

    [ContextMenu("Verificar Estado del Sistema")]
    private void CheckSystemStatus()
    {
        bool allGood = true;
        
        if (ArUcoPlacementGuide.Instance == null)
        {
            Debug.LogWarning("🎯 ArUcoPlacementGuide.Instance no encontrado");
            allGood = false;
        }
        
        if (FindObjectOfType<SAMSystemController>() == null)
        {
            Debug.LogWarning("🎯 SAMSystemController no encontrado en la escena");
            allGood = false;
        }
        
        if (UIManager.Instance == null)
        {
            Debug.LogWarning("🎯 UIManager.Instance no encontrado");
            allGood = false;
        }
        
        if (allGood)
        {
            Debug.Log("🎯 ¡Sistema de guía ArUco completamente configurado!");
        }
        else
        {
            Debug.LogWarning("🎯 El sistema de guía ArUco tiene problemas de configuración");
        }
        
        systemStatus = allGood;
    }

    [ContextMenu("Mostrar Guía de Planificación")]
    private void TestShowPlanningGuide()
    {
        if (ArUcoPlacementGuide.Instance != null)
        {
            ArUcoPlacementGuide.Instance.ShowPlanningGuide();
            Debug.Log("🎯 Prueba: Guía de planificación mostrada");
        }
    }

    [ContextMenu("Mostrar Guía de Combate")]
    private void TestShowCombatGuide()
    {
        if (ArUcoPlacementGuide.Instance != null)
        {
            ArUcoPlacementGuide.Instance.ShowCombatGuide();
            Debug.Log("🎯 Prueba: Guía de combate mostrada");
        }
    }

    [ContextMenu("Ocultar Guía")]
    private void TestHideGuide()
    {
        if (ArUcoPlacementGuide.Instance != null)
        {
            ArUcoPlacementGuide.Instance.HideGuide();
            Debug.Log("🎯 Prueba: Guía ocultada");
        }
    }
}
 