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
        
        # Configuración SIMPLE de MediaPipe Hands
        self.hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=1,
            model_complexity=0,  # Modelo simple (más rápido)
            min_detection_confidence=0.5,  # Menos estricto
            min_tracking_confidence=0.5    # Menos estricto
        )
        
        # Propiedades de cámara y threading
        self.camera = None
        self.is_running = False
        self.current_frame_bgr = None  # Frame en formato BGR para envío
        self.debug_frame = None
        self.processed_frame = None
        self.lock = threading.Lock()
        self.camera_switch_request = None # Flag para solicitar cambio de cámara
        
        # Variables para seguimiento de dedos
        self.finger_count = 0
        self.hand_detected = False
        
        # Filtrado temporal MÍNIMO
        self.history_length = 3  # Solo 3 frames
        self.finger_count_history = deque(maxlen=self.history_length)
        
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

                # --- LÓGICA DE RECUPERACIÓN DE CÁMARA ---
                if not self.camera.isOpened():
                    print("Advertencia: La cámara no está abierta. Intentando reiniciar...")
                    self.camera.release()
                    time.sleep(0.5)
                    self.camera.open(self.camera_index)
                    if not self.camera.isOpened():
                        time.sleep(1.0) # Esperar más si el reinicio falla
                        continue

                ret, frame_bgr_original = self.camera.read()
                
                if not ret:
                    read_fail_count += 1
                    print(f"Advertencia: No se pudo leer el frame. Fallo #{read_fail_count}")
                    if read_fail_count > 20: # Tras ~2 segundos de fallos
                        print("Demasiados fallos de lectura. Reiniciando la cámara por completo...")
                        self.camera.release()
                        self.camera = cv2.VideoCapture(self.camera_index)
                        self.camera.set(cv2.CAP_PROP_FRAME_WIDTH, self.width)
                        self.camera.set(cv2.CAP_PROP_FRAME_HEIGHT, self.height)
                        self.camera.set(cv2.CAP_PROP_FPS, self.fps)
                        read_fail_count = 0
                    time.sleep(0.1)
                    continue
                
                read_fail_count = 0
                
                # Voltear horizontalmente para una experiencia tipo espejo
                frame_bgr_original = cv2.flip(frame_bgr_original, 1)
                
                # *** ARREGLO CRÍTICO: CREAR COPIA DEFENSIVA ***
                # Hacer una copia del frame BGR ANTES de cualquier procesamiento
                # Esto garantiza que el frame original nunca sea modificado
                frame_bgr_for_sending = frame_bgr_original.copy()
                
                # Crear una copia separada para MediaPipe para evitar contaminación
                frame_bgr_for_mediapipe = frame_bgr_original.copy()
                
                # ARREGLO DEL PIPELINE DE COLORES:
                # 1. Convertir COPIA a RGB para MediaPipe (nunca el original)
                frame_rgb_for_mediapipe = cv2.cvtColor(frame_bgr_for_mediapipe, cv2.COLOR_BGR2RGB)
                
                # Procesar el frame con MediaPipe usando la copia RGB
                frame_rgb_for_mediapipe.flags.writeable = False
                results = self.hands.process(frame_rgb_for_mediapipe)
                frame_rgb_for_mediapipe.flags.writeable = True
                
                # Crear frame de debug si es necesario (usando otra copia)
                debug_frame = None
                if self.debug_mode:
                    debug_frame = frame_bgr_original.copy()  # Otra copia independiente
                
                # Contar dedos y visualizar (usando la copia BGR para debug)
                count, processed_frame = self._count_fingers_improved(results, frame_bgr_for_mediapipe, debug_frame)
                
                # Actualizar FPS
                frame_count += 1
                if frame_count >= 10:
                    end_time = time.time()
                    actual_fps = frame_count / (end_time - start_time)
                    frame_count = 0
                    start_time = time.time()
                
                # Debug: Verificar ocasionalmente que el frame está en BGR (fuera del lock)
                if frame_count % 100 == 0:  # Solo cada 100 frames para no spamear
                    print(f"[FingerCounter] Frame #{frame_count}: Preparando frame BGR limpio {frame_bgr_for_sending.shape}, dtype={frame_bgr_for_sending.dtype}")
                
                with self.lock:
                    # *** IMPORTANTE: Guardar la copia limpia para envío ***
                    # Esta copia nunca fue tocada por MediaPipe ni ningún otro procesamiento
                    self.current_frame_bgr = frame_bgr_for_sending
                    
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
                time.sleep(0.1)

    def _count_fingers_improved(self, results, frame_bgr, debug_frame=None):
        """
        Versión SIMPLIFICADA de conteo de dedos - menos es más.
        """
        finger_count = 0
        processed_frame = None
        
        h, w, _ = frame_bgr.shape
        self.hand_detected = False
        
        if results.multi_hand_landmarks:
            self.hand_detected = True
            hand_landmarks = results.multi_hand_landmarks[0]
            landmarks = hand_landmarks.landmark
            
            # Convertir landmarks a coordenadas
            lm_list = []
            for lm in landmarks:
                lm_list.append([int(lm.x * w), int(lm.y * h)])
            
            # Lista simple de dedos levantados
            fingers = []
            
            # PULGAR (índice 4) - Método simple
            # Comparar tip del pulgar con el punto medio del pulgar
            if lm_list[4][0] > lm_list[3][0]:  # Mano derecha
                fingers.append(1)
            else:  # Mano izquierda  
                fingers.append(0)
            
            # OTROS 4 DEDOS - Método súper simple
            # Solo comparar si la punta está más arriba que la articulación media
            tip_ids = [8, 12, 16, 20]  # Puntas
            pip_ids = [6, 10, 14, 18]  # Articulaciones medias
            
            for i in range(4):
                if lm_list[tip_ids[i]][1] < lm_list[pip_ids[i]][1]:  # Y menor = más arriba
                    fingers.append(1)
                else:
                    fingers.append(0)
            
            # Contar dedos
            finger_count = fingers.count(1)
            
            # Debug simple
            if self.debug_mode and debug_frame is not None:
                self.mp_drawing.draw_landmarks(
                    debug_frame, hand_landmarks, self.mp_hands.HAND_CONNECTIONS)
                
                cv2.putText(debug_frame, f"DEDOS: {finger_count}", (10, 50), 
                           cv2.FONT_HERSHEY_SIMPLEX, 1.5, (0, 255, 0), 3)
                
                # Mostrar estado de cada dedo
                finger_names = ["Pulgar", "Indice", "Medio", "Anular", "Meñique"]
                for i, (name, up) in enumerate(zip(finger_names, fingers)):
                    color = (0, 255, 0) if up else (0, 0, 255)
                    cv2.putText(debug_frame, f"{name}: {'Si' if up else 'No'}", 
                               (10, 90 + i * 30), cv2.FONT_HERSHEY_SIMPLEX, 0.7, color, 2)
            
            # Frame procesado simple
            processed_frame = np.zeros((h, w, 3), dtype=np.uint8)
            cv2.putText(processed_frame, f"{finger_count}", (w//2-50, h//2),
                       cv2.FONT_HERSHEY_SIMPLEX, 5, (0, 255, 0), 10)

        return finger_count, processed_frame
    
    def _get_stable_finger_count(self):
        """
        Filtro temporal SIMPLE - solo moda básica.
        """
        if not self.finger_count_history:
            return 0
            
        # Simplemente usar el valor más frecuente en el historial
        from collections import Counter
        counter = Counter(self.finger_count_history)
        return counter.most_common(1)[0][0]
    
    def get_current_frame(self):
        """
        Obtiene el frame de cámara más reciente EN FORMATO BGR (correcto para JPEG).
        
        Returns:
            numpy.ndarray: Copia del frame actual en BGR o None si no hay frame disponible.
        """
        with self.lock:
            if self.current_frame_bgr is not None:
                return self.current_frame_bgr.copy()
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


# Ejemplo de uso SIMPLE
if __name__ == "__main__":
    print("Contador de dedos simple - Presiona 'q' para salir")
    
    # Crear contador
    finger_counter = FingerCounter()
    finger_counter.set_debug_mode(True)
    
    # Iniciar cámara
    if finger_counter.start_camera():
        try:
            while True:
                debug_frame = finger_counter.get_debug_frame()
                
                if debug_frame is not None:
                    cv2.imshow("Dedos", debug_frame)
                    
                if cv2.waitKey(1) & 0xFF == ord('q'):
                    break
                    
                time.sleep(0.03)  # ~30 FPS
                
        except KeyboardInterrupt:
            print("Saliendo...")
        finally:
            finger_counter.stop_camera()
            cv2.destroyAllWindows()
    else:
        print("Error: No se pudo iniciar la cámara")