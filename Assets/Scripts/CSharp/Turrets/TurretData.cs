using UnityEngine;

[CreateAssetMenu(fileName = "NewTurretData", menuName = "TowerDefense/Turret Data")]
public class TurretData : ScriptableObject
{
    [Header("General Attributes")]
    public string turretName = "Default Turret";
    public GameObject turretPrefab;
    public GameObject projectilePrefab;
    public int cost = 100;
    public int level = 1;

    [Header("Combat Attributes")]
    public float fireRate = 1f; // Disparos por segundo
    public float range = 10f;   // Rango de ataque
    public float damage = 20f;

    [Header("Visuals & SFX - Opcional")]
    public ParticleSystem placementEffect;
    public AudioClip placementSound;
    public ParticleSystem firingEffect;
    public AudioClip firingSound;
} 