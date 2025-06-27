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


def scan_for_available_cameras(max_index_to_check=10):
    """
    Escanea todos los índices de cámara hasta un máximo y devuelve una lista de los que están disponibles.
    
    Args:
        max_index_to_check (int): El índice más alto a probar (e.g., 10 para probar de 0 a 9).
        
    Returns:
        list: Una lista de enteros con los índices de las cámaras disponibles.
    """
    available_indices = []
    print(f"Buscando cámaras disponibles hasta el índice {max_index_to_check-1}...")
    for index in range(max_index_to_check):
        cap = cv2.VideoCapture(index)
        if cap.isOpened():
            print(f"  - Cámara encontrada en el índice {index}.")
            available_indices.append(index)
            cap.release()
    print(f"Búsqueda finalizada. Cámaras encontradas: {available_indices}")
    return available_indices


class FingerCounter:
    """
    Clase mejorada para rastrear manos y contar dedos levantados usando MediaPipe.
    Implementa filtrado temporal, detección de orientación de la mano y visualización.
    """
    
    def __init__(self, camera_index=None, width=640, height=480, fps=30):
        """
        Inicializa el contador de dedos con opciones mejoradas.
        
        Args:
            camera_index (int, optional): Índice específico de la cámara a usar. Si es None, busca una disponible.
            width (int): Resolución de ancho de cámara
            height (int): Resolución de alto de cámara
            fps (int): Cuadros por segundo de la cámara
        """
        # Si no se especifica un índice, búscalo automáticamente.
        if camera_index is None:
            available_cams = scan_for_available_cameras()
            # Usar la primera cámara encontrada por defecto, o -1 si no hay ninguna
            self.camera_index = available_cams[0] if available_cams else -1
        else:
            self.camera_index = camera_index
            
        self.width = width
        self.height = height
        self.fps = fps
        
        # Componentes MediaPipe Hand con configuración mejorada
        self.mp_hands = mp.solutions.hands
        self.mp_drawing = mp.solutions.drawing_utils
        self.mp_drawing_styles = mp.solutions.drawing_styles
        
        # Objeto CLAHE para mejora de contraste, se crea una vez para eficiencia
        self.clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
        
        # Configuración mejorada de MediaPipe Hands
        self.hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=1,
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
        self.camera_switch_request = None # Flag para solicitar cambio de cámara
        
        # Variables para seguimiento de dedos
        self.finger_count = 0
        self.hand_detected = False
        
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
        if self.camera_index == -1:
            print("Error: No se puede iniciar la cámara porque no se encontró ningún índice de cámara válido.")
            return False

        try:
            if self.camera is None:
                print(f"Intentando abrir la cámara en el índice: {self.camera_index}")
                self.camera = cv2.VideoCapture(self.camera_index)
                
                # Intenta abrir la cámara varias veces si falla al principio
                retry_count = 0
                max_retries = 3
                
                while not self.camera.isOpened() and retry_count < max_retries:
                    print(f"Advertencia: No se pudo abrir la cámara {self.camera_index}. Intento {retry_count+1}/{max_retries}")
                    time.sleep(1)
                    self.camera.open(self.camera_index)
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
        read_fail_count = 0
        start_time = time.time()
        actual_fps = 0
        
        while self.is_running:
            try:
                # --- LÓGICA DE CAMBIO DE CÁMARA ---
                with self.lock:
                    if self.camera_switch_request is not None:
                        new_index = self.camera_switch_request
                        self.camera_switch_request = None
                        print(f"Cambiando al índice de cámara {new_index}...")
                        
                        # Detenemos y liberamos la cámara actual
                        if self.camera.isOpened():
                            self.camera.release()
                        
                        # Abrimos la nueva cámara
                        self.camera_index = new_index
                        self.camera = cv2.VideoCapture(self.camera_index)
                        
                        if not self.camera.isOpened():
                            print(f"Error: No se pudo cambiar a la cámara {self.camera_index}.")
                            # Opcional: intentar volver a la anterior o simplemente detener
                            self.is_running = False
                            return # Salir del hilo si la nueva cámara falla
                        
                        # Reseteamos contadores para la nueva cámara
                        read_fail_count = 0
                        frame_count = 0
                        print(f"Cámara cambiada con éxito al índice {self.camera_index}.")
                # -----------------------------------

                # --- LÓGICA DE RECUPERACIÓN DE CÁMARA ---
                if not self.camera.isOpened():
                    print("Advertencia: La cámara no está abierta. Intentando reiniciar...")
                    self.camera.release()
                    time.sleep(0.5)
                    self.camera.open(self.camera_index)
                    if not self.camera.isOpened():
                        time.sleep(1.0) # Esperar más si el reinicio falla
                        continue
                # ----------------------------------------

                ret, frame = self.camera.read()
                
                if not ret:
                    read_fail_count += 1
                    print(f"Advertencia: No se pudo leer el frame. Fallo #{read_fail_count}")
                    # Si falla muchas veces seguidas, intentamos un reinicio completo
                    if read_fail_count > 20: # Tras ~2 segundos de fallos
                        print("Demasiados fallos de lectura. Reiniciando la cámara por completo...")
                        self.camera.release()
                        self.camera = cv2.VideoCapture(self.camera_index)
                        self.camera.set(cv2.CAP_PROP_FRAME_WIDTH, self.width)
                        self.camera.set(cv2.CAP_PROP_FRAME_HEIGHT, self.height)
                        self.camera.set(cv2.CAP_PROP_FPS, self.fps)
                        read_fail_count = 0 # Reiniciar contador
                    time.sleep(0.1)
                    continue
                
                read_fail_count = 0 # El reinicio fue exitoso en la primera lectura
                
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
    
    def _preprocess_frame(self, frame):
        """
        Aplica mejoras de contraste al frame para optimizar la detección con poca luz.
        También estandariza el pipeline de color para evitar tintes azules.
        """
        # Convertir a espacio de color LAB que separa luminosidad de color
        lab_image = cv2.cvtColor(frame, cv2.COLOR_BGR2LAB)
        l_channel, a_channel, b_channel = cv2.split(lab_image)
        
        # Aplicar CLAHE solo al canal de luminosidad (L)
        l_channel_eq = self.clahe.apply(l_channel)
        
        # Unir canales y convertir de vuelta a BGR
        updated_lab_image = cv2.merge((l_channel_eq, a_channel, b_channel))
        processed_frame = cv2.cvtColor(updated_lab_image, cv2.COLOR_LAB2BGR)
        
        return processed_frame

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
    
    def _is_thumb_raised(self, landmarks):
        """
        Determina si el pulgar está levantado de una forma más robusta,
        comparando su distancia a la base del dedo índice.
        """
        # Puntos clave
        thumb_tip = landmarks[4]
        thumb_mcp = landmarks[2] # nudillo base del pulgar
        index_mcp = landmarks[5] # nudillo base del índice
        
        # El pulgar se considera "fuera" si su punta está más lejos de la palma 
        # (representada por el índice) que su propia base.
        dist_tip_to_index = math.hypot(thumb_tip.x - index_mcp.x, thumb_tip.y - index_mcp.y)
        dist_mcp_to_index = math.hypot(thumb_mcp.x - index_mcp.x, thumb_mcp.y - index_mcp.y)
        
        return dist_tip_to_index > dist_mcp_to_index

    def _is_finger_raised(self, landmarks, wrist_landmark, mcp_idx, pip_idx, tip_idx):
        """
        Determina si un dedo está levantado de forma robusta, independientemente de la orientación.
        Un dedo está levantado si está (1) recto y (2) su punta está más alejada de la muñeca que su nudillo.
        """
        # Puntos clave
        tip = landmarks[tip_idx]
        pip = landmarks[pip_idx] # Articulación media (interfalángica proximal)
        mcp = landmarks[mcp_idx] # Nudillo (metacarpofalángica)

        # 1. Comprobar si el dedo está relativamente recto.
        is_straight = self._calculate_angle(mcp, pip, tip) > 150.0

        # 2. Comprobar si la punta del dedo está más alejada de la muñeca que la articulación media.
        #    Esto evita contar dedos que están doblados hacia la palma.
        dist_tip_wrist = math.hypot(tip.x - wrist_landmark.x, tip.y - wrist_landmark.y)
        dist_pip_wrist = math.hypot(pip.x - wrist_landmark.x, pip.y - wrist_landmark.y)
        
        is_away_from_palm = dist_tip_wrist > dist_pip_wrist
        
        return is_straight and is_away_from_palm
    
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
        
        if results.multi_hand_landmarks:
            self.hand_detected = True
            
            # Como solo procesamos una mano, tomamos la primera y única detectada
            hand_landmarks = results.multi_hand_landmarks[0]
            
            # Obtener landmarks y muñeca
            landmarks = hand_landmarks.landmark
            wrist = landmarks[0]
            
            # Comprobar el pulgar (método mejorado)
            if self._is_thumb_raised(landmarks):
                finger_count += 1
            
            # Comprobar otros dedos con el método robusto
            finger_indices = [
                (5, 6, 8),    # Índice: mcp, pip, tip
                (9, 10, 12),  # Medio: mcp, pip, tip
                (13, 14, 16), # Anular: mcp, pip, tip
                (17, 18, 20)  # Meñique: mcp, pip, tip
            ]
            
            for mcp, pip, tip in finger_indices:
                if self._is_finger_raised(landmarks, wrist, mcp, pip, tip):
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
            
            # Crear un frame procesado simple con el conteo de dedos
            processed_frame = np.zeros((h, w, 3), dtype=np.uint8)
            cv2.putText(processed_frame, f"Dedos: {finger_count}", (w//2-100, h//2),
                       cv2.FONT_HERSHEY_SIMPLEX, 2, (255, 255, 255), 3)

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
    
    def switch_camera(self, new_index):
        """
        Solicita un cambio de cámara al hilo principal.
        
        Args:
            new_index (int): El nuevo índice de cámara a utilizar.
        """
        with self.lock:
            # No hacemos nada si se solicita el mismo índice
            if new_index == self.camera_index:
                return
            self.camera_switch_request = new_index

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
    finger_counter = FingerCounter(camera_index=None)
    
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