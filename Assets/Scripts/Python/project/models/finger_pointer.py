import cv2
import mediapipe as mp
import numpy as np
import time
import os


class GridSystem:
    def __init__(self, width, height, grid_size=30):
        """
        Inicializa el sistema de cuadrícula optimizado
        
        Args:
            width: Ancho de la imagen en píxeles
            height: Alto de la imagen en píxeles
            grid_size: Tamaño de cada celda de la cuadrícula en píxeles
        """
        self.grid_size = grid_size
        self.width = width
        self.height = height
        
        # Calcular número de celdas en cada dirección
        self.cols = width // grid_size
        self.rows = height // grid_size
        
        # Matriz para almacenar si una celda está ocupada (objeto) o no
        self.grid_matrix = np.zeros((self.rows, self.cols), dtype=bool)
        
        # Cargar la máscara si existe
        self.mask = None
        self.load_mask()
        
        # Pre-calcular las coordenadas de las celdas para dibujo
        self.cell_coords = self._precalculate_cell_coords()
    
    def load_mask(self):
        """Carga la máscara desde el archivo y actualiza la matriz de la cuadrícula"""
        # Lista de posibles ubicaciones para la máscara
        possible_paths = [
            "./debug_mask_final.png",
            "../debug_mask_final.png",
            "../../debug_mask_final.png",
            "/home/just/Documents/TestUnity/Tower-Defense/Assets/Scripts/Python/debug_mask_final.png",
            "/home/just/Documents/TestUnity/Tower-Defense/Assets/Scripts/debug_mask_final.png"
        ]
        
        mask_loaded = False
        
        print("DEBUG-MASK: Intentando cargar máscara...")
        
        # Intentar cargar desde cada ubicación posible
        for mask_path in possible_paths:
            if os.path.exists(mask_path):
                print(f"DEBUG-MASK: Encontrada máscara en {mask_path}")
                self.mask = cv2.imread(mask_path, cv2.IMREAD_GRAYSCALE)
                
                if self.mask is None:
                    print(f"ERROR-MASK: La máscara en {mask_path} no se pudo leer correctamente")
                    continue
                
                # Redimensionar la máscara si no coincide con las dimensiones actuales
                if self.mask.shape[0] != self.height or self.mask.shape[1] != self.width:
                    print(f"DEBUG-MASK: Redimensionando máscara de {self.mask.shape} a {self.width}x{self.height}")
                    self.mask = cv2.resize(self.mask, (self.width, self.height))
                
                # Actualizar la matriz de la cuadrícula basada en la máscara
                self._update_grid_from_mask()
                print(f"DEBUG-MASK: Máscara cargada con éxito: {self.mask.shape}")
                
                # Guardar una copia de la máscara para depuración
                try:
                    cv2.imwrite("debug_mask_loaded.png", self.mask)
                    print("DEBUG-MASK: Copia de máscara guardada como debug_mask_loaded.png")
                except Exception as e:
                    print(f"ERROR-MASK: No se pudo guardar copia de la máscara: {e}")
                
                mask_loaded = True
                break
        
        if not mask_loaded:
            print("ERROR-MASK: No se encontró la máscara en ninguna ubicación")
            print("DEBUG-MASK: Directorio actual:", os.getcwd())
            print("DEBUG-MASK: Contenido del directorio:")
            try:
                for item in os.listdir("."):
                    print(f"  - {item}")
            except Exception as e:
                print(f"ERROR-MASK: No se pudo listar el directorio: {e}")
            
            # Crear una máscara vacía para evitar errores
            print("DEBUG-MASK: Creando máscara vacía")
            self.mask = np.zeros((self.height, self.width), dtype=np.uint8)
            self._update_grid_from_mask()
    
    def _update_grid_from_mask(self):
        """Actualiza la matriz de la cuadrícula basada en la máscara - versión optimizada"""
        if self.mask is None:
            return
        
        # Redimensionar la máscara al tamaño de la cuadrícula para procesamiento más rápido
        small_mask = cv2.resize(self.mask, (self.cols, self.rows), interpolation=cv2.INTER_AREA)
        
        # Las celdas con valor promedio < 128 se consideran ocupadas (objeto)
        self.grid_matrix = small_mask < 128
    
    def _precalculate_cell_coords(self):
        """Pre-calcula todas las coordenadas de las celdas para optimizar el dibujo"""
        coords = {}
        for row in range(self.rows):
            for col in range(self.cols):
                x1 = col * self.grid_size
                y1 = row * self.grid_size
                x2 = min(x1 + self.grid_size, self.width)
                y2 = min(y1 + self.grid_size, self.height)
                cx = int((col + 0.5) * self.grid_size)
                cy = int((row + 0.5) * self.grid_size)
                coords[(row, col)] = (x1, y1, x2, y2, cx, cy)
        return coords
    
    def get_grid_cell(self, x, y):
        """Obtiene la celda de la cuadrícula que contiene el punto (x, y)"""
        if 0 <= x < self.width and 0 <= y < self.height:
            col = x // self.grid_size
            row = y // self.grid_size
            
            if 0 <= row < self.rows and 0 <= col < self.cols:
                return (row, col)
        return None
    
    def is_cell_occupied(self, row, col):
        """Comprueba si una celda contiene un objeto (basado en la máscara)"""
        if 0 <= row < self.rows and 0 <= col < self.cols:
            return self.grid_matrix[row, col]
        return False
    
    def get_cell_center(self, row, col):
        """Obtiene el centro de una celda de la cuadrícula usando las coordenadas pre-calculadas"""
        if (row, col) in self.cell_coords:
            _, _, _, _, cx, cy = self.cell_coords[(row, col)]
            return (cx, cy)
        return None
    
    def draw_grid(self, image, selected_cells=None):
        """
        Dibuja la cuadrícula en la imagen con información visual mejorada
        """
        # Crear una capa para la cuadrícula y celdas ocupadas
        grid_overlay = np.zeros_like(image, dtype=np.uint8)
        
        # Dibujar todas las celdas para mayor visibilidad
        for row in range(self.rows):
            for col in range(self.cols):
                x1, y1, x2, y2, cx, cy = self.cell_coords[(row, col)]
                
                # Color según si la celda está ocupada o no
                if self.grid_matrix[row, col]:
                    # Celda ocupada (roja)
                    cell_color = (0, 0, 200)  # Rojo en BGR
                    border_color = (0, 0, 255)  # Rojo brillante
                else:
                    # Celda libre (verde)
                    cell_color = (0, 200, 0)  # Verde en BGR
                    border_color = (0, 255, 0)  # Verde brillante
                
                # Dibujar el relleno y borde de la celda
                cv2.rectangle(grid_overlay, (x1, y1), (x2, y2), cell_color, -1)
                cv2.rectangle(grid_overlay, (x1, y1), (x2, y2), border_color, 1)
        
        # Combinar overlay con la imagen original
        cv2.addWeighted(grid_overlay, 0.4, image, 1.0, 0, image)  # Mayor opacidad (0.4)
        
        # Dibujar las celdas seleccionadas si hay alguna
        if selected_cells:
            for row, col in selected_cells:
                if (row, col) in self.cell_coords:
                    x1, y1, x2, y2, _, _ = self.cell_coords[(row, col)]
                    cv2.rectangle(image, (x1, y1), (x2, y2), (255, 0, 255), 2)
                    # Añadir texto "SELECCIONADO"
                    cv2.putText(image, "SELECCIONADO", (x1, y1-5), 
                               cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 0, 255), 1)
        
        # Añadir texto de ayuda
        cv2.putText(image, "Verde = Disponible, Rojo = Ocupado", (10, 20), 
                   cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
        
        return image


class FingerPositionDetector:
    def __init__(self, grid_system):
        # Inicializar MediaPipe Hands con configuración para mejor rendimiento
        self.mp_hands = mp.solutions.hands
        self.hands = self.mp_hands.Hands(
            static_image_mode=False,
            max_num_hands=1,
            min_detection_confidence=0.7,
            min_tracking_confidence=0.5
        )
        self.mp_drawing = mp.solutions.drawing_utils
        
        # Opciones de dibujo optimizadas para MediaPipe
        self.drawing_spec = self.mp_drawing.DrawingSpec(thickness=1, circle_radius=1)
        
        # Sistema de cuadrícula
        self.grid_system = grid_system
        
        # Variables para el seguimiento de la posición
        self.last_position = None
        self.start_time = None
        self.position_confirmed = False
        self.confirmation_duration = 1.0  # Reducido a 1 segundo para confirmar
        self.stability_threshold = 10     # Aumentado a 10 píxeles para ser más permisivo
        self.depth_threshold = 15         # Umbral para cambios en profundidad
        
        # Buffer para posiciones recientes (reducido para optimización)
        self.position_buffer = []
        self.buffer_size = 3  # Reducido de 5 a 3
        
        # Punto seleccionado
        self.selected_point = None
        self.selected_cell = None
        
        # Variables para detectar el estado del dedo
        self.is_pointing = False
        self.current_cell = None
        
        # Pre-calculo del arco para el progreso (optimización)
        self.arc_angles = [((-90, -90 + 360 * (i/10)), (0, 255, 255)) for i in range(11)]
    
    def process_frame(self, color_frame, depth_frame=None, selected_cells=None):
        """
        Procesa cada frame para detectar la posición del dedo índice - versión optimizada
        
        Args:
            color_frame: Frame de color
            depth_frame: Frame de profundidad (opcional)
            selected_cells: Lista de celdas seleccionadas (opcional)
            
        Returns:
            tuple: (output_image, current_position, is_confirmed, selected_cell)
        """
        # Crear una copia superficial (no profunda) para optimizar memoria
        output_image = color_frame.copy()
        
        # Variables para almacenar la posición actual
        current_position = None
        is_confirmed = False
        selected_cell = None
        
        try:
            # Dibujar la cuadrícula con más eficiencia
            output_image = self.grid_system.draw_grid(output_image, selected_cells)
            
            # Procesar imagen con MediaPipe (directamente en formato BGR)
            # MediaPipe espera RGB, asegurar la conversión correcta
            if color_frame.shape[2] == 3:  # Si tiene 3 canales
                rgb_frame = cv2.cvtColor(color_frame, cv2.COLOR_BGR2RGB)
            else:
                rgb_frame = color_frame
            
            results = self.hands.process(rgb_frame)
            
            # Verificar si se detectaron manos
            if results.multi_hand_landmarks:
                hand_landmarks = results.multi_hand_landmarks[0]  # Solo procesamos la primera mano
                
                # Dibujar los landmarks de la mano con opciones más ligeras
                self.mp_drawing.draw_landmarks(
                    output_image, 
                    hand_landmarks, 
                    self.mp_hands.HAND_CONNECTIONS,
                    self.drawing_spec,
                    self.drawing_spec
                )
                
                # Extraer directamente los puntos de interés para evitar cálculos repetidos
                height, width = color_frame.shape[:2]
                
                # Obtener la punta del dedo índice (landmark #8)
                index_finger_tip = hand_landmarks.landmark[8]
                index_finger_mcp = hand_landmarks.landmark[5]  # Base del dedo índice
                middle_finger_tip = hand_landmarks.landmark[12]  # Dedo medio
                middle_finger_mcp = hand_landmarks.landmark[9]  # Base del dedo medio
                
                # Calcular coordenadas una sola vez
                x = int(index_finger_tip.x * width)
                y = int(index_finger_tip.y * height)
                
                # Obtener profundidad de manera más eficiente
                z = 0
                if depth_frame is not None and 0 <= y < depth_frame.shape[0] and 0 <= x < depth_frame.shape[1]:
                    # Simplificar la obtención de profundidad
                    z = depth_frame[y, x] if depth_frame[y, x] > 0 else 0
                
                current_position = (x, y, z)
                
                # Detección más permisiva del gesto de apuntar
                is_index_extended = index_finger_tip.y < index_finger_mcp.y
                # Consideramos que está apuntando si el dedo índice está extendido
                # Sin importar tanto la posición del dedo medio
                self.is_pointing = is_index_extended
                
                # Obtener celda y validarla en un solo paso
                cell = self.grid_system.get_grid_cell(x, y)
                self.current_cell = cell
                # Una celda es válida si NO está ocupada (es decir, si está disponible para colocar algo)
                cell_is_valid = cell and not self.grid_system.is_cell_occupied(*cell)
                
                if cell:
                    print(f"DEBUG-FINGER: Celda detectada: {cell}, válida: {cell_is_valid}")
                
                # Dibujar indicadores de posición
                circle_color = (0, 255, 0) if cell_is_valid else (0, 0, 255)
                cv2.circle(output_image, (x, y), 10, circle_color, -1)
                
                # Lógica para determinar si el dedo está estable
                # Solo procesamos si el dedo está apuntando, la celda es válida (no ocupada) y la posición es estable
                is_stable = self.is_position_stable(current_position)
                print(f"DEBUG-FINGER: Apuntando: {self.is_pointing}, Celda válida: {cell_is_valid}, Estable: {is_stable}")
                
                if self.is_pointing and cell_is_valid and is_stable:
                    # Si acaba de comenzar a estar estable
                    if self.start_time is None:
                        self.start_time = time.time()
                        print(f"DEBUG-FINGER: Iniciando temporizador de estabilidad")
                    
                    # Calcular tiempo estable
                    elapsed_time = time.time() - self.start_time
                    print(f"DEBUG-FINGER: Tiempo estable: {elapsed_time:.2f}s / {self.confirmation_duration:.2f}s")
                    
                    # Mostrar el progreso de la confirmación
                    progress = min(elapsed_time / self.confirmation_duration, 1.0)
                    cv2.circle(output_image, (x, y), 30, (0, 165, 255), 2)
                    
                    # Dibujar arco de progreso (usando valores pre-calculados)
                    progress_idx = int(progress * 10)
                    start_angle, end_angle = self.arc_angles[progress_idx][0]
                    arc_color = self.arc_angles[progress_idx][1]
                    cv2.ellipse(output_image, (x, y), (30, 30), 0, start_angle, end_angle, arc_color, 3)
                    
                    # Si ha estado estable por suficiente tiempo
                    if elapsed_time >= self.confirmation_duration and not self.position_confirmed:
                        self.position_confirmed = True
                        self.selected_point = current_position
                        self.selected_cell = cell
                        is_confirmed = True
                        selected_cell = cell
                        print(f"DEBUG-FINGER: ¡CELDA CONFIRMADA! {cell}")
                        print(f"DEBUG-FINGER: Punto seleccionado: {current_position}")
                        print(f"DEBUG-FINGER: Estado de confirmación: {is_confirmed}")
                        
                    # Mostrar el tiempo restante
                    if not self.position_confirmed:
                        remaining = max(0, self.confirmation_duration - elapsed_time)
                        cv2.putText(output_image, f"{remaining:.1f}s", (x+30, y-10), 
                                    cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 255), 2)
                else:
                    # Resetear el temporizador si la posición cambió demasiado
                    self.start_time = None
                    self.position_confirmed = False
            else:
                # No se detectó ninguna mano
                self.start_time = None
                self.position_confirmed = False
            
            # Dibujar el punto seleccionado si existe
            if self.selected_point and self.selected_cell:
                x, y, _ = self.selected_point
                row, col = self.selected_cell
                
                # Resaltar la celda seleccionada si está en las coordenadas pre-calculadas
                if (row, col) in self.grid_system.cell_coords:
                    x1, y1, x2, y2, cx, cy = self.grid_system.cell_coords[(row, col)]
                    
                    # Dibujar resaltado de selección
                    overlay = np.zeros_like(output_image, dtype=np.uint8)
                    cv2.rectangle(overlay, (x1, y1), (x2, y2), (0, 0, 255), -1)
                    cv2.addWeighted(overlay, 0.4, output_image, 1.0, 0, output_image)
                    
                    # Contorno y punto central
                    cv2.rectangle(output_image, (x1, y1), (x2, y2), (0, 0, 255), 2)
                    cv2.circle(output_image, (cx, cy), 5, (0, 0, 255), -1)
                
                # Dibujar punto original de selección
                cv2.circle(output_image, (x, y), 15, (0, 0, 255), -1)
            
            # Si se confirma un nuevo punto, reiniciar para permitir nuevas selecciones
            if is_confirmed:
                self.position_confirmed = False
                self.start_time = None
            
            # Actualizar la última posición
            self.last_position = current_position
            
        except Exception as e:
            import traceback
            traceback.print_exc()
            print(f"Error en process_frame: {e}")
        
        # Siempre devolver los 4 valores, incluso si hay error
        return output_image, current_position, is_confirmed, selected_cell
    
    def is_position_stable(self, current_position):
        """Determina si la posición actual es estable - versión más permisiva"""
        if self.last_position is None or current_position is None:
            return False
        
        # Agregar posición al buffer
        self.position_buffer.append(current_position)
        if len(self.position_buffer) > self.buffer_size:
            self.position_buffer.pop(0)
        
        # Verificación rápida si no hay suficientes datos
        if len(self.position_buffer) < 2:  # Reducido a solo necesitar 2 posiciones
            print(f"DEBUG-STABLE: Buffer insuficiente: {len(self.position_buffer)}/{self.buffer_size}")
            return False
        
        # Cálculo optimizado de estabilidad usando NumPy
        positions = np.array(self.position_buffer)
        variance = np.var(positions[:, :2], axis=0)  # Solo X e Y
        
        # Calcular la varianza máxima para depuración
        max_var = np.max(variance)
        threshold = self.stability_threshold**2
        
        # La posición es estable si hay poca variación
        is_stable = np.all(variance < threshold)
        
        # Siempre imprimir información de depuración
        print(f"DEBUG-STABLE: Varianza: {max_var:.2f}, Umbral: {threshold}, Estable: {is_stable}")
            
        return is_stable

    def reset(self):
        """Resetea las variables de seguimiento"""
        self.last_position = None
        self.start_time = None
        self.position_confirmed = False
        self.selected_point = None
        self.selected_cell = None
        self.position_buffer = []


