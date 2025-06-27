using UnityEngine;

public class TutorialMonster : MonoBehaviour
{
    [Header("Atributos del Monstruo")]
    [Tooltip("Velocidad de movimiento por defecto. Se intentará sobreescribir con el valor del componente 'Monster' si existe.")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float health = 100f;

    // Evento para notificar al spawner cuando el monstruo muere
    public event System.Action OnMonsterKilled;

    void Start()
    {
        // Intentar obtener el componente del monstruo principal para heredar su velocidad
        Monster mainMonster = GetComponent<Monster>();
        if (mainMonster != null)
        {
            this.moveSpeed = mainMonster.moveSpeed;
            Debug.Log($"[TutorialMonster] Velocidad sincronizada con el componente 'Monster': {this.moveSpeed}");
        }
        else
        {
            Debug.LogWarning($"[TutorialMonster] No se encontró componente 'Monster'. Usando velocidad por defecto: {this.moveSpeed}");
        }
    }

    void Update()
    {
        // Mover el monstruo de derecha a izquierda
        transform.Translate(Vector3.left * moveSpeed * Time.deltaTime);

        // Destruir si sale de la pantalla para limpiar
        if (transform.position.x < -20f) 
        {
            Destroy(gameObject);
        }
    }

    public void TakeDamage(float amount)
    {
        health -= amount;
        if (health <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        OnMonsterKilled?.Invoke();
        // Aquí podrían ir efectos de muerte (partículas, sonido)
        Destroy(gameObject);
    }

    // Asegurarse de desuscribir eventos si el objeto se destruye inesperadamente
    void OnDestroy()
    {
        OnMonsterKilled = null; 
    }
} 