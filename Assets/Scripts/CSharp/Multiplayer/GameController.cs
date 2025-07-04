using UnityEngine;

public class GameController : MonoBehaviour
{
    [Header("System References")]
    [Tooltip("Reference to the SAMSystemController that manages game visuals and local server communication.")]
    [SerializeField] private SAMSystemController samSystemController;
    
    [Tooltip("Reference to the TurretManager to select turrets")]
    [SerializeField] private TurretManager turretManager;

    // Referencias a otros managers para sincronizar estados
    private GameManager gameManager;
    private PlayerBase playerBase;
    private MonsterManager monsterManager;
    
    public enum GamePhase { Planning, Combat }
    private GamePhase currentPhase = GamePhase.Planning;

    void Start()
    {
        if (PhotonManager.Instance == null)
        {
            Debug.LogError("GameController requires a PhotonManager in the scene. Make sure a PhotonManager object exists.");
            gameObject.SetActive(false); // Disable self if not properly configured
            return;
        }

        // Subscribe to events from PhotonManager
        PhotonManager.Instance.OnPhaseChangeReceived += HandlePhaseChange;
        PhotonManager.Instance.OnTurretSelectReceived += HandleTurretSelection;

        if (samSystemController == null)
        {
            Debug.LogError("SAMSystemController is not assigned in the GameController inspector. Please drag the 'SamController' GameObject here.");
        }
        if (turretManager == null)
        {
            Debug.LogError("TurretManager is not assigned in the GameController inspector.");
        }

        // Solo el Master Client (Colocador) debe sincronizar estados del juego
        if (PhotonManager.Instance.IsMasterClient())
        {
            SetupGameStateSync();
        }
    }

    private void SetupGameStateSync()
    {
        // Obtener referencias a los managers del juego
        gameManager = GameManager.Instance;
        playerBase = PlayerBase.Instance;
        monsterManager = FindObjectOfType<MonsterManager>();

        // Suscribirse a eventos del juego para sincronizar con el Selector
        if (gameManager != null)
        {
            GameManager.OnGoldChanged += HandleGoldChanged;
        }

        if (playerBase != null)
        {
            PlayerBase.OnHealthChanged += HandleHealthChanged;
            PlayerBase.OnBaseDestroyed += HandleBaseDestroyed;
        }

        if (monsterManager != null)
        {
            MonsterManager.OnWaveStart += HandleWaveStart;
        }

        // Suscribirse a eventos de monstruos
        Monster.OnMonsterDeath += HandleMonsterDeath;

        // Enviar estado inicial al Selector cuando se conecte
        SendInitialGameState();
    }

    private void SendInitialGameState()
    {
        // Enviar estado inicial del juego al Selector
        if (PhotonManager.Instance != null && PhotonManager.Instance.IsMasterClient())
        {
            if (gameManager != null)
            {
                PhotonManager.Instance.BroadcastGoldUpdate(gameManager.GetCurrentGold());
            }

            if (playerBase != null)
            {
                PhotonManager.Instance.BroadcastHealthUpdate(playerBase.GetCurrentHealth(), playerBase.GetMaxHealth());
            }

            if (monsterManager != null)
            {
                PhotonManager.Instance.BroadcastWaveUpdate(monsterManager.GetCurrentWave());
            }

            // Enviar información de la torreta seleccionada actual
            if (turretManager != null)
            {
                TurretData selectedTurret = turretManager.GetSelectedTurretData();
                if (selectedTurret != null)
                {
                    PhotonManager.Instance.BroadcastTurretInfo(
                        selectedTurret.name, 
                        selectedTurret.cost, 
                        selectedTurret.damage, 
                        selectedTurret.range, 
                        selectedTurret.level
                    );
                }
            }
        }
    }

