using UnityEngine;

public class GameController : MonoBehaviour
{
    [Header("System References")]
    [Tooltip("Reference to the SAMSystemController that manages game visuals and local server communication.")]
    [SerializeField] private SAMSystemController samSystemController;
    
    [Tooltip("Reference to the TurretManager to select turrets")]
    [SerializeField] private TurretManager turretManager;

    // You might need references to other managers like GameManager, UIManager, etc.
    // [SerializeField] private UIManager uiManager;
    
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
    }

    private void HandleTurretSelection(int turretIndex)
    {
        Debug.Log($"[GameController] Received turret selection via Photon: {turretIndex}");
        if (turretManager != null)
        {
            turretManager.SelectTurret(turretIndex);
        }
        else
        {
             Debug.LogError("[GameController] Cannot select turret because TurretManager is not assigned.");
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
    }
} 