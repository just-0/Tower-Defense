using UnityEngine;

public class CameraConnectionAnimation : MonoBehaviour
{
    [SerializeField] private float rotationSpeed = 30f;
    [SerializeField] private float pulseSpeed = 1.5f;
    [SerializeField] private float minScale = 0.8f;
    [SerializeField] private float maxScale = 1.2f;
    [SerializeField] private bool useRotation = true;
    [SerializeField] private bool usePulse = true;
    
    private Vector3 originalScale;
    
    void Start()
    {
        originalScale = transform.localScale;
    }
    
    void Update()
    {
        // Rotación
        if (useRotation)
        {
            transform.Rotate(Vector3.forward, rotationSpeed * Time.deltaTime);
        }
        
        // Efecto de pulso (escala)
        if (usePulse)
        {
            float pulse = Mathf.Lerp(minScale, maxScale, (Mathf.Sin(Time.time * pulseSpeed) + 1f) * 0.5f);
            transform.localScale = originalScale * pulse;
        }
    }
} 