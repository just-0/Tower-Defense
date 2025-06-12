using UnityEngine;
using System;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Player Resources")]
    [SerializeField] private int startingGold = 200;
    private int currentGold;

    public static event Action<int> OnGoldChanged;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        currentGold = startingGold;
    }

    void Start()
    {
        // Suscribirse a eventos relevantes
        Monster.OnMonsterDeath += HandleMonsterDeath;
        PlayerBase.OnBaseDestroyed += HandleBaseDestroyed;
        
        // Notificar a la UI del estado inicial
        OnGoldChanged?.Invoke(currentGold);
    }

    void OnDestroy()
    {
        // Desuscribirse para evitar memory leaks
        Monster.OnMonsterDeath -= HandleMonsterDeath;
        PlayerBase.OnBaseDestroyed -= HandleBaseDestroyed;
    }

    private void HandleMonsterDeath(Monster monster)
    {
        if (monster != null)
        {
            AddGold(monster.goldReward);
        }
    }

    private void HandleBaseDestroyed()
    {
        //Debug.Log("GameManager: La base fue destruida. Terminando el juego.");
        // Detener la aparición de monstruos
        FindObjectOfType<MonsterManager>()?.StopAllWaves();

        // Aquí podrías mostrar un panel de Game Over, detener el tiempo, etc.
        Time.timeScale = 0f; // Una forma simple de "pausar" el juego
    }

    public void AddGold(int amount)
    {
        currentGold += amount;
        //Debug.Log($"Oro añadido: +{amount}. Total: {currentGold}");
        OnGoldChanged?.Invoke(currentGold);
    }

    public bool SpendGold(int amount)
    {
        if (currentGold >= amount)
        {
            currentGold -= amount;
            //Debug.Log($"Oro gastado: -{amount}. Total: {currentGold}");
            OnGoldChanged?.Invoke(currentGold);
            return true;
        }
        else
        {
            //Debug.LogWarning($"No hay suficiente oro. Se requieren {amount}, pero solo hay {currentGold}.");
            return false;
        }
    }
    
    public int GetCurrentGold() => currentGold;
} 