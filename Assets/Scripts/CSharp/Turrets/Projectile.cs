using UnityEngine;

public class Projectile : MonoBehaviour 
{
    private Transform target;
    private float damage;
    private bool isExplosive;
    private float explosionRadius;
    
    public float speed = 70f;
    public GameObject impactEffect; // Partículas de impacto

    public void Seek(Transform _target, float _damage, bool _isExplosive, float _explosionRadius)
    {
        target = _target;
        damage = _damage;
        isExplosive = _isExplosive;
        explosionRadius = _explosionRadius;
    }

    void Update()
    {
        if (target == null)
        {
            Destroy(gameObject);
            return;
        }

        Vector3 dir = target.position - transform.position;
        float distanceThisFrame = speed * Time.deltaTime;

        if (dir.magnitude <= distanceThisFrame)
        {
            HitTarget();
            return;
        }
        transform.Translate(dir.normalized * distanceThisFrame, Space.World);
        transform.LookAt(target); // Opcional, si el proyectil rota
    }

    void HitTarget()
    {
        if (impactEffect != null)
        {
            GameObject effectIns = Instantiate(impactEffect, transform.position, transform.rotation);
            Destroy(effectIns, 2f); // Destruir efecto después de 2 segundos
        }

        if (isExplosive)
        {
            Explode();
        }
        else
        {
            // Daño a un solo objetivo
            if(target != null) {
                Monster monster = target.GetComponent<Monster>();
                if (monster != null)
                {
                    monster.TakeDamage(damage);
                }
            }
        }
        
        Destroy(gameObject);
    }
    
    void Explode()
    {
        Collider[] colliders = Physics.OverlapSphere(transform.position, explosionRadius);
        foreach (Collider collider in colliders)
        {
            Monster monster = collider.GetComponent<Monster>();
            if (monster != null)
            {
                // Opcional: podrías hacer que el daño disminuya con la distancia al centro
                monster.TakeDamage(damage);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        if (isExplosive)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, explosionRadius);
        }
    }
} 