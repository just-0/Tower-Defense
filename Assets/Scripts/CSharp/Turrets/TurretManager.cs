using UnityEngine;
using System.Collections.Generic;

public class TurretManager : MonoBehaviour
{
    public static TurretManager Instance { get; private set; }

    [Header("Turret Settings")]
    [Tooltip("El tipo de torreta por defecto que se colocará. Puedes expandir esto para seleccionar diferentes tipos.")]
    public TurretData defaultTurretData; // Podrías tener una lista y seleccionar por tipo/dedos

    [SerializeField] private Transform turretsParent; // Opcional: para organizar las torretas en la jerarquía

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

    /// <summary>
    /// Coloca una torreta del tipo especificado en la posición dada.
    /// </summary>
    /// <param name="turretType">Los datos de la torreta a colocar.</param>
    /// <param name="worldPosition">La posición en el mundo donde se colocará la torreta.</param>
    public void PlaceTurret(TurretData turretType, Vector3 worldPosition)
    {
        if (turretType == null)
        {
            Debug.LogError("TurretManager: TurretData es nulo. No se puede colocar la torreta.");
            return;
        }
        if (turretType.turretPrefab == null)
        {
            Debug.LogError($"TurretManager: El prefab para {turretType.turretName} es nulo.");
            return;
        }

        // Aquí podrías añadir lógica de coste, si el jugador tiene suficientes recursos, etc.
        // Ejemplo: if (PlayerResources.Instance.CanAfford(turretType.cost)) { ... }

        Debug.Log($"Colocando torreta {turretType.turretName} en {worldPosition}");

        GameObject turretGO = Instantiate(turretType.turretPrefab, worldPosition, Quaternion.identity, turretsParent);
        
        // Ajustar la escala y rotación inicial de la torreta instanciada
        turretGO.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
        turretGO.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

        Turret turretComponent = turretGO.GetComponent<Turret>();

        if (turretComponent != null)
        {
            turretComponent.Initialize(turretType);
            activeTurrets.Add(turretComponent);

            // Efectos de colocación
            if (turretType.placementEffect != null)
            {
                Instantiate(turretType.placementEffect, worldPosition, Quaternion.identity);
            }
            if (turretType.placementSound != null)
            {
                // Necesitarías un AudioSource o un gestor de audio
                AudioSource.PlayClipAtPoint(turretType.placementSound, worldPosition);
            }
        }
        else
        {
            Debug.LogError($"El prefab de torreta {turretType.turretName} no tiene un componente Turret adjunto.");
            Destroy(turretGO); // Limpiar si no se pudo inicializar
        }
    }

    /// <summary>
    /// Método de conveniencia para colocar la torreta por defecto.
    /// Este es el método que SAMSystemController podría llamar.
    /// </summary>
    /// <param name="worldPosition">La posición en el mundo donde se colocará la torreta.</param>
    public void PlaceDefaultTurret(Vector3 worldPosition)
    {
        if (defaultTurretData == null)
        {
            Debug.LogError("TurretManager: No se ha asignado defaultTurretData en el Inspector.");
            return;
        }
        PlaceTurret(defaultTurretData, worldPosition);
    }

    // Futuras expansiones:
    // - Seleccionar tipo de torreta basado en gestos/UI
    // - Vender/Mejorar torretas
    // - Límites de torretas
} 