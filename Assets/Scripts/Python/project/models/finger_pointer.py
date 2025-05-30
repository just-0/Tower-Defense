import cv2
import mediapipe as mp
import numpy as np
import time
import os
from collections import deque


class GridSystem:
    def __init__(self, width, height, grid_size=30):
        self.grid_size = grid_size
        self.width = width
        self.height = height
        
        self.cols = width // grid_size
        self.rows = height // grid_size
        
        x = np.arange(0, width, grid_size)
        y = np.arange(0, height, grid_size)
        xx, yy = np.meshgrid(x, y)
        
        self.cell_coords = np.stack([
            xx, yy, 
            np.minimum(xx + grid_size, width), 
            np.minimum(yy + grid_size, height),
            xx + grid_size // 2, 
            yy + grid_size // 2
        ], axis=-1)
        
        self.mask = np.zeros((height, width), dtype=np.uint8)
        self.load_mask()
        
        self._update_grid_from_mask()
    
    def load_mask(self):
        possible_paths = [
            "./debug_mask_final.png",
            "../debug_mask_final.png",
            "../../debug_mask_final.png",
            "/home/just/Documents/TestUnity/Tower-Defense/Assets/Scripts/Python/debug_mask_final.png",
            "/home/just/Documents/TestUnity/Tower-Defense/Assets/Scripts/debug_mask_final.png"
        ]
        
        for mask_path in possible_paths:
            if os.path.exists(mask_path):
                self.mask = cv2.imread(mask_path, cv2.IMREAD_GRAYSCALE)
                if self.mask is not None:
                    if self.mask.shape != (self.height, self.width):
                        self.mask = cv2.resize(self.mask, (self.width, self.height))
                break
        
        if self.mask is None:
            self.mask = np.zeros((self.height, self.width), dtype=np.uint8)
    
    def _update_grid_from_mask(self):
        small_mask = cv2.resize(self.mask, (self.cols, self.rows), interpolation=cv2.INTER_AREA)
        self.grid_matrix = (small_mask >= 128)  # Áreas blancas son ocupadas
    
    def get_grid_cell(self, x, y):
        if 0 <= x < self.width and 0 <= y < self.height:
            col = x // self.grid_size
            row = y // self.grid_size
            return (row, col) if (0 <= row < self.rows and 0 <= col < self.cols) else None
        return None

    def is_cell_occupied(self, row, col):
        if 0 <= row < self.rows and 0 <= col < self.cols:
            return self.grid_matrix[row, col]
        return False
        
    def get_cell_center(self, row, col):
        if 0 <= row < self.rows and 0 <= col < self.cols:
            _, _, _, _, cx, cy = self.cell_coords[row, col]
            return (cx, cy)
        return None
    
    def draw_grid(self, image, selected_cells=None):
        overlay = np.zeros_like(image)
        free_cells = ~self.grid_matrix
        y_idx, x_idx = np.where(free_cells)
        for row, col in zip(y_idx, x_idx):
            x1, y1, x2, y2, _, _ = self.cell_coords[row, col]
            cv2.rectangle(overlay, (x1, y1), (x2, y2), (0, 200, 0), -1)
            cv2.rectangle(overlay, (x1, y1), (x2, y2), (0, 255, 0), 1)
        
        cv2.addWeighted(overlay, 0.4, image, 1.0, 0, image)
        
        if selected_cells:
            for row, col in selected_cells:
                x1, y1, x2, y2, _, _ = self.cell_coords[row, col]
                cv2.rectangle(image, (x1, y1), (x2, y2), (255, 0, 255), 2)
        
        return image


