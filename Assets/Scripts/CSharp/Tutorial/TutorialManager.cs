using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Collections;
using System.Collections.Generic;

// El enum de acciones se simplifica
public enum PlayerAction
{
    None,           // El paso avanza automáticamente
    Continue,       // El jugador debe usar el gesto de "Siguiente"
    TestAllWeapons, // El jugador debe seleccionar y probar todas las armas contra enemigos
    Exit            // El jugador debe usar el gesto de "Salir"
}

// Un objeto que contiene toda la información para un paso del tutorial
[System.Serializable]
public class TutorialStep
{
    [TextArea(3, 10)]
    public string dialogueText;
    public PlayerAction requiredAction = PlayerAction.Continue;
    // La variable de enemigos a derrotar ya no es necesaria
    // public int enemiesToDefeat = 3; 

    [Header("UI Elements to Activate for this step")]
    public bool showWeaponIconsPanel = false;
    public bool showGoldDisplayPanel = false;

    [Header("Gesture Groups to Activate for this step")]
    public GameObject gesture_Continue;
    public GameObject gesture_SelectWeapon;
    public GameObject gesture_Exit;
}

public class TutorialManager : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("El componente de Texto donde aparecerá el diálogo.")]
    [SerializeField] private Text dialogueText;
    [Tooltip("El panel que contiene la UI del diálogo.")]
    [SerializeField] private GameObject dialoguePanel;
    [SerializeField] private GameObject weaponIconsPanel; // El GameObject "PanelArmas"
    [SerializeField] private List<Image> weaponIconImages; // Arrastra aquí las imágenes de Arma1, Arma2, Arma3
    [SerializeField] private GameObject goldDisplayPanel; // Panel que muestra el oro

    [Header("Selection Animation Settings")]
    [SerializeField] private Vector3 selectedIconScale = new Vector3(1.2f, 1.2f, 1.2f);
    [SerializeField] private float unselectedIconAlpha = 0.5f;

    [Header("Tutorial Content")]
    [Tooltip("Configura aquí los pasos del tutorial.")]
    [SerializeField] private List<TutorialStep> tutorialSteps;
    [SerializeField] private List<TurretData> tutorialTurrets;

    [Header("Scene References")]
    [SerializeField] private TutorialSpawner spawner;

    [Header("Persistent Gestures")]
    [Tooltip("Un gesto que estará activo durante todo el tutorial, como el de Salir.")]
    [SerializeField] private GameObject persistent_ExitGesture;

    [Header("Text Effect Settings")]
    [Tooltip("La velocidad a la que aparece el texto (segundos por caracter).")]
    [SerializeField] private float typingSpeed = 0.05f;
    [Tooltip("La velocidad del texto al hacer avance rápido.")]
    [SerializeField] private float fastTypingSpeed = 0.01f;

    // --- ESTADO INTERNO ---
    private int currentStepIndex = 0;
    private bool isTyping = false;
    private bool isFastForwarding = false;
    private bool waitingForPlayerAction = false;
    private Coroutine typingCoroutine;
    private HashSet<int> selectedWeaponsTracker = new HashSet<int>();
    private bool isCompletingWeaponTest = false; // Flag para evitar que la corutina de fin de paso se llame múltiples veces

    void Start()
    {
        if (dialogueText == null || dialoguePanel == null || spawner == null || tutorialSteps.Count == 0)
        {
            Debug.LogError("Faltan referencias críticas (UI, Spawner o Pasos) en el TutorialManager. Desactivando.");
            gameObject.SetActive(false);
            return;
        }

        // Activar el gesto de salida persistente una sola vez al inicio.
        if (persistent_ExitGesture != null)
        {
            persistent_ExitGesture.SetActive(true);
        }

        StartCoroutine(TutorialFlow());
    }
    
    private IEnumerator TutorialFlow()
    {
        Debug.Log("--- INICIANDO FLUJO DEL TUTORIAL ---");
        currentStepIndex = 0;
        while (currentStepIndex < tutorialSteps.Count)
        {
            TutorialStep currentStep = tutorialSteps[currentStepIndex];
            Debug.Log($"[TutorialFlow] Iniciando Paso {currentStepIndex}: {currentStep.requiredAction}");
            
            SetupUIForStep(currentStep);

            if (typingCoroutine != null) StopCoroutine(typingCoroutine);
            typingCoroutine = StartCoroutine(TypeText(currentStep.dialogueText));

            waitingForPlayerAction = true;
            Debug.Log("[TutorialFlow] Ejecutando HandleStepAction...");
            yield return HandleStepAction(currentStep); // No necesita esperar aquí, pero lo mantenemos por consistencia
            Debug.Log("[TutorialFlow] HandleStepAction ejecutado.");

            Debug.Log("[TutorialFlow] Esperando a que la acción del jugador se complete (waitingForPlayerAction == false)...");
            yield return new WaitUntil(() => !waitingForPlayerAction);

            // Lógica de limpieza al completar un paso
            if (currentStep.requiredAction == PlayerAction.TestAllWeapons)
            {
                Debug.Log("[TutorialFlow] Limpiando después del paso TestAllWeapons (parando monstruos y torreta).");
                spawner.StopSpawningMonsters();
                spawner.DestroyDemoTurret();
                spawner.DestroyAllMonsters();
            }

            Debug.Log("[TutorialFlow] ¡Acción completada! Avanzando al siguiente paso.");
            
            currentStepIndex++;
        }

        dialogueText.text = "¡Tutorial completado!";
        Debug.Log("TUTORIAL FINALIZADO");
    }
    
    private void SetupUIForStep(TutorialStep step)
    {
        // Desactivar todo primero para un estado limpio
        weaponIconsPanel.SetActive(false);
        if (goldDisplayPanel != null) goldDisplayPanel.SetActive(false);
        foreach (var s in tutorialSteps)
        {
            if(s.gesture_Continue) s.gesture_Continue.SetActive(false);
            if(s.gesture_SelectWeapon) s.gesture_SelectWeapon.SetActive(false);
            if(s.gesture_Exit) s.gesture_Exit.SetActive(false);
        }

        // Activar lo necesario para el paso actual
        if (step.showWeaponIconsPanel) weaponIconsPanel.SetActive(true);
        if (step.showGoldDisplayPanel && goldDisplayPanel != null) goldDisplayPanel.SetActive(true);
        
        if (step.gesture_Continue) step.gesture_Continue.SetActive(true);
        if (step.gesture_SelectWeapon) step.gesture_SelectWeapon.SetActive(true);
        if (step.gesture_Exit) step.gesture_Exit.SetActive(true);
    }
    
    private IEnumerator HandleStepAction(TutorialStep step)
    {
        Debug.Log($"[HandleStepAction] Ejecutando acción: {step.requiredAction}");
        switch (step.requiredAction)
        {
            case PlayerAction.TestAllWeapons:
                ResetWeaponIconStates();
                selectedWeaponsTracker.Clear();
                isCompletingWeaponTest = false; // Resetear el flag al iniciar el paso
                spawner.StartSpawningMonsters();
                // La acción la completa OnWeaponSelected
                break;

            case PlayerAction.Continue:
            case PlayerAction.Exit:
                 // La acción la completan los métodos OnContinue / OnExitTutorial
                break;
            case PlayerAction.None:
            default:
                waitingForPlayerAction = false;
                break;
        }
        yield return null; // La corrutina debe devolver algo
    }

    private IEnumerator TypeText(string text)
    {
        isTyping = true;
        dialogueText.text = "";
        foreach (char letter in text.ToCharArray())
        {
            dialogueText.text += letter;
            yield return new WaitForSeconds(isFastForwarding ? fastTypingSpeed : typingSpeed);
        }
        isTyping = false;
        isFastForwarding = false;
        typingCoroutine = null;
    }

    // --- MÉTODOS PÚBLICOS PARA SER LLAMADOS POR LOS GESTOS ---

    public void OnContinue()
    {
        if (currentStepIndex >= tutorialSteps.Count) return;
        TutorialStep currentStep = tutorialSteps[currentStepIndex];
        if (waitingForPlayerAction && currentStep.requiredAction == PlayerAction.Continue)
        {
            if (isTyping)
            {
                SpeedUpTyping();
            }
            else
            {
                waitingForPlayerAction = false;
            }
        }
    }

    public void OnWeaponSelected(int weaponIndex)
    {
        if (currentStepIndex >= tutorialSteps.Count) return;
        TutorialStep currentStep = tutorialSteps[currentStepIndex];
        // Ahora la acción es TestAllWeapons
        if (waitingForPlayerAction && currentStep.requiredAction == PlayerAction.TestAllWeapons)
        {
            if (weaponIndex < 0 || weaponIndex >= tutorialTurrets.Count) return;

            // Colocar la torreta seleccionada, destruyendo la anterior
            spawner.DestroyDemoTurret();
            spawner.SpawnTurretForDemo(tutorialTurrets[weaponIndex]);

            // Animar los iconos
            for (int i = 0; i < weaponIconImages.Count; i++)
            {
                Image icon = weaponIconImages[i];
                if (i == weaponIndex)
                {
                    // El icono seleccionado es de tamaño normal y opaco.
                    icon.rectTransform.localScale = Vector3.one;
                    var tempColor = icon.color;
                    tempColor.a = 1f;
                    icon.color = tempColor;
                }
                else
                {
                    // Los iconos no seleccionados son más pequeños y semi-transparentes.
                    icon.rectTransform.localScale = selectedIconScale;
                    var tempColor = icon.color;
                    tempColor.a = unselectedIconAlpha;
                    icon.color = tempColor;
                }
            }

            // Registrar que esta arma ya fue seleccionada
            if (!selectedWeaponsTracker.Contains(weaponIndex))
            {
                selectedWeaponsTracker.Add(weaponIndex);
            }

            // Comprobar si ya se seleccionaron todas
            if (selectedWeaponsTracker.Count >= tutorialTurrets.Count && !isCompletingWeaponTest)
            {
                // En lugar de terminar inmediatamente, iniciamos una corutina que dará tiempo al jugador
                isCompletingWeaponTest = true;
                StartCoroutine(CompleteWeaponTestAfterDelay(5f));
            }
        }
    }
    
    /// <summary>
    /// Espera un tiempo después de que se han probado todas las armas antes de avanzar.
    /// </summary>
    private IEnumerator CompleteWeaponTestAfterDelay(float delay)
    {
        Debug.Log($"[TutorialManager] Todas las armas probadas. El tutorial avanzará en {delay} segundos.");
        yield return new WaitForSeconds(delay);
        waitingForPlayerAction = false;
    }

    public void OnExitTutorial()
    {
        // Comprobación de seguridad robusta. Si el objeto está destruido o inactivo (por cambio de escena),
        // ignora la llamada para prevenir el error.
        if (this == null || !gameObject.activeInHierarchy)
        {
            Debug.LogWarning("OnExitTutorial fue llamado en una instancia inválida de TutorialManager (probablemente desde otra escena). La llamada fue ignorada.");
            return;
        }

        // Limpiar cualquier estado del tutorial que pueda interferir
        StopAllCoroutines();
        
        // Resetear el MenuGestureController para asegurar reconexión limpia
        Debug.Log("Saliendo del tutorial y volviendo al menú principal...");
        Debug.Log("Reseteando MenuGestureController para evitar problemas de conexión...");
        
        if (MenuGestureController.Instance != null)
        {
            // Forzar reset del estado antes de ir al menú
            MenuGestureController.Instance.ResetAndReconnect();
        }
        
        SceneManager.LoadScene("1_MainMenu");
    }

    private void ResetWeaponIconStates()
    {
        // Al inicio del paso, todos los iconos aparecen como 'no seleccionados'.
        foreach (var icon in weaponIconImages)
        {
            icon.rectTransform.localScale = selectedIconScale;
            var tempColor = icon.color;
            tempColor.a = unselectedIconAlpha;
            icon.color = tempColor;
        }
    }

    /// <summary>
    /// Activa o desactiva el modo de avance rápido para el texto.
    /// </summary>
    public void SetFastForward(bool fast)
    {
        isFastForwarding = fast;
    }

    /// <summary>
    /// Completa instantáneamente el texto que se está escribiendo, si lo hay.
    /// Ideal para un gesto de "avance rápido".
    /// </summary>
    public void SpeedUpTyping()
    {
        if (isTyping && typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            dialogueText.text = tutorialSteps[currentStepIndex].dialogueText;
            isTyping = false;
            typingCoroutine = null;
        }
    }
} 