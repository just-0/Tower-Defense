"""
Módulo mejorado de seguimiento de dedos usando MediaPipe con mayor robustez.
"""

import cv2
import mediapipe as mp
import numpy as np
import threading
import time
import math
from collections import deque


class FingerCounter:
    """
    Clase mejorada para rastrear manos y contar dedos levantados usando MediaPipe.
    Implementa filtrado temporal, detección de orientación de la mano y visualización.
    """
    
    def __init__(self, camera_index=0, width=640, height=480, fps=30):
        """
        Inicializa el contador de dedos con opciones mejoradas.
        
        Args:
            camera_index (int): Índice de la cámara a usar
            width (int): Resolución de ancho de cámara
            height (int): Resolución de alto de cámara
            fps (int): Cuadros por segundo de la cámara
        """
        self.camera_index = camera_index
        self.width = width
        self.height = height
        self.fps = fps
        
        # Componentes MediaPipe Hand con configuración mejorada
        self.mp_hands = mp.solutions.hands
        self.mp_drawing = mp.solutions.drawing_utils
        self.mp_drawing_styles = mp.solutions.drawing_styles
        
        # Configuración mejorada de MediaPipe Hands
        self.hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=2,
            model_complexity=1,  # Modelo más preciso (0-simple, 1-completo)
            min_detection_confidence=0.6,
            min_tracking_confidence=0.6
        )
        
        # Propiedades de cámara y threading
        self.camera = None
        self.is_running = False
        self.current_frame = None
        self.debug_frame = None
        self.processed_frame = None
        self.lock = threading.Lock()
        
        # Variables para seguimiento de dedos
        self.finger_count = 0
        self.hand_detected = False
        self.left_hand_detected = False
        self.right_hand_detected = False
        
        # Filtrado temporal (estabilizador)
        self.history_length = 5
        self.finger_count_history = deque(maxlen=self.history_length)
        self.landmarks_history = []
        
        # Umbrales y configuración para detección robusta
        self.thumb_angle_threshold = 150  # Ángulo para detectar pulgar levantado
        self.finger_height_threshold = 0.05  # Umbral para altura de dedos
        
        # Estado de depuración
        self.debug_mode = False
    
    def start_camera(self):
        """
        Inicia la cámara y comienza el seguimiento de dedos con manejo de errores.
        
        Returns:
            bool: True si la cámara se inició correctamente, False de lo contrario.
        """
        try:
            if self.camera is None:
                self.camera = cv2.VideoCapture(self.camera_index)
                
                # Intenta abrir la cámara varias veces si falla al principio
                retry_count = 0
                max_retries = 3
                
                while not self.camera.isOpened() and retry_count < max_retries:
                    print(f"Advertencia: No se pudo abrir la cámara {self.camera_index}. Intento {retry_count+1}/{max_retries}")
                    time.sleep(1)
                    self.camera = cv2.VideoCapture(self.camera_index)
                    retry_count += 1
                    
                if not self.camera.isOpened():
                    print(f"Error: No se pudo abrir la cámara {self.camera_index} después de {max_retries} intentos")
                    return False
                    
                # Configurar propiedades de la cámara
                self.camera.set(cv2.CAP_PROP_FRAME_WIDTH, self.width)
                self.camera.set(cv2.CAP_PROP_FRAME_HEIGHT, self.height)
                self.camera.set(cv2.CAP_PROP_FPS, self.fps)
                
                # Iniciar el hilo de la cámara
                self.is_running = True
                threading.Thread(target=self._camera_thread, daemon=True).start()
                print(f"Cámara de seguimiento de dedos (índice {self.camera_index}) iniciada")
                return True
            return False
        except Exception as e:
            print(f"Error al iniciar la cámara: {str(e)}")
            return False
    
    def _camera_thread(self):
        """Hilo en segundo plano que captura continuamente frames y procesa manos."""
        frame_count = 0
        start_time = time.time()
        actual_fps = 0
        
        while self.is_running:
            try:
                ret, frame = self.camera.read()
                
                if not ret:
                    print("Advertencia: No se pudo leer el frame. Reintentando...")
                    time.sleep(0.1)
                    continue
                
                # Voltear horizontalmente para una experiencia tipo espejo
                frame = cv2.flip(frame, 1)
                
                # Convertir a RGB para MediaPipe
                frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                
                # Procesar el frame con MediaPipe (evitar copias innecesarias)
                frame_rgb.flags.writeable = False
                results = self.hands.process(frame_rgb)
                frame_rgb.flags.writeable = True
                
                # Crear una copia para dibujar
                debug_frame = None
                if self.debug_mode:
                    debug_frame = cv2.cvtColor(frame_rgb, cv2.COLOR_RGB2BGR).copy()
                
                # Contar dedos y visualizar
                count, processed_frame = self._count_fingers(results, frame_rgb, debug_frame)
                
                # Actualizar FPS
                frame_count += 1
                if frame_count >= 10:
                    end_time = time.time()
                    actual_fps = frame_count / (end_time - start_time)
                    frame_count = 0
                    start_time = time.time()
                
                with self.lock:
                    self.current_frame = frame_rgb
                    if self.debug_mode and debug_frame is not None:
                        # Añadir información de FPS
                        cv2.putText(debug_frame, f"FPS: {actual_fps:.1f}", (10, 30), 
                                    cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 0), 2)
                        self.debug_frame = debug_frame
                    
                    if processed_frame is not None:
                        self.processed_frame = processed_frame
                    
                    # Filtrado temporal para estabilizar el conteo
                    self.finger_count_history.append(count)
                    self.finger_count = self._get_stable_finger_count()
                
                # Control de velocidad para mantener FPS constante
                time.sleep(max(0, 1/self.fps - (time.time() - start_time)))
                
            except Exception as e:
                print(f"Error en hilo de cámara: {str(e)}")
                time.sleep(0.1)  # Evitar bucle de error rápido
    
    def _calculate_angle(self, p1, p2, p3):
        """
        Calcula el ángulo entre tres puntos.
        
        Args:
            p1, p2, p3: Puntos en el espacio 3D
            
        Returns:
            float: Ángulo en grados
        """
        # Vectores
        v1 = [p1.x - p2.x, p1.y - p2.y, p1.z - p2.z]
        v2 = [p3.x - p2.x, p3.y - p2.y, p3.z - p2.z]
        
        # Producto punto
        dot = sum(a*b for a, b in zip(v1, v2))
        
        # Magnitudes
        mag1 = math.sqrt(sum(a*a for a in v1))
        mag2 = math.sqrt(sum(a*a for a in v2))
        
        # Evitar división por cero
        if mag1 * mag2 == 0:
            return 0
            
        # Calcular ángulo
        cos_angle = max(-1, min(1, dot / (mag1 * mag2)))
        angle = math.degrees(math.acos(cos_angle))
        
        return angle
    
    def _is_thumb_raised(self, landmarks, is_left_hand):
        """
        Determina si el pulgar está levantado usando ángulos entre articulaciones.
        
        Args:
            landmarks: Puntos de referencia de MediaPipe
            is_left_hand: Si es la mano izquierda
            
        Returns:
            bool: True si el pulgar está levantado
        """
        # Puntos clave del pulgar
        cmc = landmarks[1]    # Articulación carpo-metacarpiana
        mcp = landmarks[2]    # Articulación metacarpo-falángica 
        ip = landmarks[3]     # Articulación interfalángica
        tip = landmarks[4]    # Punta del pulgar
        wrist = landmarks[0]  # Muñeca
        
        # Calcular ángulo principal para el pulgar
        angle = self._calculate_angle(wrist, cmc, tip)
        
        # Diferentes umbrales basados en si es mano izquierda o derecha
        threshold = self.thumb_angle_threshold
        
        # Comprobar si el pulgar está extendido usando el ángulo
        if angle > threshold:
            # Verificación posicional adicional (dependiendo de la orientación de la mano)
            if (is_left_hand and tip.x > mcp.x) or (not is_left_hand and tip.x < mcp.x):
                return True
                
        return False
    
    def _is_finger_raised(self, landmarks, base_idx, mid_idx, tip_idx):
        """
        Determina si un dedo está levantado usando la posición relativa.
        
        Args:
            landmarks: Puntos de referencia de MediaPipe
            base_idx: Índice de la base del dedo
            mid_idx: Índice de la articulación media
            tip_idx: Índice de la punta del dedo
            
        Returns:
            bool: True si el dedo está levantado
        """
        base = landmarks[base_idx]
        mid = landmarks[mid_idx]
        tip = landmarks[tip_idx]
        
        # Comprobar si la punta está más alta que la base (eje Y invertido)
        finger_raised = tip.y < base.y
        
        # Verificar que el dedo está realmente extendido, no doblado
        extended = self._calculate_angle(base, mid, tip) > 160
        
        # El dedo está levantado si la punta está sobre la base y el dedo está extendido
        return finger_raised and extended
    
    def _detect_hand_orientation(self, landmarks):
        """
        Detecta si la mano es izquierda o derecha basado en la posición del pulgar.
        
        Args:
            landmarks: Puntos clave de la mano
            
        Returns:
            bool: True si es mano izquierda
        """
        # Calcular orientación basada en la posición relativa del pulgar y el meñique
        thumb_tip = landmarks[4]
        pinky_tip = landmarks[20]
        
        return thumb_tip.x > pinky_tip.x
    
    def _count_fingers(self, results, frame, debug_frame=None):
        """
        Cuenta el número de dedos levantados en el frame con detección mejorada.
        
        Args:
            results: Resultados de landmarks de MediaPipe
            frame: Frame actual
            debug_frame: Frame para visualización de depuración
            
        Returns:
            tuple: (Número de dedos levantados, Frame procesado con visualización)
        """
        finger_count = 0
        processed_frame = None
        
        h, w, _ = frame.shape
        
        # Reiniciar estado de detección
        self.hand_detected = False
        self.left_hand_detected = False
        self.right_hand_detected = False
        
        if results.multi_hand_landmarks:
            self.hand_detected = True
            
            for hand_idx, hand_landmarks in enumerate(results.multi_hand_landmarks):
                # Determinar si es mano izquierda o derecha
                if results.multi_handedness:
                    if hand_idx < len(results.multi_handedness):
                        handedness = results.multi_handedness[hand_idx]
                        is_left = "Left" in handedness.classification[0].label
                    else:
                        # Hacer una determinación basada en los landmarks si MediaPipe no proporciona la información
                        is_left = self._detect_hand_orientation(hand_landmarks.landmark)
                else:
                    is_left = self._detect_hand_orientation(hand_landmarks.landmark)
                
                if is_left:
                    self.left_hand_detected = True
                else:
                    self.right_hand_detected = True
                
                # Obtener landmarks
                landmarks = hand_landmarks.landmark
                
                # Comprobar el pulgar (método mejorado)
                if self._is_thumb_raised(landmarks, is_left):
                    finger_count += 1
                
                # Comprobar otros dedos
                finger_indices = [
                    (5, 6, 8),    # Índice: base, medio, punta
                    (9, 10, 12),  # Medio: base, medio, punta
                    (13, 14, 16), # Anular: base, medio, punta
                    (17, 18, 20)  # Meñique: base, medio, punta
                ]
                
                for base, mid, tip in finger_indices:
                    if self._is_finger_raised(landmarks, base, mid, tip):
                        finger_count += 1
                
                # Dibujar landmarks y añadir información visual en modo depuración
                if self.debug_mode and debug_frame is not None:
                    # Dibujar landmarks
                    self.mp_drawing.draw_landmarks(
                        debug_frame,
                        hand_landmarks,
                        self.mp_hands.HAND_CONNECTIONS,
                        self.mp_drawing_styles.get_default_hand_landmarks_style(),
                        self.mp_drawing_styles.get_default_hand_connections_style()
                    )
                    
                    # Añadir texto de handedness
                    hand_text = "Izquierda" if is_left else "Derecha"
                    cv2.putText(debug_frame, hand_text, 
                               (int(landmarks[0].x * w), int(landmarks[0].y * h - 20)),
                               cv2.FONT_HERSHEY_SIMPLEX, 0.6, (255, 0, 0), 2)
            
            # Crear un frame procesado simple con el conteo de dedos
            processed_frame = np.zeros((h, w, 3), dtype=np.uint8)
            cv2.putText(processed_frame, f"Dedos: {finger_count}", (w//2-100, h//2),
                       cv2.FONT_HERSHEY_SIMPLEX, 2, (255, 255, 255), 3)
            
            # Si se detectaron ambas manos, mostrar esa información
            if self.left_hand_detected and self.right_hand_detected:
                cv2.putText(processed_frame, "Ambas manos detectadas", (w//2-150, h//2+50),
                           cv2.FONT_HERSHEY_SIMPLEX, 1, (0, 255, 255), 2)
        
        return finger_count, processed_frame
    
    def _get_stable_finger_count(self):
        """
        Obtiene un conteo de dedos estable utilizando un filtro temporal.
        
        Returns:
            int: Conteo de dedos estabilizado
        """
        if not self.finger_count_history:
            return 0
            
        # Usar la moda (el valor más frecuente) para estabilizar
        counts = {}
        for count in self.finger_count_history:
            if count in counts:
                counts[count] += 1
            else:
                counts[count] = 1
                
        # Encontrar el valor más frecuente
        max_count = 0
        stable_count = 0
        
        for count, frequency in counts.items():
            if frequency > max_count:
                max_count = frequency
                stable_count = count
                
        return stable_count
    
    def get_current_frame(self):
        """
        Obtiene el frame de cámara más reciente.
        
        Returns:
            numpy.ndarray: Copia del frame actual o None si no hay frame disponible.
        """
        with self.lock:
            if self.current_frame is not None:
                return self.current_frame.copy()
            return None
    
    def get_debug_frame(self):
        """
        Obtiene el frame de depuración con visualizaciones.
        
        Returns:
            numpy.ndarray: Frame de depuración o None si no está disponible.
        """
        with self.lock:
            if self.debug_frame is not None:
                return self.debug_frame.copy()
            return None
    
    def get_processed_frame(self):
        """
        Obtiene el frame procesado con información del conteo.
        
        Returns:
            numpy.ndarray: Frame procesado o None si no está disponible.
        """
        with self.lock:
            if self.processed_frame is not None:
                return self.processed_frame.copy()
            return None
    
    def get_finger_count(self):
        """
        Obtiene el conteo de dedos actual estabilizado.
        
        Returns:
            int: Número de dedos levantados detectados
        """
        with self.lock:
            return self.finger_count
    
    def is_hand_detected(self):
        """
        Verifica si se detecta alguna mano.
        
        Returns:
            bool: True si se detecta al menos una mano
        """
        with self.lock:
            return self.hand_detected
    
    def set_debug_mode(self, enabled):
        """
        Activa o desactiva el modo de depuración.
        
        Args:
            enabled (bool): True para activar, False para desactivar
        """
        with self.lock:
            self.debug_mode = enabled
    
    def stop_camera(self):
        """Detiene la cámara y libera recursos."""
        try:
            self.is_running = False
            time.sleep(0.5)  # Esperar a que el hilo termine
            
            if self.camera is not None:
                self.camera.release()
                self.camera = None
                print(f"Cámara de seguimiento de dedos (índice {self.camera_index}) detenida")
        except Exception as e:
            print(f"Error al detener la cámara: {str(e)}")


# Ejemplo de uso
if __name__ == "__main__":
    # Crear instancia del contador de dedos
    finger_counter = FingerCounter(camera_index=0)
    
    # Activar modo depuración
    finger_counter.set_debug_mode(True)
    
    # Iniciar la cámara
    if finger_counter.start_camera():
        try:
            while True:
                # Obtener frame de depuración
                debug_frame = finger_counter.get_debug_frame()
                
                # Mostrar información
                if debug_frame is not None:
                    # Añadir conteo de dedos al frame
                    count = finger_counter.get_finger_count()
                    cv2.putText(debug_frame, f"Conteo: {count}", (20, 70), 
                                cv2.FONT_HERSHEY_SIMPLEX, 2, (0, 255, 0), 3)
                    
                    # Mostrar frame
                    cv2.imshow("Contador de Dedos", debug_frame)
                    
                # Salir con 'q'
                if cv2.waitKey(1) & 0xFF == ord('q'):
                    break
                    
                time.sleep(0.01)  # Reducir uso de CPU
                
        except KeyboardInterrupt:
            print("Programa interrumpido por el usuario")
        finally:
            # Liberar recursos
            finger_counter.stop_camera()
            cv2.destroyAllWindows()
    else:
        print("No se pudo iniciar la cámara. Verifique la conexión y el índice.")