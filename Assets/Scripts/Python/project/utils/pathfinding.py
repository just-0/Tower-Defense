import heapq
import numpy as np
import cv2
from matplotlib import pyplot as plt

def astar(mask, debug=False):
    height, width = mask.shape
    start = (height // 2, width - 1)  # (y, x) -> derecha al medio
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
        
        # Guardar imagen
        cv2.imwrite("astar_debug.png", debug_img)
        print("Imagen de debug guardada como 'astar_debug.png'")
    
    return path

def handle_astar_from_mask(mask_bytes, debug=False):
    """
    Decode mask, compute binary mask, run A*, and prepare path for Unity.
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
    path = astar(binary_mask, debug=debug)

    if not path:
        print("No se encontró camino con A*.")
        return None

    print(f"Camino calculado. Puntos: {len(path)}.")
    return path