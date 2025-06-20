using UnityEngine;
using System.Collections.Generic;

public class TurretManager : MonoBehaviour
{
    public static TurretManager Instance { get; private set; }

    [Header("Turret Settings")]
    [Tooltip("La lista de todos los tipos de torretas disponibles en el juego.")]
    public List<TurretData> turretDatas; // Lista de todos los tipos de torretas

    [SerializeField] private Transform turretsParent; // Opcional: para organizar las torretas en la jerarquía

    // El límite de torretas por oleada ha sido eliminado.
    // [Header("Placement Rules")]
    // [SerializeField] private int maxTurretsPerWave = 2;
    // private int turretsPlacedThisWave = 0;

    private int selectedTurretIndex = 0; // La torreta seleccionada por defecto es la primera de la lista

    // Referencia al MonsterManager para saber cuándo empieza una oleada
    private MonsterManager monsterManager;

    // Lista de torretas activas (opcional, para gestión)
    private List<Turret> activeTurrets = new List<Turret>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (turretsParent == null)
        {
            GameObject parentGO = new GameObject("ActiveTurrets");
            turretsParent = parentGO.transform;
        }
    }

    void Start()
    {
        monsterManager = FindObjectOfType<MonsterManager>();
        if (monsterManager != null)
        {
            // Ya no necesitamos suscribirnos a este evento para resetear el contador.
            // MonsterManager.OnWaveStart += HandleWaveStart;
        }
        else
        {
            //Debug.LogError("TurretManager no pudo encontrar a MonsterManager en la escena.");
        }
    }

    void OnDestroy()
    {
        if (monsterManager != null)
        {
            // MonsterManager.OnWaveStart -= HandleWaveStart;
        }
    }
    
    // El handler para el inicio de la oleada ya no es necesario.
    // private void HandleWaveStart(int waveNumber)
    // {
    //     turretsPlacedThisWave = 0;
    //     //Debug.Log($"Oleada {waveNumber} iniciada. Se pueden colocar {maxTurretsPerWave} torretas.");
    // }

    /// <summary>
    /// Selecciona el tipo de torreta a colocar. El índice corresponde a la lista `turretDatas`.
    /// </summary>
    /// <param name="index">Índice de la torreta en la lista `turretDatas`.</param>
    public void SelectTurret(int index)
    {
        if (index >= 0 && index < turretDatas.Count)
        {
            selectedTurretIndex = index;
            //Debug.Log($"Torreta seleccionada: {turretDatas[selectedTurretIndex].turretName}");
            
            if(UIManager.Instance != null)
            {
                UIManager.Instance.UpdateSelectedTurretUI(turretDatas[selectedTurretIndex]);
            }
        }
        else
        {
            //Debug.LogWarning($"Índice de torreta ({index}) fuera de rango.");
        }
    }

    /// <summary>
    /// Coloca la torreta actualmente seleccionada en la posición dada.
    /// </summary>
    /// <param name="worldPosition">La posición en el mundo donde se colocará la torreta.</param>
    public void PlaceSelectedTurret(Vector3 worldPosition)
    {
        if (turretDatas == null || turretDatas.Count == 0)
        {
            //Debug.LogError("TurretManager: No hay datos de torretas asignados en la lista.");
            return;
        }

        if (selectedTurretIndex < 0 || selectedTurretIndex >= turretDatas.Count)
        {
            //Debug.LogError("TurretManager: El índice de la torreta seleccionada es inválido.");
            return;
        }

        PlaceTurret(turretDatas[selectedTurretIndex], worldPosition);
    }

    /// <summary>
    /// Coloca una torreta del tipo especificado en la posición dada.
    /// </summary>
    /// <param name="turretType">Los datos de la torreta a colocar.</param>
    /// <param name="worldPosition">La posición en el mundo donde se colocará la torreta.</param>
    private void PlaceTurret(TurretData turretType, Vector3 worldPosition)
    {
        if (turretType == null)
        {
            //Debug.LogError("TurretManager: TurretData es nulo. No se puede colocar la torreta.");
            return;
        }
        
        // Lógica de límite de torretas eliminada.
        // if (turretsPlacedThisWave >= maxTurretsPerWave)
        // {
        //     //Debug.LogWarning($"Límite de torretas ({maxTurretsPerWave}) para esta oleada alcanzado.");
        //     return;
        // }

        if (GameManager.Instance == null || !GameManager.Instance.SpendGold(turretType.cost))
        {
            //Debug.LogWarning($"No hay suficiente oro para la torreta {turretType.turretName}.");
            UIManager.Instance?.FlashGoldText(); 
            return;
        }

        if (turretType.turretPrefab == null)
        {
            //Debug.LogError($"TurretManager: El prefab para {turretType.turretName} es nulo.");
            GameManager.Instance.AddGold(turretType.cost);
            return;
        }

        //Debug.Log($"Colocando torreta {turretType.turretName} en {worldPosition}");

        GameObject turretGO = Instantiate(turretType.turretPrefab, worldPosition, Quaternion.identity, turretsParent);
        
        turretGO.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
        turretGO.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        Turret turretComponent = turretGO.GetComponent<Turret>();

        if (turretComponent != null)
        {
            turretComponent.Initialize(turretType);
            activeTurrets.Add(turretComponent);
            
            // Lógica de conteo de torretas eliminada.
            // turretsPlacedThisWave++;
            // //Debug.Log($"Torretas colocadas en esta oleada: {turretsPlacedThisWave}/{maxTurretsPerWave}");

            if (turretType.placementEffect != null)
            {
                Instantiate(turretType.placementEffect, worldPosition, Quaternion.identity);
            }
            if (turretType.placementSound != null)
            {
                AudioSource.PlayClipAtPoint(turretType.placementSound, worldPosition);
            }
        }
        else
        {
            //Debug.LogError($"El prefab de torreta {turretType.turretName} no tiene un componente Turret adjunto.");
            Destroy(turretGO); 
            GameManager.Instance.AddGold(turretType.cost);
        }
    }

    /// <summary>
    /// Devuelve los datos de la torreta actualmente seleccionada.
    /// </summary>
    /// <returns>El ScriptableObject TurretData de la torreta seleccionada, o null si no hay ninguna.</returns>
    public TurretData GetSelectedTurretData()
    {
        if (turretDatas == null || turretDatas.Count == 0 || selectedTurretIndex < 0 || selectedTurretIndex >= turretDatas.Count)
        {
            return null;
        }
        return turretDatas[selectedTurretIndex];
    }

    // Futuras expansiones:
    // - Seleccionar tipo de torreta basado en gestos/UI
    // - Vender/Mejorar torretas
    // - Límites de torretas
} 