using UnityEngine;

public class PulsingSphere : MonoBehaviour
{
    private float pulseSpeed = 2f;
    private float minScale = 0.1f;
    private float maxScale = 0.3f;
    private float startTime;
    private Renderer rend;

    void Start()
    {
        startTime = Time.time;
        rend = GetComponent<Renderer>();
    }

    void Update()
    {
        float t = Mathf.PingPong((Time.time - startTime) * pulseSpeed, 1f);
        float scale = Mathf.Lerp(minScale, maxScale, t);
        transform.localScale = new Vector3(scale, scale, scale);

        Color c = rend.material.color;
        c.a = Mathf.Lerp(0.2f, 1f, t); // transparencia
        rend.material.color = c;
    }
} 