using UnityEngine;

public class MonsterVisual : MonoBehaviour
{
    [Header("Componentes Visuales")]
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Transform healthBarParent;
    [SerializeField] private Transform healthBarFill;
    
    [Header("Efectos")]
    [SerializeField] private GameObject damageEffect;
    [SerializeField] private GameObject deathEffect;
    [SerializeField] private Color damageColor = Color.red;
    [SerializeField] private float damageFlashDuration = 0.2f;
    
    private Monster parentMonster;
    private Color originalColor;
    private bool isFlashing = false;
    private Renderer healthBarFillRenderer;
    
    void Start()
    {
        parentMonster = GetComponent<Monster>();
        
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
        
        // La barra de vida ahora se crea siempre desde código para asegurar el comportamiento deseado.
        CreateHealthBar();

        if (healthBarFill != null)
        {
            healthBarFillRenderer = healthBarFill.GetComponent<Renderer>();
        }

        // Suscribirse a eventos del monstruo si están disponibles
        if (parentMonster != null)
        {
            // Aquí podrías suscribirte a eventos del monstruo para efectos visuales
        }
    }

    void OnDestroy()
    {
        // Es crucial destruir la barra de vida cuando el monstruo muere,
        // ya que ya no es un objeto hijo y no se destruiría automáticamente.
        if (healthBarParent != null)
        {
            Destroy(healthBarParent.gameObject);
        }
    }
    
    void Update()
    {
        // Primero, actualizamos la posición y rotación de la barra de vida
        if (healthBarParent != null)
        {
            // La posiciona 1.0f unidades arriba del monstruo (en coordenadas del mundo)
            healthBarParent.position = transform.position + new Vector3(0, 1.0f, 0); 
            // Mantiene la barra "de pie" (sin rotación) sin importar la del monstruo
            healthBarParent.rotation = Quaternion.identity; 
        }

        // Luego, actualizamos su contenido y visibilidad
        UpdateHealthBar();
        UpdateMovementAnimation();
    }
    
    private void CreateHealthBar()
    {
        // Crear el objeto padre para la barra de vida
        GameObject healthBarGO = new GameObject("HealthBar");
        healthBarParent = healthBarGO.transform;
        
        // ¡Importante! Ya NO es hijo del monstruo. Esto evita que rote con él.
        // healthBarParent.SetParent(transform);

        // Crear el fondo de la barra
        GameObject backgroundGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        backgroundGO.name = "Background";
        backgroundGO.transform.SetParent(healthBarParent);
        backgroundGO.transform.localPosition = new Vector3(0, 0, 0.01f);
        backgroundGO.transform.localRotation = Quaternion.identity;
        backgroundGO.transform.localScale = new Vector3(1.2f, 0.15f, 1f); // Barra más grande
        var bgRenderer = backgroundGO.GetComponent<Renderer>();
        bgRenderer.material.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        Destroy(backgroundGO.GetComponent<Collider>());

        // Crear el relleno de la barra
        GameObject fillGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fillGO.name = "Fill";
        fillGO.transform.SetParent(healthBarParent);
        fillGO.transform.localPosition = Vector3.zero;
        fillGO.transform.localRotation = Quaternion.identity;
        fillGO.transform.localScale = new Vector3(1.2f, 0.15f, 1f); // Barra más grande
        healthBarFill = fillGO.transform;
        Destroy(fillGO.GetComponent<Collider>());
    }

    private void UpdateHealthBar()
    {
        if (parentMonster != null && healthBarFill != null)
        {
            float healthPercentage = parentMonster.GetHealthPercentage();
            
            if (healthBarParent != null)
            {
                healthBarParent.gameObject.SetActive(healthPercentage < 1f && healthPercentage > 0f);
            }

            if (!healthBarParent.gameObject.activeSelf) return;

            // Lógica corregida para escalar la barra desde el centro
            float totalWidth = 1.2f; // El ancho total de la barra de fondo
            float newFillWidth = totalWidth * healthPercentage;

            // Escala el relleno al porcentaje de vida
            healthBarFill.localScale = new Vector3(newFillWidth, healthBarFill.localScale.y, healthBarFill.localScale.z);
            // Reposiciona el relleno para que se alinee a la izquierda
            healthBarFill.localPosition = new Vector3(- (totalWidth - newFillWidth) / 2f, healthBarFill.localPosition.y, healthBarFill.localPosition.z);

            // Cambiar color basado en la vida
            if (healthBarFillRenderer != null)
            {
                if (healthPercentage > 0.5f)
                    healthBarFillRenderer.material.color = Color.green;
                else if (healthPercentage > 0.25f)
                    healthBarFillRenderer.material.color = Color.yellow;
                else
                    healthBarFillRenderer.material.color = Color.red;
            }
        }
    }
    
    private void UpdateMovementAnimation()
    {
        if (animator != null && parentMonster != null)
        {
            // Activar animación de caminar si se está moviendo
            bool isMoving = parentMonster.IsAlive() && !parentMonster.isDead;
            animator.SetBool("IsWalking", isMoving);
        }
    }
    
    public void PlayDamageEffect()
    {
        // Efecto visual de daño
        if (damageEffect != null)
        {
            GameObject effect = Instantiate(damageEffect, transform.position, Quaternion.identity);
            Destroy(effect, 1f);
        }
        
        // Flash de color
        if (!isFlashing)
        {
            StartCoroutine(DamageFlash());
        }
    }
    
    private System.Collections.IEnumerator DamageFlash()
    {
        if (spriteRenderer == null) yield break;
        
        isFlashing = true;
        spriteRenderer.color = damageColor;
        
        yield return new WaitForSeconds(damageFlashDuration);
        
        spriteRenderer.color = originalColor;
        isFlashing = false;
    }
    
    public void PlayDeathEffect()
    {
        if (deathEffect != null)
        {
            GameObject effect = Instantiate(deathEffect, transform.position, Quaternion.identity);
            Destroy(effect, 2f);
        }
        
        // Animación de muerte
        if (animator != null)
        {
            animator.SetTrigger("Die");
        }
    }
    
    // Método para configurar el sprite del monstruo
    public void SetMonsterSprite(Sprite newSprite)
    {
        if (spriteRenderer != null && newSprite != null)
        {
            spriteRenderer.sprite = newSprite;
        }
    }
    
    // Método para configurar el color del monstruo
    public void SetMonsterColor(Color newColor)
    {
        if (spriteRenderer != null)
        {
            originalColor = newColor;
            spriteRenderer.color = newColor;
        }
    }
} 