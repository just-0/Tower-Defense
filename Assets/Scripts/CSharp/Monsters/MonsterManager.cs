using UnityEngine;
using System.Collections.Generic;
using System.Collections;

// Clase para definir un grupo de monstruos dentro de una misma oleada
[System.Serializable]
public class SubWave
{
    public GameObject monsterPrefab;
    public int count;
    [Tooltip("Tiempo de espera antes de que comience esta sub-oleada (después de la anterior).")]
    public float delay;
}

// Clase para definir una oleada completa, compuesta por una o más sub-oleadas
[System.Serializable]
public class Wave
{
    public string waveName; // Opcional, para identificar la oleada en el editor
    public List<SubWave> subWaves;
}

public class MonsterManager : MonoBehaviour
{
    [Header("Configuración de Monstruos")]
    [Tooltip("Arrastra aquí los prefabs de todos los tipos de monstruos.")]
    public List<GameObject> monsterPrefabs;

    [Header("Configuración de Oleadas")]
    [Tooltip("Define la secuencia de oleadas. El juego ciclará a través de esta lista.")]
    public List<Wave> waveDefinitions;
    [SerializeField] private float timeBetweenWaves = 10f;
    
    [Header("Escalado de Dificultad")]
    [Tooltip("Cada 5 oleadas, la vida de los monstruos se multiplicará por este valor.")]
    [SerializeField] private float healthMultiplierPerTier = 1.5f;
    [Tooltip("Cada 5 oleadas, la velocidad de los monstruos se multiplicará por este valor.")]
    [SerializeField] private float speedMultiplierPerTier = 1.1f;

    [Header("Path del A*")]
    private List<Vector3> currentPath;
    private bool hasValidPath = false;
    
    [Header("Control de Oleadas")]
    private int currentWaveNumber = 0; // El número de oleada que ve el jugador
    private int waveDefinitionIndex = 0; // El índice para la lista `waveDefinitions`
    private int difficultyTier = 0; // El nivel de escalado de dificultad
    private bool waveInProgress = false;
    private int monstersAlive = 0;
    
    private List<Monster> activeMonsters = new List<Monster>();
    
    public delegate void WaveEvent(int waveNumber);
    public static event WaveEvent OnWaveStart;
    public static event WaveEvent OnWaveComplete;
    
    void Start()
    {
        Monster.OnMonsterDeath += OnMonsterDied;
        Monster.OnMonsterReachedEnd += OnMonsterReachedEnd;
    }
    
    void OnDestroy()
    {
        Monster.OnMonsterDeath -= OnMonsterDied;
        Monster.OnMonsterReachedEnd -= OnMonsterReachedEnd;
    }
    
    public void SetPath(List<Vector3> newPath)
    {
        currentPath = new List<Vector3>(newPath);
        hasValidPath = currentPath != null && currentPath.Count > 0;
        
        if (hasValidPath && !waveInProgress)
        {
            StartCoroutine(StartWaveAfterDelay(2f));
        }
    }
    