    private void HandlePhaseChange(string phaseCommand)
    {
        Debug.Log($"[GameController] Received phase command via Photon: {phaseCommand}");
        if (samSystemController != null)
        {
            // Delegate the command to SAMSystemController, which already knows how to handle it.
            samSystemController.SendMessage(phaseCommand);
        }
        else
        {
            Debug.LogError("[GameController] Cannot execute phase command because SAMSystemController is not assigned.");
        }

        // Also handle UI state changes here, based on the original game flow.
        if (UIManager.Instance != null)
        {
            if (phaseCommand == "START_COMBAT")
            {
                UIManager.Instance.SetCombatUIVisibility(true);
            }
            else if (phaseCommand == "STOP_COMBAT")
            {
                UIManager.Instance.SetCombatUIVisibility(false);
            }
        }
        else
        {
            Debug.LogWarning("[GameController] UIManager.Instance not found. Cannot update combat UI visibility.");
        }
    }

    private void HandleTurretSelection(int turretIndex)
    {
        Debug.Log($"[GameController] Received turret selection via Photon: {turretIndex}");
        if (turretManager != null)
        {
            turretManager.SelectTurret(turretIndex);
            
            // Enviar información de la nueva torreta seleccionada al Selector
            if (PhotonManager.Instance.IsMasterClient())
            {
                TurretData selectedTurret = turretManager.GetSelectedTurretData();
                if (selectedTurret != null)
                {
                    PhotonManager.Instance.BroadcastTurretInfo(
                        selectedTurret.name, 
                        selectedTurret.cost, 
                        selectedTurret.damage, 
                        selectedTurret.range, 
                        selectedTurret.level
                    );
                }
            }
        }
        else
        {
             Debug.LogError("[GameController] Cannot select turret because TurretManager is not assigned.");
        }
    }

    // --- MANEJADORES DE EVENTOS DEL JUEGO PARA SINCRONIZACIÓN ---

    private void HandleGoldChanged(int newGoldAmount)
    {
        if (PhotonManager.Instance != null && PhotonManager.Instance.IsMasterClient())
        {
            PhotonManager.Instance.BroadcastGoldUpdate(newGoldAmount);
        }
    }

    private void HandleHealthChanged(int currentHealth, int maxHealth)
    {
        if (PhotonManager.Instance != null && PhotonManager.Instance.IsMasterClient())
        {
            PhotonManager.Instance.BroadcastHealthUpdate(currentHealth, maxHealth);
        }
    }

    private void HandleBaseDestroyed()
    {
        if (PhotonManager.Instance != null && PhotonManager.Instance.IsMasterClient())
        {
            PhotonManager.Instance.BroadcastBaseDestroyed();
        }
    }

    private void HandleWaveStart(int waveNumber)
    {
        if (PhotonManager.Instance != null && PhotonManager.Instance.IsMasterClient())
        {
            PhotonManager.Instance.BroadcastWaveUpdate(waveNumber);
        }
    }

    private void HandleMonsterDeath(Monster monster)
    {
        if (PhotonManager.Instance != null && PhotonManager.Instance.IsMasterClient())
        {
            PhotonManager.Instance.BroadcastMonsterDeath();
        }
    }

    private void OnDestroy()
    {
        // Unsubscribe to prevent memory leaks
        if (PhotonManager.Instance != null)
        {
            PhotonManager.Instance.OnPhaseChangeReceived -= HandlePhaseChange;
            PhotonManager.Instance.OnTurretSelectReceived -= HandleTurretSelection;
        }

        // Desuscribirse de eventos del juego
        if (gameManager != null)
        {
            GameManager.OnGoldChanged -= HandleGoldChanged;
        }

        if (playerBase != null)
        {
            PlayerBase.OnHealthChanged -= HandleHealthChanged;
            PlayerBase.OnBaseDestroyed -= HandleBaseDestroyed;
        }

        if (monsterManager != null)
        {
            MonsterManager.OnWaveStart -= HandleWaveStart;
        }

        Monster.OnMonsterDeath -= HandleMonsterDeath;
    }
} 