using UnityEngine;
using System;

public class PlayerBase : MonoBehaviour
{
    public static PlayerBase Instance { get; private set; }

    [Header("Health Settings")]
    [SerializeField] private int maxHealth = 100;
    private int currentHealth;

    public static event Action<int, int> OnHealthChanged; // current, max
    public static event Action OnBaseDestroyed;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        currentHealth = maxHealth;
    }

    public void TakeDamage(int damage)
    {
        if (currentHealth <= 0) return;

        currentHealth -= damage;
        currentHealth = Mathf.Max(0, currentHealth);

        //Debug.Log($"La base recibió {damage} de daño. Vida restante: {currentHealth}/{maxHealth}");
        OnHealthChanged?.Invoke(currentHealth, maxHealth);

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        //Debug.LogError("¡La base ha sido destruida! GAME OVER.");
        OnBaseDestroyed?.Invoke();
        
        // Aquí podrías desactivar el objeto, mostrar una animación, etc.
        // Por ahora, solo lanzamos el evento.
    }

    public int GetCurrentHealth() => currentHealth;
    public int GetMaxHealth() => maxHealth;
} 