class FingerPositionDetector:
    def __init__(self, grid_system):
        self.mp_hands = mp.solutions.hands
        self.hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=1,
            min_detection_confidence=0.25,
            min_tracking_confidence=0.25,
            model_complexity=1
        )
        self.mp_drawing = mp.solutions.drawing_utils
        
        self.grid_system = grid_system
        self.position_history = deque(maxlen=7)
        self.selected_cell = None
        self.last_position = None
        self.start_time = None
        self.is_pointing = False
        
        self.confirmation_duration = 0.8
        self.stability_threshold = 20
        self.pointing_threshold = 0.45
        
        self.kalman = cv2.KalmanFilter(4, 2)
        self.kalman.measurementMatrix = np.array([[1, 0, 0, 0], [0, 1, 0, 0]], np.float32)
        self.kalman.transitionMatrix = np.array([[1, 0, 1, 0], [0, 1, 0, 1], [0, 0, 1, 0], [0, 0, 0, 1]], np.float32)
        self.kalman.processNoiseCov = np.eye(4, dtype=np.float32) * 0.03
        self.kalman.measurementNoiseCov = np.eye(2, dtype=np.float32) * 0.08
        
        self.progress_angles = np.linspace(-90, 270, 11)
        self.current_cell = None
        
        # Para filtro exponencial suave
        self.smoothed_pos = None
        self.smooth_alpha = 0.75
        
        # Caché de la última posición válida para mantener continuidad
        self.last_valid_position = None
        self.position_valid_count = 0
        
        # Contador para limitar envío de posiciones
        self.frame_counter = 0
        self.send_frame_interval = 1
        
        # Memoria de celdas para reducir parpadeo
        self.cell_memory = None
        self.cell_memory_counter = 0
        self.cell_memory_threshold = 3

    def _calculate_pointing_score(self, landmarks):
        try:
            tip = np.array([landmarks[8].x, landmarks[8].y])
            mcp = np.array([landmarks[5].x, landmarks[5].y])
            finger_vec = tip - mcp
            finger_len = np.linalg.norm(finger_vec)
            if finger_len <= 0:
                return 0.0
            extension_score = min(finger_len / 0.1, 1.0) * 0.6

            base_vec = np.array([landmarks[6].x, landmarks[6].y]) - mcp
            tip_vec = tip - np.array([landmarks[7].x, landmarks[7].y])
            base_vec /= np.linalg.norm(base_vec) if np.linalg.norm(base_vec) > 0 else 1
            tip_vec /= np.linalg.norm(tip_vec) if np.linalg.norm(tip_vec) > 0 else 1
            alignment_score = (np.dot(base_vec, tip_vec) + 1) / 2 * 0.3

            bent_fingers = 0
            for i_base, i_tip in [(9,12),(13,16),(17,20)]:
                if landmarks[i_tip].y > landmarks[i_base].y - 0.05:
                    bent_fingers += 1
            fingers_score = bent_fingers * 0.1

            total_score = extension_score + alignment_score + fingers_score
            return total_score

        except Exception:
            return 0.0

    def _update_smoothed_position(self, raw_pos):
        if self.smoothed_pos is None:
            self.smoothed_pos = np.array(raw_pos, dtype=np.float32)
        else:
            self.smoothed_pos = self.smooth_alpha * np.array(raw_pos) + (1 - self.smooth_alpha) * self.smoothed_pos
        return self.smoothed_pos.astype(int)

    def process_frame(self, color_frame, depth_frame=None, selected_cells=None):
        output_image = color_frame.copy()
        current_position = None
        is_confirmed = False
        self.frame_counter += 1
        
        try:
            if color_frame is None or color_frame.size == 0:
                self.is_pointing = False
                return output_image, None, False, None
            
            # Simplificar preprocesamiento para menor latencia
            process_height, process_width = color_frame.shape[:2]
            
            # Reducir resolución solo si es muy grande, de lo contrario usar original
            if process_width > 640:
                scale_factor = 640.0 / process_width
                process_width = 640
                process_height = int(process_height * scale_factor)
                small_frame = cv2.resize(color_frame, (process_width, process_height), interpolation=cv2.INTER_AREA)
            else:
                small_frame = color_frame
                
            # Preprocesamiento simplificado - mínimo necesario para MediaPipe
            enhanced_rgb = cv2.cvtColor(small_frame, cv2.COLOR_BGR2RGB)

            # Procesar frame con MediaPipe
            results = self.hands.process(enhanced_rgb)

            # Si se detectó la mano
            if results.multi_hand_landmarks:
                hand = results.multi_hand_landmarks[0]
                landmarks = hand.landmark

                # Dibujar landmarks para depuración visual
                self.mp_drawing.draw_landmarks(
                    output_image,
                    hand,
                    self.mp_hands.HAND_CONNECTIONS,
                    self.mp_drawing.DrawingSpec(thickness=2, circle_radius=2, color=(0, 255, 0)),
                    self.mp_drawing.DrawingSpec(thickness=2, circle_radius=1, color=(255, 0, 0))
                )

                height, width = color_frame.shape[:2]
                raw_x = int(landmarks[8].x * width)
                raw_y = int(landmarks[8].y * height)

                # Suavizar posición raw con filtro exponencial
                smooth_x, smooth_y = self._update_smoothed_position((raw_x, raw_y))

                # Aplicar filtro Kalman para mayor estabilidad
                measurement = np.array([[smooth_x], [smooth_y]], dtype=np.float32)
                self.kalman.correct(measurement)
                predicted = self.kalman.predict()
                x, y = int(predicted[0]), int(predicted[1])

                current_position = (x, y, 0)
                self.position_history.append(current_position)
                
                # Actualizar posición válida
                self.last_valid_position = (x, y)
                self.position_valid_count = min(self.position_valid_count + 1, 15)

                pointing_score = self._calculate_pointing_score(landmarks)
                self.is_pointing = pointing_score >= self.pointing_threshold

                # Determinar celda actual
                cell = self.grid_system.get_grid_cell(x, y)
                
                # Estabilizar cambios de celda usando una memoria de celdas
                if cell != self.current_cell:
                    if cell == self.cell_memory:
                        self.cell_memory_counter += 1
                        if self.cell_memory_counter >= self.cell_memory_threshold:
                            self.current_cell = cell
                            self.cell_memory_counter = 0
                    else:
                        self.cell_memory = cell
                        self.cell_memory_counter = 1
                else:
                    self.cell_memory = cell
                    self.cell_memory_counter = 0

                if self.current_cell:
                    cell_valid = not self.grid_system.is_cell_occupied(*self.current_cell)
                    
                    if self.is_pointing:
                        # Dibujar círculo en la posición del dedo
                        cv2.circle(output_image, (x, y), 15,
                                  (0, 255, 0) if cell_valid else (0, 0, 255),
                                  -1)
                        palm_x = int(landmarks[0].x * width)
                        palm_y = int(landmarks[0].y * height)
                        cv2.line(output_image, (palm_x, palm_y), (x, y), (255, 255, 0), 2)
    
                        # Proceso de confirmación con animación
                        if cell_valid and self._is_position_stable():
                            if self.start_time is None:
                                self.start_time = time.time()
    
                            elapsed = time.time() - self.start_time
                            progress = min(elapsed / self.confirmation_duration, 1.0)
    
                            angle_idx = int(progress * 10)
                            cv2.circle(output_image, (x, y), 35, (0, 165, 255), 2)
                            cv2.ellipse(output_image, (x, y), (35, 35), 0,
                                        self.progress_angles[0], self.progress_angles[angle_idx],
                                        (0, 255, 255), 3)
    
                            if elapsed >= self.confirmation_duration:
                                self.selected_cell = self.current_cell
                                is_confirmed = True
                                self.start_time = None
    
                                # Animación de confirmación
                                cv2.circle(output_image, (x, y), 40, (0, 255, 0), -1, cv2.LINE_AA)
                                cv2.circle(output_image, (x, y), 30, (255, 255, 255), -1, cv2.LINE_AA)
                                cv2.circle(output_image, (x, y), 20, (0, 0, 255), -1, cv2.LINE_AA)
                        else:
                            self.start_time = None
            else:
                # Manejo mejorado para cuando no se detecta la mano
                self.position_valid_count = max(0, self.position_valid_count - 1)
                
                # Mantener la última posición conocida por un tiempo
                if self.position_valid_count > 0 and self.last_valid_position is not None:
                    x, y = self.last_valid_position
                    current_position = (x, y, 0)
                    
                    # Decaer gradualmente el pointing
                    if self.position_valid_count > 8:
                        self.is_pointing = True
                        
                        if self.current_cell:
                            cell_valid = not self.grid_system.is_cell_occupied(*self.current_cell)
                            
                            # Dibujar indicador visual
                            cv2.circle(output_image, (x, y), 15,
                                      (0, 255, 0) if cell_valid else (0, 0, 255),
                                      -1, cv2.LINE_AA)
                    else:
                        # Disminuir la opacidad gradualmente
                        alpha = self.position_valid_count / 8.0
                        if self.current_cell and alpha > 0.2:
                            cell_valid = not self.grid_system.is_cell_occupied(*self.current_cell)
                            color = (0, 255, 0) if cell_valid else (0, 0, 255)
                            
                            # Crear capa transparente para efecto de desvanecimiento
                            overlay = output_image.copy()
                            cv2.circle(overlay, (x, y), 15, color, -1, cv2.LINE_AA)
                            cv2.addWeighted(overlay, alpha, output_image, 1-alpha, 0, output_image)
                else:
                    # Reiniciar cuando la mano ya no es detectada por mucho tiempo
                    self.position_history.clear()
                    self.start_time = None
                    self.is_pointing = False
                    self.current_cell = None
                    self.cell_memory = None
                    self.cell_memory_counter = 0

            # Dibujar la cuadrícula y las celdas seleccionadas
            output_image = self.grid_system.draw_grid(output_image, selected_cells)

            # Resaltar la celda seleccionada
            if self.selected_cell:
                row, col = self.selected_cell
                x1, y1, x2, y2, cx, cy = self.grid_system.cell_coords[row, col]
                overlay = np.zeros_like(output_image)
                cv2.rectangle(overlay, (x1, y1), (x2, y2), (0, 0, 255), -1)
                cv2.addWeighted(overlay, 0.4, output_image, 1.0, 0, output_image)
                cv2.rectangle(output_image, (x1, y1), (x2, y2), (0, 0, 255), 2)

        except Exception as e:
            import traceback
            traceback.print_exc()
            print(f"Error en process_frame: {e}")
            self.is_pointing = False
            self.current_cell = None
            
        return output_image, current_position, is_confirmed, self.selected_cell if is_confirmed else None

    def _is_position_stable(self):
        """
        Determina si la posición del dedo ha estado estable durante un tiempo suficiente.
        
        Returns:
            bool: True si la posición es estable, False en caso contrario
        """
        # Si no hay suficientes puntos en el historial, no es estable
        if len(self.position_history) < 3:
            return False
            
        # Obtener las posiciones x,y de los últimos frames
        positions = np.array(self.position_history)[:, :2]
        
        # Calcular la varianza de las posiciones x e y
        variance_x = np.var(positions[:, 0])
        variance_y = np.var(positions[:, 1])
        
        # Calcular distancia máxima desde la posición actual
        max_dist = np.max(np.linalg.norm(positions - positions[-1], axis=1))
        
        # Una posición es estable si tanto la varianza como la distancia máxima son pequeñas
        # Esto permite pequeñas vibraciones naturales sin falsos positivos
        is_stable = max_dist < self.stability_threshold and variance_x < 100 and variance_y < 100
        
        return is_stable

    def is_position_stable(self, current_position):
        return self._is_position_stable()

    def reset(self):
        self.position_history.clear()
        self.start_time = None
        self.selected_cell = None
        self.kalman.statePre = np.zeros((4, 1), np.float32)
        self.kalman.statePost = np.zeros((4, 1), np.float32)
        self.smoothed_pos = None
        self.last_valid_position = None
        self.position_valid_count = 0
        self.cell_memory = None
        self.cell_memory_counter = 0


def main():
    cap = cv2.VideoCapture(1)
    if not cap.isOpened():
        print("Error al abrir cámara")
        return
    
    ret, frame = cap.read()
    if not ret:
        print("Error al leer frame")
        return
    
    height, width = frame.shape[:2]
    grid_system = GridSystem(width, height, 40)
    detector = FingerPositionDetector(grid_system)
    
    selected_cells = []
    
    cv2.namedWindow('Detección de Dedos', cv2.WINDOW_NORMAL)
    
    while True:
        ret, frame = cap.read()
        if not ret:
            break
        
        output, pos, confirmed, cell = detector.process_frame(frame, None, selected_cells)
        
        if confirmed and cell and cell not in selected_cells:
            selected_cells.append(cell)
            print(f"Celda seleccionada: {cell}")
        
        cv2.imshow('Detección de Dedos', output)
        
        key = cv2.waitKey(1)
        if key == ord('q'):
            break
        elif key == ord('r'):
            detector.reset()
            selected_cells = []
    
    cap.release()
    cv2.destroyAllWindows()


if __name__ == "__main__":
    main()
