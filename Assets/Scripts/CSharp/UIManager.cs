using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("UI Containers")]
    [Tooltip("Arrastra aquí el objeto que contiene toda la UI del modo combate (textos de oro, oleada, etc.).")]
    [SerializeField] private GameObject combatUIContainer;

    [Header("UI Elements")]
    [SerializeField] private Text goldText;
    [SerializeField] private Text waveText;
    [SerializeField] private Text baseHealthText;
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private Text selectedTurretText;

    [Header("Feedback Settings")]
    [SerializeField] private Color goldFlashColor = Color.red;
    [SerializeField] private float goldFlashDuration = 0.5f;
    private Color originalGoldColor;
    private Coroutine goldFlashCoroutine;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Start()
    {
        // Suscribirse a eventos
        GameManager.OnGoldChanged += UpdateGoldUI;
        PlayerBase.OnHealthChanged += UpdateBaseHealthUI;
        PlayerBase.OnBaseDestroyed += ShowGameOverScreen;
        MonsterManager.OnWaveStart += UpdateWaveUI;
        
        // Inicializar UI
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (combatUIContainer != null) combatUIContainer.SetActive(false); // Ocultar al inicio

        // Forzar actualización inicial
        if(GameManager.Instance != null)
            UpdateGoldUI(GameManager.Instance.GetCurrentGold());

        if (PlayerBase.Instance != null)
            UpdateBaseHealthUI(PlayerBase.Instance.GetCurrentHealth(), PlayerBase.Instance.GetMaxHealth());

        if (FindObjectOfType<MonsterManager>() != null)
            UpdateWaveUI(FindObjectOfType<MonsterManager>().GetCurrentWave());

        if (selectedTurretText != null)
        {
            selectedTurretText.text = "Torreta: Ninguna";
            originalGoldColor = goldText.color;
        }
    }

    void OnDestroy()
    {
        // Desuscribirse
        GameManager.OnGoldChanged -= UpdateGoldUI;
        PlayerBase.OnHealthChanged -= UpdateBaseHealthUI;
        PlayerBase.OnBaseDestroyed -= ShowGameOverScreen;
        MonsterManager.OnWaveStart -= UpdateWaveUI;
    }
    
    public void SetCombatUIVisibility(bool isVisible)
    {
        if (combatUIContainer != null)
        {
            combatUIContainer.SetActive(isVisible);
        }
    }

    private void UpdateGoldUI(int newGoldAmount)
    {
        if (goldText != null)
        {
            goldText.text = $"Oro: {newGoldAmount}";
        }
    }

    private void UpdateWaveUI(int newWaveNumber)
    {
        if (waveText != null)
        {
            waveText.text = newWaveNumber > 0 ? $"Oleada: {newWaveNumber}" : "Preparando...";
        }
    }

    private void UpdateBaseHealthUI(int currentHealth, int maxHealth)
    {
        if (baseHealthText != null)
        {
            baseHealthText.text = $"Base: {currentHealth} / {maxHealth}";
        }
    }

    public void UpdateSelectedTurretUI(TurretData turretData)
    {
        if (selectedTurretText != null && turretData != null)
        {
            selectedTurretText.text = $"Seleccionada: {turretData.turretName} (Costo: {turretData.cost})";
        }
    }

    public void FlashGoldText()
    {
        if (goldFlashCoroutine != null)
        {
            StopCoroutine(goldFlashCoroutine);
        }
        goldFlashCoroutine = StartCoroutine(FlashGoldCoroutine());
    }

    private IEnumerator FlashGoldCoroutine()
    {
        if (goldText == null) yield break;

        goldText.color = goldFlashColor;
        yield return new WaitForSeconds(goldFlashDuration);
        goldText.color = originalGoldColor;
    }

    private void ShowGameOverScreen()
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }
    }
} 