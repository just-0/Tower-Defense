import cv2
import mediapipe as mp
import time
import math

# --- Configuración Ajustada para Cámara desde Arriba ---
# Cuando la cámara está mirando desde arriba, el movimiento en Z se interpreta diferente
# Un tap ahora significa que el valor Z *disminuye* al acercarse a la superficie
Z_VELOCITY_THRESHOLD = -0.015  # VALOR NEGATIVO - Umbral para detectar bajada
Z_RESET_THRESHOLD = 0.008      # VALOR POSITIVO - Umbral para detectar subida
COOLDOWN_SECONDS = 0.5         # Tiempo de espera después de un toque

# --- Inicialización de MediaPipe ---
mp_hands = mp.solutions.hands
mp_drawing = mp.solutions.drawing_utils
mp_drawing_styles = mp.solutions.drawing_styles

hands = mp_hands.Hands(
    static_image_mode=False,      
    max_num_hands=1,              
    model_complexity=1,           # Usar modelo más complejo para mejor precisión
    min_detection_confidence=0.6, 
    min_tracking_confidence=0.6)  

# --- Inicialización de la Cámara ---
cap = cv2.VideoCapture(1)

if not cap.isOpened():
    print("Error: No se pudo abrir la cámara.")
    exit()

# --- Variables de Estado ---
previous_index_tip_z = None
finger_state = "UP"   # Puede ser "UP", "DOWN" (recién tocó)
last_tap_time = 0
z_values = []         # Para almacenar algunos valores recientes de Z
MAX_Z_HISTORY = 5     # Número de valores Z a mantener para suavizado
debug_info = True     # Mostrar información de depuración en pantalla
tap_count = 0         # Contador de taps detectados

print("Iniciando detección para cámara desde arriba. Presiona 'q' para salir.")
print(f"Umbral de velocidad Z para toque: {Z_VELOCITY_THRESHOLD}")
print(f"Umbral de velocidad Z para reset: {Z_RESET_THRESHOLD}")
print("IMPORTANTE: Este código está optimizado para cámara mirando desde arriba.")

while cap.isOpened():
    success, image = cap.read()
    if not success:
        print("Ignorando frame vacío de la cámara.")
        continue

    # Voltear la imagen horizontalmente para una vista tipo espejo y convertir BGR a RGB
    image = cv2.cvtColor(cv2.flip(image, 1), cv2.COLOR_BGR2RGB)

    # Mejorar rendimiento marcando la imagen como no escribible
    image.flags.writeable = False
    results = hands.process(image)

    # Restaurar la capacidad de escritura y convertir de vuelta a BGR para dibujar
    image.flags.writeable = True
    image = cv2.cvtColor(image, cv2.COLOR_RGB2BGR)

    h, w, _ = image.shape
    tap_detected_in_frame = False

    # --- Procesamiento de Landmarks ---
    if results.multi_hand_landmarks:
        # Iterar sobre la mano detectada
        for hand_landmarks in results.multi_hand_landmarks:
            # --- Obtener Landmark del Dedo Índice ---
            index_tip = hand_landmarks.landmark[mp_hands.HandLandmark.INDEX_FINGER_TIP]
            index_pip = hand_landmarks.landmark[mp_hands.HandLandmark.INDEX_FINGER_PIP]
            index_mcp = hand_landmarks.landmark[mp_hands.HandLandmark.INDEX_FINGER_MCP]

            # Comprobar si el dedo índice está extendido
            finger_extended = index_tip.y < index_pip.y
            
            # Obtener valor Z actual
            current_z = index_tip.z
            
            # Añadir a historial para suavizado
            z_values.append(current_z)
            if len(z_values) > MAX_Z_HISTORY:
                z_values.pop(0)
            
            # Calcular Z suavizado para reducir ruido
            smoothed_z = sum(z_values) / len(z_values)

            # --- Lógica de Detección de Toque (Tap) ---
            if previous_index_tip_z is not None and finger_extended:
                # En configuración desde arriba, calculamos delta_z
                delta_z = smoothed_z - previous_index_tip_z
                current_time = time.time()

                # Debug: Imprimir valores Z
                if debug_info:
                    z_text = f"Z: {smoothed_z:.4f}, Delta: {delta_z:.4f}"
                    cv2.putText(image, z_text, (10, 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
                    state_text = f"Estado: {finger_state}"
                    cv2.putText(image, state_text, (10, 60), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
                    tap_text = f"Taps: {tap_count}"
                    cv2.putText(image, tap_text, (10, 90), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)

                # En configuración desde arriba:
                # - Un valor delta_z negativo significa que el dedo se acerca a la superficie
                # - Un valor delta_z positivo significa que el dedo se aleja de la superficie
                
                # Condición 1: Detectar movimiento hacia abajo significativo (Z disminuye)
                if finger_state == "UP" and delta_z < Z_VELOCITY_THRESHOLD:
                    # Condición 2: Cooldown para evitar múltiples detecciones rápidas
                    if current_time - last_tap_time > COOLDOWN_SECONDS:
                        print(f"¡TAP DETECTADO! (Delta Z: {delta_z:.4f})")
                        tap_detected_in_frame = True
                        finger_state = "DOWN"  # Cambiar estado a recién tocado
                        last_tap_time = current_time
                        tap_count += 1

                # Condición 3: Detectar movimiento hacia arriba para resetear el estado
                elif finger_state == "DOWN" and delta_z > Z_RESET_THRESHOLD:
                    finger_state = "UP"
                    # print("Finger reset to UP")

            # Actualizar Z anterior para el próximo frame
            previous_index_tip_z = smoothed_z

            # --- Dibujar Landmarks ---
            # Dibujar todos los landmarks por defecto
            mp_drawing.draw_landmarks(
                image,
                hand_landmarks,
                mp_hands.HAND_CONNECTIONS,
                mp_drawing_styles.get_default_hand_landmarks_style(),
                mp_drawing_styles.get_default_hand_connections_style())

            # Resaltar la punta del dedo índice si se detectó toque
            tip_x, tip_y = int(index_tip.x * w), int(index_tip.y * h)
            color = (0, 255, 0) if tap_detected_in_frame else (0, 0, 255)  # Verde si TAP, Rojo si no
            radius = 15 if tap_detected_in_frame else 10  # Círculo más grande cuando hay tap
            cv2.circle(image, (tip_x, tip_y), radius, color, -1)  # Círculo relleno

            # Si se detectó tap, mostrar mensaje grande en pantalla
            if tap_detected_in_frame:
                cv2.putText(image, "¡TAP!", (w//2-60, 50), 
                           cv2.FONT_HERSHEY_SIMPLEX, 1.5, (0, 255, 0), 3)

    else:
        # Si no se detecta mano, resetear el estado Z y vaciar historial
        previous_index_tip_z = None
        finger_state = "UP"
        z_values = []

    # Mostrar información de uso
    cv2.putText(image, "Presiona 'q' para salir", (w-230, h-20), 
               cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)

    # Mostrar la imagen
    cv2.imshow('Detector de Tap - Cámara desde Arriba', image)

    # Salir con 'q'
    if cv2.waitKey(5) & 0xFF == ord('q'):
        break

# --- Limpieza ---
print(f"Total de taps detectados: {tap_count}")
cap.release()
cv2.destroyAllWindows()
hands.close()