    private IEnumerator StartWaveAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        StartNextWave();
    }
    
    public void StartNextWave()
    {
        if (!hasValidPath || waveInProgress || waveDefinitions.Count == 0) return;
        
        waveInProgress = true;
        currentWaveNumber++;
        
        //Debug.Log($"Iniciando oleada {currentWaveNumber} (Definición: {waveDefinitions[waveDefinitionIndex].waveName}, Tier: {difficultyTier})");
        OnWaveStart?.Invoke(currentWaveNumber);
        
        StartCoroutine(SpawnWave(waveDefinitions[waveDefinitionIndex]));
    }
    
    private IEnumerator SpawnWave(Wave wave)
    {
        monstersAlive = 0;
        foreach(var subWave in wave.subWaves)
        {
            monstersAlive += subWave.count;
        }

        foreach (var subWave in wave.subWaves)
        {
            yield return new WaitForSeconds(subWave.delay);
            for (int i = 0; i < subWave.count; i++)
            {
                SpawnMonster(subWave.monsterPrefab);
                yield return new WaitForSeconds(1f); // Tiempo entre monstruos de la misma sub-oleada
            }
        }
    }
    
    private void SpawnMonster(GameObject monsterPrefab)
    {
        if (monsterPrefab == null || !hasValidPath) return;
        
        GameObject monsterObj = Instantiate(monsterPrefab, currentPath[0], Quaternion.identity);
        Monster monster = monsterObj.GetComponent<Monster>();
        if (monster == null) monster = monsterObj.AddComponent<Monster>();
        
        ConfigureMonsterStats(monster);
        monster.SetPath(currentPath);
        activeMonsters.Add(monster);
    }
    
    private void ConfigureMonsterStats(Monster monster)
    {
        if (difficultyTier == 0) return; // Sin cambios en el primer ciclo

        float healthMultiplier = Mathf.Pow(healthMultiplierPerTier, difficultyTier);
        float speedMultiplier = Mathf.Pow(speedMultiplierPerTier, difficultyTier);
        
        monster.maxHealth *= healthMultiplier;
        monster.currentHealth = monster.maxHealth;
        monster.moveSpeed *= speedMultiplier;
        // La recompensa de oro podría escalar también si se desea
        // monster.goldReward = (int)(monster.goldReward * (1 + difficultyTier * 0.5f));
    }
    
    private void OnMonsterDied(Monster monster)
    {
        RemoveMonster(monster);
    }
    
    private void OnMonsterReachedEnd(Monster monster)
    {
        if (PlayerBase.Instance != null)
        {
            if (monster.damageToBase > 0)
            {
                PlayerBase.Instance.TakeDamage(monster.damageToBase);
            }
            else
            {
                //Debug.LogWarning($"El monstruo {monster.name} llegó al final pero tiene 0 de daño a la base (damageToBase). Revisa el prefab del monstruo en el editor de Unity.");
            }
        }
        else
        {
            //Debug.LogWarning("OnMonsterReachedEnd: No se encontró una instancia de PlayerBase para aplicar daño.");
        }
        RemoveMonster(monster);
    }
    
    private void RemoveMonster(Monster monster)
    {
        if (activeMonsters.Contains(monster))
        {
            activeMonsters.Remove(monster);
        }
        
        monstersAlive--;
        monstersAlive = Mathf.Max(0, monstersAlive);
        
        if (monstersAlive <= 0 && waveInProgress)
        {
            CompleteWave();
        }
    }
    
    private void CompleteWave()
    {
        waveInProgress = false;
        OnWaveComplete?.Invoke(currentWaveNumber);
        
        // Avanzar al siguiente ciclo de oleadas
        waveDefinitionIndex++;
        if (waveDefinitionIndex >= waveDefinitions.Count)
        {
            waveDefinitionIndex = 0; // Reiniciar el ciclo
            difficultyTier++; // Aumentar la dificultad para el próximo ciclo
            //Debug.Log($"<color=magenta>¡Ciclo de oleadas completado! Nuevo Tier de Dificultad: {difficultyTier}</color>");
        }
        
        StartCoroutine(StartNextWaveAfterDelay());
    }
    
    private IEnumerator StartNextWaveAfterDelay()
    {
        yield return new WaitForSeconds(timeBetweenWaves);
        if (hasValidPath)
        {
            StartNextWave();
        }
    }
    
    public void StopAllWaves()
    {
        StopAllCoroutines();
        waveInProgress = false;
        
        foreach (Monster monster in activeMonsters)
        {
            if (monster != null)
            {
                Destroy(monster.gameObject);
            }
        }
        
        activeMonsters.Clear();
        monstersAlive = 0;
    }
    
    public void ResetWaves()
    {
        StopAllWaves();
        currentWaveNumber = 0;
        waveDefinitionIndex = 0;
        difficultyTier = 0;
        hasValidPath = false;
        currentPath = null;
    }
    
    public int GetCurrentWave() => currentWaveNumber;
    public int GetMonstersAlive() => monstersAlive;
    public bool IsWaveInProgress() => waveInProgress;
    public List<Monster> GetActiveMonsters() => new List<Monster>(activeMonsters);
} 