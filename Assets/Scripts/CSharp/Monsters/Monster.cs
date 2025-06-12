using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class Monster : MonoBehaviour
{
    [Header("Estadísticas del Monstruo")]
    public float maxHealth = 100f;
    public float currentHealth;
    public float moveSpeed = 1f;
    public int goldReward = 10;
    public int damageToBase = 1;
    
    [Header("Componentes Visuales")]
    public GameObject modelPrefab;
    private GameObject modelInstance;
    
    [Header("Movimiento")]
    private List<Vector3> pathToFollow;
    private int currentPathIndex = 0;
    private bool isMoving = false;
    private bool hasReachedEnd = false;
    
    [Header("Estado")]
    public bool isDead = false;
    
    // Eventos
    public delegate void MonsterEvent(Monster monster);
    public static event MonsterEvent OnMonsterDeath;
    public static event MonsterEvent OnMonsterReachedEnd;
    
    void Awake()
    {
        currentHealth = maxHealth;
    }
    
    void Start()
    {
        // Instanciar el modelo visual si está asignado
        if (modelPrefab != null)
        {
            modelInstance = Instantiate(modelPrefab, transform);
            modelInstance.transform.localPosition = Vector3.zero;
        }
    }
    
    void Update()
    {
        if (isMoving && !isDead && !hasReachedEnd)
        {
            MoveAlongPath();
        }
    }
    
    public void SetPath(List<Vector3> newPath)
    {
        pathToFollow = new List<Vector3>(newPath);
        currentPathIndex = 0;
        isMoving = true;
        
        // Posicionar en el primer punto del camino
        if (pathToFollow.Count > 0)
        {
            transform.position = pathToFollow[0];
        }
    }
    
    private void MoveAlongPath()
    {
        if (pathToFollow == null || pathToFollow.Count == 0 || currentPathIndex >= pathToFollow.Count)
        {
            ReachEnd();
            return;
        }
        
        Vector3 targetPosition = pathToFollow[currentPathIndex];
        Vector3 direction = (targetPosition - transform.position).normalized;
        
        // Mover hacia el objetivo
        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
        
        // Rotar hacia la dirección de movimiento
        if (direction != Vector3.zero)
        {
            if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
            {
                // Movimiento horizontal
                if (direction.x > 0)
                {
                    // Derecha
                    transform.rotation = Quaternion.Euler(0, 75, -75);
                }
                else
                {
                    // Izquierda
                    transform.rotation = Quaternion.Euler(0, -75, 75);
                }
            }
            else
            {
                // Movimiento vertical
                if (direction.y > 0)
                {
                    // Arriba
                    transform.rotation = Quaternion.Euler(-90, 0, 0);
                }
                else
                {
                    // Abajo
                    transform.rotation = Quaternion.Euler(90, 180, 0);
                }
            }
        }
        
        // Verificar si llegó al punto actual
        if (Vector3.Distance(transform.position, targetPosition) < 0.1f)
        {
            currentPathIndex++;
            
            // Si llegó al final del camino
            if (currentPathIndex >= pathToFollow.Count)
            {
                ReachEnd();
            }
        }
    }
    
    public void TakeDamage(float damage)
    {
        if (isDead) return;
        
        currentHealth -= damage;
        currentHealth = Mathf.Max(0, currentHealth);
        
        if (currentHealth <= 0)
        {
            Die();
        }
    }
    
    private void Die()
    {
        if (isDead) return;
        
        isDead = true;
        isMoving = false;
        
        OnMonsterDeath?.Invoke(this);
        
        // Efecto de muerte (opcional)
        // PlayDeathEffect();
        
        // Destruir después de un pequeño delay
        Destroy(gameObject, 0.5f);
    }
    
    private void ReachEnd()
    {
        if (hasReachedEnd) return;
        
        hasReachedEnd = true;
        isMoving = false;
        
        OnMonsterReachedEnd?.Invoke(this);
        
        // Destruir el monstruo
        Destroy(gameObject, 0.1f);
    }
    
    public float GetHealthPercentage()
    {
        return currentHealth / maxHealth;
    }
    
    public bool IsAlive()
    {
        return !isDead && currentHealth > 0;
    }
    
    // Método para obtener la posición actual en el path (útil para targeting de torres)
    public Vector3 GetCurrentTarget()
    {
        if (pathToFollow == null || currentPathIndex >= pathToFollow.Count)
            return transform.position;
            
        return pathToFollow[currentPathIndex];
    }
} 