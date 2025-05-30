using UnityEngine;
using System.Collections.Generic;

public class Turret : MonoBehaviour
{
    public TurretData turretData { get; private set; }

    private Transform target;
    private float fireCountdown = 0f;
    private List<Monster> monstersInRange = new List<Monster>(); // Para un mejor manejo de objetivos

    [Header("Unity Setup - Opcional, puede asignarse dinámicamente")]
    public Transform partToRotate; // Parte de la torreta que rota para apuntar (ej. cabeza)
    public Transform firePoint;    // Punto desde donde se disparan los proyectiles
    public float turnSpeed = 10f;

    public void Initialize(TurretData data)
    {
        turretData = data;
        // Aplicar cualquier configuración inicial basada en turretData si es necesario
        // Por ejemplo, si el prefab no tiene un SphereCollider para el rango,
        // podrías añadirlo y configurarlo aquí.
        SphereCollider rangeCollider = GetComponent<SphereCollider>();
        if (rangeCollider == null)
        {
            rangeCollider = gameObject.AddComponent<SphereCollider>();
        }
        rangeCollider.isTrigger = true;
        rangeCollider.radius = turretData.range;

        if (partToRotate == null)
        {
            // Intenta encontrar una parte llamada "Head" o similar, o usa el transform principal
            Transform head = transform.Find("Head"); // Asume una convención de nombrado
            if (head != null) partToRotate = head;
            else partToRotate = transform; 
        }

        if (firePoint == null)
        {
            // Intenta encontrar un firepoint, o usa la posición de la torreta
            Transform fp = transform.Find("FirePoint"); // Asume una convención de nombrado
            if (fp != null) firePoint = fp;
            else firePoint = transform;
        }
    }

    void Update()
    {
        if (turretData == null) return;

        UpdateTarget();

        if (target != null)
        {
            LockOnTarget();

            if (fireCountdown <= 0f)
            {
                Shoot();
                fireCountdown = 1f / turretData.fireRate;
            }
        }

        fireCountdown -= Time.deltaTime;
    }

    void UpdateTarget()
    {
        // Limpiar monstruos muertos o fuera de rango de la lista
        monstersInRange.RemoveAll(monster => monster == null || !monster.gameObject.activeInHierarchy || Vector3.Distance(transform.position, monster.transform.position) > turretData.range);

        if (monstersInRange.Count == 0)
        {
            target = null;
            return;
        }

        // Estrategia simple: el primer monstruo en la lista (el que entró primero en rango y sigue vivo)
        // Se podría mejorar con otras lógicas (más cercano, más vida, etc.)
        target = monstersInRange[0].transform;
    }

    void LockOnTarget()
    {
        if (partToRotate == null) return;

        Vector3 dir = target.position - partToRotate.position;
        Quaternion lookRotation = Quaternion.LookRotation(dir);
        // Solo rotar en Y para torretas terrestres, o ajustar según necesidad
        Vector3 rotation = Quaternion.Lerp(partToRotate.rotation, lookRotation, Time.deltaTime * turnSpeed).eulerAngles;
        partToRotate.rotation = Quaternion.Euler(0f, rotation.y, 0f); // O Quaternion.Euler(rotation.x, rotation.y, rotation.z) si es torreta 3D
    }

    void Shoot()
    {
        if (turretData.projectilePrefab == null || firePoint == null)
        {
            Debug.LogWarning($"{turretData.turretName}: No se puede disparar. Falta prefab de proyectil o firePoint.");
            return;
        }

        GameObject projectileGO = Instantiate(turretData.projectilePrefab, firePoint.position, firePoint.rotation);
        Projectile projectile = projectileGO.GetComponent<Projectile>(); // Asumiendo que tienes un script Projectile

        if (projectile != null)
        {
            projectile.Seek(target, turretData.damage); // El proyectil necesita un método Seek(Transform target, int damage)
        }
        else
        {
            Debug.LogWarning($"El prefab {turretData.projectilePrefab.name} no tiene un componente Projectile.");
            // Como alternativa, podrías aplicar daño directo aquí si no usas proyectiles físicos
            // o manejar la lógica del proyectil de otra forma.
        }

        // Efectos visuales y de sonido de disparo
        if (turretData.firingEffect != null)
        {
            Instantiate(turretData.firingEffect, firePoint.position, firePoint.rotation);
        }
        if (turretData.firingSound != null)
        {
            // Deberías tener un AudioSource en la torreta o un gestor de audio global
            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.PlayOneShot(turretData.firingSound);
        }

        // Debug.Log($"{turretData.turretName} disparando a {target.name}");
    }

    // Estos métodos se llaman por los mensajes de Unity OnTriggerEnter/Exit
    void OnTriggerEnter(Collider other)
    {
        Monster monster = other.GetComponent<Monster>();
        if (monster != null && !monstersInRange.Contains(monster))
        {
            monstersInRange.Add(monster);
        }
    }

    void OnTriggerExit(Collider other)
    {
        Monster monster = other.GetComponent<Monster>();
        if (monster != null && monstersInRange.Contains(monster))
        {
            monstersInRange.Remove(monster);
        }
    }

    // Para dibujar el rango en el editor
    void OnDrawGizmosSelected()
    {
        if (turretData != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, turretData.range);
        }
        else
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, 5f); // Rango por defecto para gizmo si no hay data
        }
    }
}

// Necesitarás un script Projectile.cs básico. Aquí un ejemplo simple:
/*
public class Projectile : MonoBehaviour 
{
    private Transform target;
    private int damage;
    public float speed = 70f;
    public GameObject impactEffect; // Partículas de impacto

    public void Seek(Transform _target, int _damage)
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
            monster.TakeDamage(damage); // Asume que Monster.cs tiene un método TakeDamage(int amount)
        }
        else
        {
            // Si el objetivo no es un monstruo (quizás fue destruido)
            // podrías aplicar daño en área o simplemente destruir el proyectil
        }
        Destroy(gameObject);
    }
}
*/ 