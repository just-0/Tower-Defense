using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class TutorialSpawner : MonoBehaviour
{
    [Header("Configuración de Spawn")]
    [SerializeField] private GameObject monsterPrefab;
    [SerializeField] private Transform monsterSpawnPoint;
    [SerializeField] private Transform turretDemoPoint;
    [SerializeField] private float spawnInterval = 2f;

    private GameObject currentDemoTurret;
    private Coroutine spawningCoroutine;
    private List<GameObject> activeMonsters = new List<GameObject>(); // Para registrar los monstruos activos

    public void SpawnTurretForDemo(TurretData turretData)
    {
        if (turretData.turretPrefab != null)
        {
            currentDemoTurret = Instantiate(turretData.turretPrefab, turretDemoPoint.position, turretDemoPoint.rotation);
            
            // Ajustar la escala y rotación para que coincida con el juego principal.
            currentDemoTurret.transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
            currentDemoTurret.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);

            // Desactivar el componente 'Turret' del juego principal para evitar conflictos.
            Turret mainGameTurret = currentDemoTurret.GetComponent<Turret>();
            if (mainGameTurret != null)
            {
                mainGameTurret.enabled = false;
            }

            // Asegurarnos de que la torreta sea una versión de tutorial
            var turret = currentDemoTurret.AddComponent<TutorialTurret>();
            turret.Initialize(turretData);
        }
        else
        {
            Debug.LogError($"El prefab de la torreta '{turretData.turretName}' no está asignado en su archivo .asset. Ve a la carpeta del proyecto, selecciona el asset '{turretData.name}' y asigna el prefab de la torreta.");
        }
    }

    public void DestroyDemoTurret()
    {
        if (currentDemoTurret != null)
        {
            Destroy(currentDemoTurret);
        }
    }

    /// <summary>
    /// Destruye todos los monstruos activos que el spawner haya creado.
    /// </summary>
    public void DestroyAllMonsters()
    {
        // Iteramos sobre una copia de la lista por si se modifica durante la destrucción
        foreach (var monster in activeMonsters)
        {
            if (monster != null)
            {
                Destroy(monster);
            }
        }
        activeMonsters.Clear();
        Debug.Log("[TutorialSpawner] Todos los monstruos activos han sido eliminados.");
    }

    public void StartSpawningMonsters()
    {
        if (spawningCoroutine == null)
        {
            spawningCoroutine = StartCoroutine(SpawnMonsterRoutine());
        }
    }

    public void StopSpawningMonsters()
    {
        if (spawningCoroutine != null)
        {
            StopCoroutine(spawningCoroutine);
            spawningCoroutine = null;
        }
    }

    private IEnumerator SpawnMonsterRoutine()
    {
        while (true)
        {
            if (monsterPrefab != null)
            {
                GameObject monsterGO = Instantiate(monsterPrefab, monsterSpawnPoint.position, monsterSpawnPoint.rotation);
                activeMonsters.Add(monsterGO); // Añadir el nuevo monstruo a la lista
                
                // Intentar obtener el componente, y si no existe, añadirlo.
                // Esta es la lógica que falta y que soluciona el problema.
                TutorialMonster monster = monsterGO.GetComponent<TutorialMonster>();
                if (monster == null)
                {
                    monster = monsterGO.AddComponent<TutorialMonster>();
                }
                
                // Ahora la suscripción al evento siempre funcionará.
                monster.OnMonsterKilled += OnMonsterKilled;
            }
            yield return new WaitForSeconds(spawnInterval);
        }
    }
    
    private void OnMonsterKilled()
    {
        // El contador ya no se usa, pero el método debe existir para la suscripción al evento.
        // Se podría eliminar si también se quita la suscripción del evento.
    }
} 