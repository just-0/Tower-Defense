using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public class Turret : MonoBehaviour
{
    public TurretData turretData { get; private set; }

    private Transform target;
    // La lista monstersInRange ya no es necesaria con el nuevo método
    // private float fireCountdown = 0f; // Eliminado, no se usaba

    [Header("Unity Setup - Opcional, puede asignarse dinámicamente")]
    [Tooltip("Asigna aquí la parte de la torreta que debe girar, ej: el objeto 'CannonG_Barrel02' del prefab.")]
    public Transform partToRotate; // Parte de la torreta que rota para apuntar (ej. cabeza)
    [Tooltip("Asigna el punto exacto desde donde debe salir el disparo, ej: el objeto 'ShootPoint' del prefab.")]
    public Transform firePoint;    // Punto desde donde se disparan los proyectiles
    public float turnSpeed = 10f;
    private float fireCountdown = 0f;


    public void Initialize(TurretData data)
    {
        turretData = data;
        // La configuración del SphereCollider ya no es necesaria, la detección es activa
        // Debug.Log($"<color=cyan>--- INICIALIZANDO TORRETA ACTIVA: {turretData.turretName} ---</color>");

        if (partToRotate == null)
        {
            partToRotate = transform.Find("CannonG_Barrel02") ?? transform.Find("Turret") ?? transform;
        }

        if (firePoint == null)
        {
            firePoint = transform.Find("ShootPoint") ?? transform;
        }
    }

    void Update()
    {
        if (turretData == null) return;

        UpdateTarget();

        // Si no tenemos un objetivo, no hacemos nada más.
        if (target == null)
        {
            return;
        }

        // Si tenemos un objetivo, procedemos a apuntar y disparar.
        LockOnTarget();

        // El contador solo disminuye si hay un objetivo al que apuntar.
        fireCountdown -= Time.deltaTime;

        if (fireCountdown <= 0f)
        {
            Shoot();
            fireCountdown = 1f / turretData.fireRate; // Reiniciar contador
        }
    }

    void UpdateTarget()
    {
        // 1. Detectar todos los colliders dentro del rango de la torreta
        Collider[] colliders = Physics.OverlapSphere(transform.position, turretData.range);

        // 2. Encontrar el monstruo más cercano de entre todos los colliders detectados
        Transform nearestMonster = null;
        float shortestDistance = Mathf.Infinity;

        foreach (Collider collider in colliders)
        {
            Monster monster = collider.GetComponent<Monster>();
            if (monster != null)
            {
                float distanceToMonster = Vector3.Distance(transform.position, monster.transform.position);
                if (distanceToMonster < shortestDistance)
                {
                    shortestDistance = distanceToMonster;
                    nearestMonster = monster.transform;
                }
            }
        }

        // 3. Asignar el monstruo más cercano como objetivo
        if (nearestMonster != null)
        {
            // Si encontramos un objetivo y es DIFERENTE al que ya teníamos...
            if (target != nearestMonster)
            {
                target = nearestMonster;
                // ...reiniciamos el contador para simular un tiempo de "fijar blanco".
                fireCountdown = 1f / turretData.fireRate;
                // Debug.Log($"<color=green>Nuevo objetivo adquirido: {target.name} a {shortestDistance}m</color>");
            }
        }
        else
        {
            target = null;
        }
    }


    void LockOnTarget()
    {
        if (partToRotate == null || target == null) return;

        // --- Lógica de Rotación 2D en un Plano ---

        // 1. Definimos el "suelo" o "pared" del juego. Nuestro eje de rotación será perpendicular a este plano.
        //    Como la cámara mira en Z, la pared es el plano XY. El eje de rotación es el eje Z (Vector3.forward).
        Vector3 rotationAxis = Vector3.forward;

        // 2. Definimos la dirección "cero" de la torreta. Con la rotación base de (-90,0,0),
        //    la parte frontal de la torreta (su eje Z local) apunta hacia ARRIBA en el mundo (Vector3.up).
        Vector3 defaultAimDirection = Vector3.up;

        // 3. Calculamos la dirección real hacia el objetivo en el plano XY.
        Vector3 targetDirection = target.position - partToRotate.position;
        targetDirection.z = 0; // Aplanamos la dirección al plano 2D

        // 4. Calculamos el ángulo que necesitamos girar.
        //    Usamos SignedAngle para obtener un ángulo positivo o negativo dependiendo de si el objetivo
        //    está a la izquierda o a la derecha de nuestra dirección por defecto.
        float angle = Vector3.SignedAngle(defaultAimDirection, targetDirection, rotationAxis);

        // 5. Creamos la rotación final.
        //    La rotación base (-90,0,0) más la rotación de apuntado que acabamos de calcular.
        Quaternion baseRotation = Quaternion.Euler(-90, 0, 0);
        Quaternion aimRotation = Quaternion.AngleAxis(angle, rotationAxis);
        Quaternion finalRotation = aimRotation * baseRotation; // El orden es importante

        // 6. Aplicamos la rotación suavemente.
        partToRotate.rotation = Quaternion.Slerp(partToRotate.rotation, finalRotation, Time.deltaTime * turnSpeed);
    }

    void Shoot()
    {
        if (turretData.projectilePrefab == null || firePoint == null)
        {
            // Debug.LogWarning($"{turretData.turretName}: No se puede disparar. Falta prefab de proyectil o firePoint.");
            return;
        }

        GameObject projectileGO = Instantiate(turretData.projectilePrefab, firePoint.position, firePoint.rotation);
        Projectile projectile = projectileGO.GetComponent<Projectile>();

        if (projectile != null)
        {
            projectile.Seek(target, turretData.damage, turretData.isExplosive, turretData.explosionRadius);
        }
        else
        {
            // Debug.LogWarning($"El prefab {turretData.projectilePrefab.name} no tiene un componente Projectile.");
        }

        // Efectos visuales y de sonido de disparo
        if (turretData.firingEffect != null)
        {
            Instantiate(turretData.firingEffect, firePoint.position, firePoint.rotation);
        }
        if (turretData.firingSound != null)
        {
            AudioSource audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.PlayOneShot(turretData.firingSound);
        }
    }

    // Los métodos OnTriggerEnter y OnTriggerExit ya no son necesarios

    // Para dibujar el rango en el editor
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        if (turretData != null)
        {
            Gizmos.DrawWireSphere(transform.position, turretData.range);
        }
        else
        {
            // Usar un valor por defecto si turretData no está asignado aún
            Gizmos.DrawWireSphere(transform.position, 10f); 
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