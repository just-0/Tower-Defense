using UnityEngine;
using UnityEngine.UI;

public class DisconnectedCameraEffect : MonoBehaviour
{
    [SerializeField] private RawImage cameraDisplay;
    [SerializeField] private float noiseSpeed = 0.2f;
    [SerializeField] private float colorChangeSpeed = 0.8f;
    [SerializeField] private string disconnectedText = "CÁMARA DESCONECTADA";
    [SerializeField] private Text statusText;
    [SerializeField] private GameObject warningIcon;
    [SerializeField] private float warningBlinkSpeed = 1.5f;
    
    private Texture2D noiseTexture;
    private Color[] noiseColors;
    private int textureWidth = 256;
    private int textureHeight = 256;
    private float time = 0;
    
    void Start()
    {
        // Crear textura de ruido
        noiseTexture = new Texture2D(textureWidth, textureHeight);
        noiseColors = new Color[textureWidth * textureHeight];
        
        // Generar textura de ruido inicial
        GenerateNoise();
        
        // Asignar textura al RawImage si no tiene una asignada
        if (cameraDisplay != null && cameraDisplay.texture == null)
        {
            cameraDisplay.texture = noiseTexture;
        }
        
        // Configurar texto de estado
        if (statusText != null)
        {
            statusText.text = disconnectedText;
            statusText.color = Color.red;
        }
    }
    
    void Update()
    {
        time += Time.deltaTime;
        
        // Actualizar la textura de ruido cada frame
        GenerateNoise();
        
        // Animar el texto de estado
        if (statusText != null)
        {
            float alpha = Mathf.PingPong(time * colorChangeSpeed, 1f);
            statusText.color = new Color(1f, 0.2f, 0.2f, 0.5f + alpha * 0.5f);
            
            // Efecto de escala pulsante en el texto
            float scale = 1f + 0.2f * Mathf.Sin(time * 2f);
            statusText.transform.localScale = new Vector3(scale, scale, 1f);
        }
        
        // Animar icono de advertencia
        if (warningIcon != null)
        {
            // Hacer parpadear el icono
            warningIcon.SetActive(Mathf.Sin(time * warningBlinkSpeed * Mathf.PI) > 0);
            
            // Rotar el icono
            warningIcon.transform.Rotate(Vector3.forward, 45f * Time.deltaTime);
        }
    }
    
    private void GenerateNoise()
    {
        // Generar ruido aleatorio con colores dinámicos
        for (int y = 0; y < textureHeight; y++)
        {
            for (int x = 0; x < textureWidth; x++)
            {
                float noise = Mathf.PerlinNoise(
                    (float)x / textureWidth * 10f + time * noiseSpeed,
                    (float)y / textureHeight * 10f + time * noiseSpeed
                );
                
                // Crear un efecto de "estática" de TV más visible
                float value = Random.Range(0f, 0.5f) + noise * 0.2f;
                
                // Añadir barras horizontales ocasionales
                if ((y + (int)(time * 20)) % 30 < 5)
                    value = 0.7f;
                
                // Añadir un tinte rojo para indicar error
                float redTint = Mathf.Sin(time * 0.5f) * 0.3f + 0.3f;
                noiseColors[y * textureWidth + x] = new Color(value + redTint, value * 0.5f, value * 0.5f, 1f);
            }
        }
        
        // Añadir mensaje de "NO SIGNAL" que se mueve
        int messageWidth = 100;
        int messageHeight = 20;
        int messageX = (int)(Mathf.PingPong(time * 30, textureWidth - messageWidth));
        int messageY = (int)(Mathf.PingPong(time * 15, textureHeight - messageHeight));
        
        for (int y = messageY; y < messageY + messageHeight; y++)
        {
            for (int x = messageX; x < messageX + messageWidth; x++)
            {
                if (y >= 0 && y < textureHeight && x >= 0 && x < textureWidth)
                {
                    noiseColors[y * textureWidth + x] = new Color(1f, 0f, 0f, 1f);
                }
            }
        }
        
        // Aplicar colores a la textura
        noiseTexture.SetPixels(noiseColors);
        noiseTexture.Apply();
    }
    
    // Método para activar/desactivar el efecto
    public void SetActive(bool active)
    {
        gameObject.SetActive(active);
    }
} 