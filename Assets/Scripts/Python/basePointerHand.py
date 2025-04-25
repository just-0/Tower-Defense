import cv2
import mediapipe as mp
import time
import math

# --- Configuración ---
Z_VELOCITY_THRESHOLD = 0.006  # Umbral de cambio en Z para detectar bajada (AJUSTAR ESTE VALOR)
Z_RESET_THRESHOLD = -0.008 # Umbral de cambio negativo en Z para detectar subida (AJUSTAR ESTE VALOR)
COOLDOWN_SECONDS = 0.5     # Tiempo de espera (segundos) después de un toque para evitar detecciones múltiples

# --- Inicialización de MediaPipe ---
mp_hands = mp.solutions.hands
mp_drawing = mp.solutions.drawing_utils
mp_drawing_styles = mp.solutions.drawing_styles

hands = mp_hands.Hands(
    static_image_mode=False,      # Procesar video
    max_num_hands=1,              # Detectar solo una mano para simplificar
    min_detection_confidence=0.6, # Confianza mínima para detección inicial
    min_tracking_confidence=0.6)  # Confianza mínima para seguimiento

# --- Inicialización de la Cámara ---
cap = cv2.VideoCapture(1) # Usa 0 para la cámara predeterminada, o cambia el índice si tienes varias

if not cap.isOpened():
    print("Error: No se pudo abrir la cámara.")
    exit()

# --- Variables de Estado ---
previous_index_tip_z = None
finger_state = "UP" # Puede ser "UP", "DOWN" (recién tocó)
last_tap_time = 0

print("Iniciando detección. Presiona 'q' para salir.")
print(f"Umbral de velocidad Z para toque: {Z_VELOCITY_THRESHOLD}")
print(f"Umbral de velocidad Z para reset: {Z_RESET_THRESHOLD}")

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
        # Iterar sobre la mano detectada (configuramos max_num_hands=1)
        for hand_landmarks in results.multi_hand_landmarks:
            # --- Obtener Landmark del Dedo Índice ---
            index_tip = hand_landmarks.landmark[mp_hands.HandLandmark.INDEX_FINGER_TIP]
            # index_pip = hand_landmarks.landmark[mp_hands.HandLandmark.INDEX_FINGER_PIP] # Opcional: para verificar extensión

            current_z = index_tip.z

            # --- Lógica de Detección de Toque (Tap) ---
            if previous_index_tip_z is not None:
                delta_z = current_z - previous_index_tip_z
                current_time = time.time()

                # Debug: Imprimir valores Z
                # print(f"Current Z: {current_z:.4f}, Prev Z: {previous_index_tip_z:.4f}, Delta Z: {delta_z:.4f}, State: {finger_state}")

                # Condición 1: Detectar movimiento hacia abajo significativo
                # La coordenada Z de MediaPipe disminuye cuanto más cerca está de la cámara.
                # Por lo tanto, al bajar la mano (alejarla de la cámara), Z *aumenta*.
                # Usamos un estado para registrar un toque solo cuando viene desde "UP".
                if finger_state == "UP" and delta_z > Z_VELOCITY_THRESHOLD:
                    # Condición 2: Cooldown para evitar múltiples detecciones rápidas
                    if current_time - last_tap_time > COOLDOWN_SECONDS:
                        print(f"¡TAP DETECTADO! (Delta Z: {delta_z:.4f})")
                        tap_detected_in_frame = True
                        finger_state = "DOWN" # Cambiar estado a recién tocado
                        last_tap_time = current_time

                # Condición 3: Detectar movimiento hacia arriba para resetear el estado
                # Si el dedo sube (Z disminuye) lo suficiente, vuelve al estado "UP"
                elif finger_state == "DOWN" and delta_z < Z_RESET_THRESHOLD:
                     finger_state = "UP"
                     # print("Finger reset to UP")


            # Actualizar Z anterior para el próximo frame
            previous_index_tip_z = current_z

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
            color = (0, 255, 0) if tap_detected_in_frame else (0, 0, 255) # Verde si TAP, Rojo si no
            cv2.circle(image, (tip_x, tip_y), 10, color, -1) # Círculo relleno más grande

    else:
        # Si no se detecta mano, resetear el estado Z
        previous_index_tip_z = None
        finger_state = "UP"

    # Mostrar la imagen
    cv2.imshow('MediaPipe Hands - Index Tap Detection', image)

    # Salir con 'q'
    if cv2.waitKey(5) & 0xFF == ord('q'):
        break

# --- Limpieza ---
print("Cerrando...")
cap.release()
cv2.destroyAllWindows()
hands.close()
