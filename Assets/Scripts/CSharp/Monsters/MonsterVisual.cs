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
    
    void Start()
    {
        parentMonster = GetComponent<Monster>();
        
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
        
        // Suscribirse a eventos del monstruo si están disponibles
        if (parentMonster != null)
        {
            // Aquí podrías suscribirte a eventos del monstruo para efectos visuales
        }
    }
    
    void Update()
    {
        UpdateHealthBar();
        UpdateMovementAnimation();
    }
    
    private void UpdateHealthBar()
    {
        if (parentMonster != null && healthBarFill != null)
        {
            float healthPercentage = parentMonster.GetHealthPercentage();
            healthBarFill.localScale = new Vector3(healthPercentage, 1f, 1f);
            
            // Ocultar la barra de vida si está llena o el monstruo está muerto
            if (healthBarParent != null)
            {
                healthBarParent.gameObject.SetActive(healthPercentage < 1f && healthPercentage > 0f);
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