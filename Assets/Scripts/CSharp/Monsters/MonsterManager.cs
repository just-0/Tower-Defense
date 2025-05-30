using UnityEngine;
using System.Collections.Generic;
using System.Collections;

public class MonsterManager : MonoBehaviour
{
    [Header("Prefabs de Monstruos")]
    [SerializeField] private GameObject monsterPrefab;
    
    [Header("Configuración de Oleadas")]
    [SerializeField] private int monstersPerWave = 5;
    [SerializeField] private float timeBetweenMonsters = 3f;
    [SerializeField] private float timeBetweenWaves = 10f;
    
    [Header("Path del A*")]
    private List<Vector3> currentPath;
    private bool hasValidPath = false;
    
    [Header("Control de Oleadas")]
    private int currentWave = 0;
    private bool waveInProgress = false;
    private int monstersAlive = 0;
    
    // Lista de monstruos activos
    private List<Monster> activeMonsters = new List<Monster>();
    
    // Eventos
    public delegate void WaveEvent(int waveNumber);
    public static event WaveEvent OnWaveStart;
    public static event WaveEvent OnWaveComplete;
    
    void Start()
    {
        // Suscribirse a eventos de monstruos
        Monster.OnMonsterDeath += OnMonsterDied;
        Monster.OnMonsterReachedEnd += OnMonsterReachedEnd;
    }
    
    void OnDestroy()
    {
        // Desuscribirse de eventos
        Monster.OnMonsterDeath -= OnMonsterDied;
        Monster.OnMonsterReachedEnd -= OnMonsterReachedEnd;
    }
    
    public void SetPath(List<Vector3> newPath)
    {
        currentPath = new List<Vector3>(newPath);
        hasValidPath = currentPath != null && currentPath.Count > 0;
        
        Debug.Log($"MonsterManager: Path actualizado con {currentPath.Count} puntos");
        
        // Si tenemos un path válido y no hay oleada en progreso, iniciar la primera oleada
        if (hasValidPath && !waveInProgress)
        {
            StartCoroutine(StartWaveAfterDelay(2f)); // Pequeño delay antes de empezar
        }
    }
    
    private IEnumerator StartWaveAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        StartNextWave();
    }
    
    public void StartNextWave()
    {
        if (!hasValidPath || waveInProgress) return;
        
        currentWave++;
        waveInProgress = true;
        
        Debug.Log($"Iniciando oleada {currentWave}");
        OnWaveStart?.Invoke(currentWave);
        
        StartCoroutine(SpawnWave());
    }
    
    private IEnumerator SpawnWave()
    {
        int monstersToSpawn = monstersPerWave + (currentWave - 1); // Más monstruos cada oleada
        
        for (int i = 0; i < monstersToSpawn; i++)
        {
            SpawnMonster();
            yield return new WaitForSeconds(timeBetweenMonsters);
        }
        
        Debug.Log($"Todos los monstruos de la oleada {currentWave} han sido generados");
    }
    
    private void SpawnMonster()
    {
        if (monsterPrefab == null || !hasValidPath)
        {
            Debug.LogWarning("MonsterManager: No se puede generar monstruo - falta prefab o path");
            return;
        }
        
        // Crear el GameObject del monstruo
        GameObject monsterObj = Instantiate(monsterPrefab);
        
        // Obtener o añadir el componente Monster
        Monster monster = monsterObj.GetComponent<Monster>();
        if (monster == null)
        {
            monster = monsterObj.AddComponent<Monster>();
        }
        
        // Configurar estadísticas basadas en la oleada actual
        ConfigureMonsterStats(monster);
        
        // Asignar el path
        monster.SetPath(currentPath);
        
        // Añadir a la lista de monstruos activos
        activeMonsters.Add(monster);
        monstersAlive++;
        
        Debug.Log($"Monstruo generado. Monstruos vivos: {monstersAlive}");
    }
    
    private void ConfigureMonsterStats(Monster monster)
    {
        // Escalar estadísticas basadas en la oleada
        float waveMultiplier = 1f + (currentWave - 1) * 0.2f; // 20% más stats por oleada
        
        monster.maxHealth = 100f * waveMultiplier;
        monster.currentHealth = monster.maxHealth;
        monster.moveSpeed = 2f + (currentWave - 1) * 0.1f; // Slightly faster each wave
        monster.goldReward = 10 + (currentWave - 1) * 2; // Más oro por oleada
    }
    
    private void OnMonsterDied(Monster monster)
    {
        RemoveMonster(monster);
        // Aquí podrías añadir oro al jugador, etc.
        Debug.Log($"Monstruo murió. Monstruos restantes: {monstersAlive}");
    }
    
    private void OnMonsterReachedEnd(Monster monster)
    {
        RemoveMonster(monster);
        // Aquí podrías reducir vida de la base, etc.
        Debug.Log($"Monstruo llegó al final. Monstruos restantes: {monstersAlive}");
    }
    
    private void RemoveMonster(Monster monster)
    {
        if (activeMonsters.Contains(monster))
        {
            activeMonsters.Remove(monster);
        }
        
        monstersAlive--;
        monstersAlive = Mathf.Max(0, monstersAlive); // Asegurar que no sea negativo
        
        // Verificar si la oleada terminó
        if (monstersAlive <= 0 && waveInProgress)
        {
            CompleteWave();
        }
    }
    
    private void CompleteWave()
    {
        waveInProgress = false;
        
        Debug.Log($"Oleada {currentWave} completada");
        OnWaveComplete?.Invoke(currentWave);
        
        // Iniciar la siguiente oleada después de un delay
        StartCoroutine(StartNextWaveAfterDelay());
    }
    
    private IEnumerator StartNextWaveAfterDelay()
    {
        yield return new WaitForSeconds(timeBetweenWaves);
        
        // Solo continuar si aún tenemos un path válido
        if (hasValidPath)
        {
            StartNextWave();
        }
    }
    
    public void StopAllWaves()
    {
        StopAllCoroutines();
        waveInProgress = false;
        
        // Destruir todos los monstruos activos
        foreach (Monster monster in activeMonsters)
        {
            if (monster != null)
            {
                Destroy(monster.gameObject);
            }
        }
        
        activeMonsters.Clear();
        monstersAlive = 0;
        
        Debug.Log("Todas las oleadas detenidas y monstruos eliminados");
    }
    
    public void ResetWaves()
    {
        StopAllWaves();
        currentWave = 0;
        hasValidPath = false;
        currentPath = null;
    }
    
    // Métodos de información pública
    public int GetCurrentWave() => currentWave;
    public int GetMonstersAlive() => monstersAlive;
    public bool IsWaveInProgress() => waveInProgress;
    public List<Monster> GetActiveMonsters() => new List<Monster>(activeMonsters);
} 