using UnityEngine;
using System.Collections;

public class TutorialTurret : MonoBehaviour
{
    private TurretData turretData;
    private Transform target;
    private float fireCountdown = 0f;

    private Transform firePoint;
    private Transform partToRotate;
    private float turnSpeed = 10f;

    public void Initialize(TurretData data)
    {
        turretData = data;
        
        firePoint = transform.Find("ShootPoint") ?? transform;
        partToRotate = transform.Find("CannonG_Barrel02") ?? transform.Find("Turret") ?? transform;

        DrawRangeIndicator();
    }

    /// <summary>
    /// Dibuja un círculo usando un LineRenderer para visualizar el rango de la torreta.
    /// </summary>
    private void DrawRangeIndicator()
    {
        var rangeIndicatorObject = new GameObject("RangeIndicator");
        rangeIndicatorObject.transform.SetParent(transform, false);
        rangeIndicatorObject.transform.localPosition = Vector3.zero;
        rangeIndicatorObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        var lineRenderer = rangeIndicatorObject.AddComponent<LineRenderer>();
        lineRenderer.material = new Material(Shader.Find("Legacy Shaders/Particles/Additive"));
        lineRenderer.startColor = new Color(0.9f, 0.9f, 0.2f, 0.4f);
        lineRenderer.endColor = new Color(0.9f, 0.9f, 0.2f, 0.4f);
        lineRenderer.startWidth = 0.1f;
        lineRenderer.endWidth = 0.1f;
        lineRenderer.useWorldSpace = false;
        lineRenderer.loop = true;

        int segments = 50;
        lineRenderer.positionCount = segments + 1;

        float radius = turretData.range;
        if (transform.localScale.x != 0)
        {
            radius /= transform.localScale.x;
        }

        Vector3[] points = new Vector3[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            float angle = ((float)i / (float)segments) * 360f * Mathf.Deg2Rad;
            float x = Mathf.Cos(angle) * radius;
            float y = Mathf.Sin(angle) * radius;
            points[i] = new Vector3(x, y, 0);
        }
        lineRenderer.SetPositions(points);
    }

    void Update()
    {
        if (turretData == null) return;

        UpdateTarget();

        if (target == null)
        {
            return;
        }

        LockOnTarget();
        
        fireCountdown -= Time.deltaTime;
        if (fireCountdown <= 0f)
        {
            Shoot();
            fireCountdown = 1f / turretData.fireRate;
        }
    }

    private void UpdateTarget()
    {
        TutorialMonster[] monsters = FindObjectsOfType<TutorialMonster>();
        float shortestDistance = Mathf.Infinity;
        TutorialMonster nearestMonster = null;

        foreach (TutorialMonster monster in monsters)
        {
            float distanceToMonster = Vector3.Distance(transform.position, monster.transform.position);
            if (distanceToMonster < shortestDistance)
            {
                shortestDistance = distanceToMonster;
                nearestMonster = monster;
            }
        }

        if (nearestMonster != null && shortestDistance <= turretData.range)
        {
            target = nearestMonster.transform;
        }
        else
        {
            target = null;
        }
    }

    private void Shoot()
    {
        if (turretData.projectilePrefab == null || firePoint == null || target == null) return;

        GameObject projectileGO = Instantiate(turretData.projectilePrefab, firePoint.position, firePoint.rotation);
        Projectile projectile = projectileGO.GetComponent<Projectile>();

        if (projectile != null)
        {
            projectile.Seek(target, turretData.damage, turretData.isExplosive, turretData.explosionRadius);
        }

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

    private void LockOnTarget()
    {
        if (partToRotate == null || target == null) return;

        Vector3 rotationAxis = Vector3.forward;
        Vector3 defaultAimDirection = Vector3.up;
        Vector3 targetDirection = target.position - partToRotate.position;
        targetDirection.z = 0; 
        float angle = Vector3.SignedAngle(defaultAimDirection, targetDirection, rotationAxis);
        Quaternion baseRotation = Quaternion.Euler(-90, 0, 0);
        Quaternion aimRotation = Quaternion.AngleAxis(angle, rotationAxis);
        Quaternion finalRotation = aimRotation * baseRotation;
        partToRotate.rotation = Quaternion.Slerp(partToRotate.rotation, finalRotation, Time.deltaTime * turnSpeed);
    }
} 