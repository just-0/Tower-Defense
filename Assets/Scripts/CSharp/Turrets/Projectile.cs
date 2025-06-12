using UnityEngine;

public class Projectile : MonoBehaviour 
{
    private Transform target;
    private float damage;
    public float speed = 70f;
    public GameObject impactEffect; // Partículas de impacto

    public void Seek(Transform _target, float _damage)
    {
        target = _target;
        damage = _damage;
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

        Monster monster = target.GetComponent<Monster>();
        if (monster != null)
        {
            monster.TakeDamage(damage);
        }
        else
        {
            // Si el objetivo no es un monstruo (quizás fue destruido)
            // podrías aplicar daño en área o simplemente destruir el proyectil
        }
        Destroy(gameObject);
    }
} 