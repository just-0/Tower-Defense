using UnityEngine;

public class SpinnerUI : MonoBehaviour
{
    [Tooltip("Velocidad de rotación en grados por segundo.")]
    [SerializeField] private float rotationSpeed = 200f;
    private RectTransform rectTransform;

    void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    void Update()
    {
        // Rotamos el objeto sobre su eje Z
        rectTransform.Rotate(0f, 0f, -rotationSpeed * Time.deltaTime);
    }
} 