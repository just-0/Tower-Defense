using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

public class SettingsController : MonoBehaviour
{
    [Header("Control References")]
    [SerializeField] private MenuGestureController menuGestureController;
    [SerializeField] private Text currentCameraText;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button prevButton;

    private List<int> availableCameraIndices = new List<int>();
    private int listPosition = 0; // El índice DENTRO de nuestra lista de cámaras disponibles

    public void SetAvailableCameras(List<int> indices)
    {
        availableCameraIndices = indices.OrderBy(i => i).ToList();
        Debug.Log($"Configuración actualizada con cámaras: {string.Join(", ", availableCameraIndices)}");

        if (availableCameraIndices.Count > 0)
        {
            listPosition = 0;
            nextButton.interactable = availableCameraIndices.Count > 1;
            prevButton.interactable = availableCameraIndices.Count > 1;
            UpdateCameraText();
            // Opcional: Solicitar el cambio a la primera cámara de la lista por si acaso
            menuGestureController.RequestCameraSwitch(availableCameraIndices[listPosition]);
        }
        else
        {
            currentCameraText.text = "No se encontraron cámaras";
            nextButton.interactable = false;
            prevButton.interactable = false;
        }
    }

    public void OnNextCameraButton()
    {
        if (availableCameraIndices.Count <= 1) return;

        listPosition++;
        if (listPosition >= availableCameraIndices.Count)
        {
            listPosition = 0; // Vuelve al inicio
        }
        
        menuGestureController.RequestCameraSwitch(availableCameraIndices[listPosition]);
        UpdateCameraText();
    }

    public void OnPreviousCameraButton()
    {
        if (availableCameraIndices.Count <= 1) return;

        listPosition--;
        if (listPosition < 0)
        {
            listPosition = availableCameraIndices.Count - 1; // Va al final
        }

        menuGestureController.RequestCameraSwitch(availableCameraIndices[listPosition]);
        UpdateCameraText();
    }

    private void UpdateCameraText()
    {
        if (currentCameraText != null)
        {
            if (availableCameraIndices.Count > 0)
            {
                currentCameraText.text = $"Cámaras: {availableCameraIndices.Count} | Usando Índice: {availableCameraIndices[listPosition]}";
            }
            else
            {
                currentCameraText.text = "No se encontraron cámaras";
            }
        }
    }
} 