def main():
    print("DEBUG-MAIN: Iniciando prueba manual de detección de dedos")
    
    # Intentar con diferentes índices de cámara
    camera_indices = [0, 1, 2]
    cap = None
    
    for idx in camera_indices:
        print(f"DEBUG-MAIN: Intentando abrir cámara {idx}")
        cap = cv2.VideoCapture(idx)
        if cap.isOpened():
            print(f"DEBUG-MAIN: Cámara {idx} abierta con éxito")
            break
    
    # Verificar que la cámara se abrió correctamente
    if not cap or not cap.isOpened():
        print("ERROR-MAIN: No se pudo abrir ninguna cámara.")
        return
    
    # Obtener dimensiones de la cámara
    ret, test_frame = cap.read()
    if not ret:
        print("ERROR-MAIN: Error al leer frame de la cámara.")
        return
    
    height, width, _ = test_frame.shape
    print(f"DEBUG-MAIN: Dimensiones de la cámara: {width}x{height}")
    
    # Inicializar el sistema de cuadrícula
    grid_size = 40  # Tamaño de cada celda en píxeles (aumentado para mejor visibilidad)
    grid_system = GridSystem(width, height, grid_size)
    
    # Verificar si se cargó la máscara correctamente
    if grid_system.mask is None:
        print("ERROR-MAIN: La máscara no se cargó correctamente")
    else:
        print(f"DEBUG-MAIN: Máscara cargada con dimensiones {grid_system.mask.shape}")
        # Guardar una copia de la máscara para verificación
        cv2.imwrite("debug_main_mask.png", grid_system.mask)
        print("DEBUG-MAIN: Máscara guardada como debug_main_mask.png")
        
    # Mostrar información sobre la cuadrícula
    print(f"DEBUG-MAIN: Cuadrícula creada con {grid_system.rows} filas y {grid_system.cols} columnas")
    print(f"DEBUG-MAIN: Tamaño de celda: {grid_size}x{grid_size} píxeles")
    
    # Inicializar el detector con el sistema de cuadrícula
    detector = FingerPositionDetector(grid_system)
    
    # Crear ventana optimizada para visualización
    window_name = 'Detección de Dedos - Prueba Manual'
    cv2.namedWindow(window_name, cv2.WINDOW_NORMAL)
    cv2.resizeWindow(window_name, width, height)
    
    # Lista para almacenar las celdas seleccionadas
    selected_cells = []
    
    print("DEBUG-MAIN: Iniciando bucle de procesamiento de frames")
    print("DEBUG-MAIN: Presiona 'q' para salir, 'r' para resetear selecciones")
    
    try:
        last_frame_time = time.time()
        frame_count = 0
        
        while True:
            current_time = time.time()
            
            # Leer frame de la cámara
            ret, frame = cap.read()
            if not ret:
                print("ERROR-MAIN: Error al leer frame de la cámara")
                break
            
            # Procesar el frame
            output_frame, position, is_confirmed, selected_cell = detector.process_frame(
                frame, None, selected_cells
            )
            
            # Si se confirmó una celda, agregarla a la lista de celdas seleccionadas
            if is_confirmed and selected_cell and selected_cell not in selected_cells:
                row, col = selected_cell
                print(f"DEBUG-MAIN: ¡Celda confirmada: [{row}, {col}]!")
                selected_cells.append(selected_cell)
            
            # Añadir información de depuración al frame
            frame_count += 1
            cv2.putText(output_frame, f"Frame: {frame_count}", (10, height - 10), 
                       cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
            
            if position:
                cv2.putText(output_frame, f"Posición: {position}", (10, height - 30), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
            
            if detector.is_pointing:
                cv2.putText(output_frame, "Apuntando: SÍ", (10, height - 50), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 255, 0), 1)
            else:
                cv2.putText(output_frame, "Apuntando: NO", (10, height - 50), 
                           cv2.FONT_HERSHEY_SIMPLEX, 0.5, (0, 0, 255), 1)
            
            # Mostrar el resultado
            cv2.imshow(window_name, output_frame)
            
            # Salir con 'q'
            key = cv2.waitKey(1) & 0xFF
            if key == ord('q') or key == 27:  # q o ESC
                break
            
            # Resetear con 'r'
            if key == ord('r'):
                detector.reset()
                selected_cells = []
                print("DEBUG-MAIN: Detector y selecciones reseteados")
            
            # Guardar frame con 's'
            if key == ord('s'):
                filename = f"debug_frame_{frame_count}.png"
                cv2.imwrite(filename, output_frame)
                print(f"DEBUG-MAIN: Frame guardado como {filename}")
            
            # Limitar la tasa de fotogramas
            elapsed = time.time() - current_time
            if elapsed < 1/30:  # Máximo 30 FPS
                time.sleep(1/30 - elapsed)
    
    except Exception as e:
        import traceback
        traceback.print_exc()
        print(f"ERROR-MAIN: Error en la ejecución: {e}")
    
    finally:
        # Liberar recursos
        print("DEBUG-MAIN: Cerrando aplicación...")
        cap.release()
        cv2.destroyAllWindows()

if __name__ == "__main__":
    # Configuración para mejorar el rendimiento general
    cv2.setNumThreads(4)  # Limitar hilos de OpenCV
    
    try:
        main()
    except KeyboardInterrupt:
        print("Programa interrumpido por el usuario")
    except Exception as e:
        print(f"Error: {e}")
    finally:
        cv2.destroyAllWindows()