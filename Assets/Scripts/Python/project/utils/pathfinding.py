import heapq
import numpy as np
import cv2
from matplotlib import pyplot as plt

def astar(mask, debug=False, goal=None):
    height, width = mask.shape
    start = (height // 2, width - 1)  # (y, x) -> derecha al medio
    if goal is None:
        goal = (height // 2, 0)           # izquierda al medio

    def heuristic(a, b):
        return abs(a[0] - b[0]) + abs(a[1] - b[1])

    def neighbors(pos):
        y, x = pos
        for dy, dx in [(-1,0), (1,0), (0,-1), (0,1)]:
            ny, nx = y + dy, x + dx
            if 0 <= ny < height and 0 <= nx < width and mask[ny, nx] == 0:
                yield (ny, nx)

    frontier = []
    heapq.heappush(frontier, (0, start))
    came_from = {start: None}
    cost_so_far = {start: 0}
    
    # Para debug: registrar todos los nodos visitados
    visited_nodes = set()
    explored_nodes = []

    while frontier:
        _, current = heapq.heappop(frontier)
        visited_nodes.add(current)
        explored_nodes.append(current)

        if current == goal:
            break

        for next in neighbors(current):
            new_cost = cost_so_far[current] + 1
            if next not in cost_so_far or new_cost < cost_so_far[next]:
                cost_so_far[next] = new_cost
                priority = new_cost + heuristic(goal, next)
                heapq.heappush(frontier, (priority, next))
                came_from[next] = current

    # Reconstruir camino
    path = []
    current = goal
    while current and current in came_from:
        path.append((current[1], current[0]))  # convertir a (x, y)
        current = came_from[current]
    path.reverse()
    
    # Generar imagen de debug si se solicita
    if debug:
        debug_img = np.zeros((height, width, 3), dtype=np.uint8)
        
        # Dibujar la máscara (blanco para obstáculos, negro para espacio libre)
        debug_img[mask == 1] = [255, 255, 255]
        
        # Dibujar nodos explorados (azul claro)
        for node in explored_nodes:
            debug_img[node[0], node[1]] = [200, 200, 255]
            
        # Dibujar nodos en el camino final (verde)
        for x, y in path:
            debug_img[y, x] = [0, 255, 0]
            
        # Dibujar inicio (rojo) y meta (azul)
        debug_img[start[0], start[1]] = [0, 0, 255]  # Rojo (BGR)
        debug_img[goal[0], goal[1]] = [255, 0, 0]    # Azul (BGR)
        
        # Mostrar imagen
        plt.figure(figsize=(10, 10))
        plt.imshow(cv2.cvtColor(debug_img, cv2.COLOR_BGR2RGB))
        plt.title(f"Camino A* - {len(path)} puntos")
        plt.axis('off')
        plt.show()
        
        print("2 Imagen de debug")
        # Guardar imagen
        cv2.imwrite("astar_debug.png", debug_img)
        
        print("Imagen de debug guardada como 'astar_debug.png'")
    
    return path

def handle_astar_from_mask(mask_bytes, debug=False, goal=None):
    """
    Decode mask, compute binary mask, run A*, and prepare path for Unity.
    goal: (y, x) en coordenadas de la máscara (no de la imagen original)
    """
    # Convertir bytes a imagen OpenCV
    mask_array = np.frombuffer(mask_bytes, np.uint8)
    mask_image = cv2.imdecode(mask_array, cv2.IMREAD_GRAYSCALE)

    if mask_image is None:
        print("Error: no se pudo decodificar la imagen de máscara.")
        return None

    # Binarizar: fondo = 0, objeto = 1
    _, binary_mask = cv2.threshold(mask_image, 127, 1, cv2.THRESH_BINARY_INV)

    # Calcular A* con modo debug
    path = astar(binary_mask, debug=debug, goal=goal)
    if not path:
        print("No se encontró camino con A*.")
        return None

    print(f"Camino calculado. Puntos: {len(path)}.")
    return path

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
                # Celda ocupada (camino) - Solo dibujar borde sutil sin relleno
                cv2.rectangle(grid_overlay, (x1, y1), (x2, y2), (100, 100, 100), 1)
            else:
                # Celda libre (objeto) - Colorear de verde
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
    cv2.putText(image, "Verde = Disponible para colocar armas", (10, 20), 
               cv2.FONT_HERSHEY_SIMPLEX, 0.5, (255, 255, 255), 1)
    
    return image