"""
WebSocket server for real-time communication.
"""

import asyncio
import websockets
import json
import threading
import cv2
import time

from utils.camera import CameraManager
from utils.image_processings import encode_frame_to_jpeg
from utils.finger_tracking import FingerCounter
from models.sam_model import SAMProcessor
from utils.pathfinding import handle_astar_from_mask
from models.finger_pointer import GridSystem, FingerPositionDetector

from config.settings import (
    WEBSOCKET_HOST, WEBSOCKET_PORT, FINGER_TRACKING_PORT, TRANSMISSION_FPS,
    MESSAGE_TYPE_CAMERA_FRAME, MESSAGE_TYPE_MASK, MESSAGE_TYPE_PATH, 
    MESSAGE_TYPE_FINGER_COUNT, FINGER_CAMERA_INDEX, FINGER_CAMERA_WIDTH,
    FINGER_CAMERA_HEIGHT, FINGER_CAMERA_FPS, FINGER_TRANSMISSION_FPS,
    MESSAGE_TYPE_GRID_POSITION, CAMERA_INDEX, CAMERA_WIDTH, CAMERA_HEIGHT,
    CAMERA_FPS
)

class WebSocketServer:
    """
    WebSocket server for handling client connections and processing requests.
    """
    
    def __init__(self):
        """Initialize the WebSocket server."""
        self.server = None
        self.finger_server = None
        self.finger_counter = FingerCounter(
            camera_index=FINGER_CAMERA_INDEX,
            width=FINGER_CAMERA_WIDTH,
            height=FINGER_CAMERA_HEIGHT,
            fps=FINGER_CAMERA_FPS
        )
        
    async def start(self):
        """Start the WebSocket servers."""
        # Start the main WebSocket server
        self.server = await websockets.serve(
            self.handle_client, 
            WEBSOCKET_HOST, 
            WEBSOCKET_PORT
        )
        print(f"Main WebSocket server started at ws://{WEBSOCKET_HOST}:{WEBSOCKET_PORT}")
        
        # Start the finger tracking WebSocket server
        self.finger_server = await websockets.serve(
            self.handle_finger_client,
            WEBSOCKET_HOST,
            FINGER_TRACKING_PORT
        )
        print(f"Finger tracking WebSocket server started at ws://{WEBSOCKET_HOST}:{FINGER_TRACKING_PORT}")
        
        # Start the finger tracking camera
        self.finger_counter.start_camera()
        
        # Wait for both servers to complete (they will run indefinitely)
        await asyncio.gather(
            self.server.wait_closed(),
            self.finger_server.wait_closed()
        )
        
    async def handle_finger_client(self, websocket):
        """
        Handle a client connection for finger tracking.
        
        Args:
            websocket: WebSocket connection object
        """
        print("New finger tracking client connected")
        finger_frame_task = None
        finger_count_task = None
        
        try:
            # Start sending finger frames and counts immediately upon connection
            finger_frame_task = asyncio.create_task(
                self.send_finger_frames(websocket)
            )
            
            finger_count_task = asyncio.create_task(
                self.send_finger_counts(websocket)
            )
            
            # Keep the connection alive and handle any potential messages
            async for message in websocket:
                if isinstance(message, str):
                    print(f"Received finger tracking command: {message}")
                    # Process any additional commands if needed in the future
                    
        except websockets.exceptions.ConnectionClosed:
            print("Finger tracking client disconnected")
        finally:
            # Ensure tasks are canceled if they exist
            if finger_frame_task and not finger_frame_task.done():
                finger_frame_task.cancel()
            
            if finger_count_task and not finger_count_task.done():
                finger_count_task.cancel()
                
    async def send_finger_frames(self, websocket):
        """
        Send finger tracking camera frames to the client.
        
        Args:
            websocket: WebSocket connection object
        """
        try:
            while self.finger_counter.is_running:
                frame = self.finger_counter.get_current_frame()
                if frame is not None:
                    # Encode the frame as JPEG
                    success, encoded_frame = encode_frame_to_jpeg(frame)
                    if success:
                        # Send camera frame (type 1)
                        await websocket.send(bytes([MESSAGE_TYPE_CAMERA_FRAME]) + encoded_frame)
                
                # Control frame rate
                await asyncio.sleep(1/TRANSMISSION_FPS)
                
        except (websockets.exceptions.ConnectionClosed, asyncio.CancelledError):
            print("Finger camera frame sending stopped")
    
    async def send_finger_counts(self, websocket):
        """
        Send finger count updates to the client.
        
        Args:
            websocket: WebSocket connection object
        """
        try:
            while self.finger_counter.is_running:
                # Get current finger count
                finger_count = self.finger_counter.get_finger_count()
                
                # Create finger count message
                finger_data = {"count": finger_count}
                finger_json = json.dumps(finger_data)
                
                # Send finger count (type 5)
                await websocket.send(bytes([MESSAGE_TYPE_FINGER_COUNT]) + finger_json.encode('utf-8'))
                
                # Control update rate
                await asyncio.sleep(1/FINGER_TRANSMISSION_FPS)
                
        except (websockets.exceptions.ConnectionClosed, asyncio.CancelledError):
            print("Finger count sending stopped")
                
    async def handle_client(self, websocket):
        """
        Handle a client connection for SAM processing.
        
        Args:
            websocket: WebSocket connection object
        """
        camera_manager = CameraManager()
        sam_processor = SAMProcessor()
        send_frames = False
        frame_task = None
        combat_task = None
        grid_system = None
        finger_detector = None
        combat_mode_active = False
        
        print("New SAM client connected")
        
        try:
            async for message in websocket:
                if isinstance(message, str):
                    print(f"Received SAM command: {message}")
                    
                    if message == "START_CAMERA" and not combat_mode_active:
                        camera_manager.start_camera()
                        send_frames = True
                        
                        # Start sending frames in a separate task
                        if frame_task is None or frame_task.done():
                            frame_task = asyncio.create_task(
                                self.send_camera_frames(websocket, camera_manager)
                            )
                        
                    elif message == "STOP_CAMERA":
                        send_frames = False
                        if not combat_mode_active and camera_manager.is_running:
                            camera_manager.stop_camera()
                            if frame_task and not frame_task.done():
                                frame_task.cancel()
                        
                    elif message == "PROCESS_SAM":
                        await self.process_sam(websocket, camera_manager, sam_processor)
                        
                    elif message == "START_COMBAT":
                        # Detener cámara normal si está activa
                        if send_frames and frame_task and not frame_task.done():
                            frame_task.cancel()
                        if camera_manager.is_running:
                            camera_manager.stop_camera()
                        
                        combat_mode_active = True
                        
                        # Initialize grid system and finger detector
                        if grid_system is None:
                            grid_system = GridSystem(CAMERA_WIDTH, CAMERA_HEIGHT)
                            finger_detector = FingerPositionDetector(grid_system)
                        
                        # Start combat mode task
                        if combat_task is None or combat_task.done():
                            combat_task = asyncio.create_task(
                                self.handle_combat_mode(websocket, finger_detector)
                            )
                            
                    elif message == "STOP_COMBAT":
                        combat_mode_active = False
                        if combat_task and not combat_task.done():
                            combat_task.cancel()
                            print("Modo combate detenido")
                        
                        # Reiniciar la cámara normal si estaba activa antes
                        if send_frames and not camera_manager.is_running:
                            camera_manager.start_camera()
                            if frame_task is None or frame_task.done():
                                frame_task = asyncio.create_task(
                                    self.send_camera_frames(websocket, camera_manager)
                                )
                    
        except websockets.exceptions.ConnectionClosed:
            print("SAM client disconnected")
        finally:
            # Cleanup resources
            if camera_manager.is_running:
                camera_manager.stop_camera()
            if frame_task and not frame_task.done():
                frame_task.cancel()
            if combat_task and not combat_task.done():
                combat_task.cancel()
                
    async def process_sam(self, websocket, camera_manager, sam_processor):
        """Process the current frame with SAM and send the result."""
        frame = camera_manager.get_current_frame()
        if frame is None:
            return
            
        # Process the frame with SAM
        mask_bytes = sam_processor.process_image(frame)
        if not mask_bytes:
            return
            
        # Send the mask
        await websocket.send(bytes([MESSAGE_TYPE_MASK]) + mask_bytes)
        print("Sent mask data")
        
        # Process and send A* path
        path = handle_astar_from_mask(mask_bytes, False)
        if path:
            path_data = [{"x": x, "y": y} for x, y in path]
            path_json = json.dumps(path_data)
            try:
                await websocket.send(bytes([MESSAGE_TYPE_PATH]) + path_json.encode('utf-8'))
                print(f"Sent A* path with {len(path)} points")
            except Exception as e:
                print(f"Error sending A* path: {e}")
            
    async def send_camera_frames(self, websocket, camera_manager):
        """Send camera frames to the client."""
        try:
            while camera_manager.is_running:
                frame = camera_manager.get_current_frame()
                if frame is not None:
                    success, encoded_frame = encode_frame_to_jpeg(frame)
                    if success:
                        await websocket.send(bytes([MESSAGE_TYPE_CAMERA_FRAME]) + encoded_frame)
                await asyncio.sleep(1/TRANSMISSION_FPS)
        except (websockets.exceptions.ConnectionClosed, asyncio.CancelledError):
            print("Camera frame sending stopped")
            
    async def handle_combat_mode(self, websocket, finger_detector):
        """Handle the combat mode with finger detection."""
        cap = None
        try:
            print(f"DEBUG-CAMERA: Intentando abrir cámara con índice {CAMERA_INDEX}")
            # Abrir cámara para modo combate
            cap = cv2.VideoCapture(CAMERA_INDEX)
            
            # Verificar si la cámara está abierta
            if not cap.isOpened():
                print(f"ERROR-CAMERA: No se pudo abrir la cámara {CAMERA_INDEX}")
                # Intentar con otros índices de cámara
                for test_index in range(3):  # Probar con índices 0, 1, 2
                    if test_index == CAMERA_INDEX:
                        continue
                    print(f"DEBUG-CAMERA: Intentando con cámara alternativa {test_index}")
                    cap = cv2.VideoCapture(test_index)
                    if cap.isOpened():
                        print(f"DEBUG-CAMERA: Cámara {test_index} abierta con éxito")
                        break
                
                if not cap.isOpened():
                    print("ERROR-CAMERA: No se pudo abrir ninguna cámara")
                    return
                
            # Configurar la cámara
            cap.set(cv2.CAP_PROP_FRAME_WIDTH, CAMERA_WIDTH)
            cap.set(cv2.CAP_PROP_FRAME_HEIGHT, CAMERA_HEIGHT)
            cap.set(cv2.CAP_PROP_FPS, CAMERA_FPS)
            
            # Verificar configuración real de la cámara
            actual_width = cap.get(cv2.CAP_PROP_FRAME_WIDTH)
            actual_height = cap.get(cv2.CAP_PROP_FRAME_HEIGHT)
            actual_fps = cap.get(cv2.CAP_PROP_FPS)
            
            print(f"DEBUG-CAMERA: Modo combate iniciado con cámara {CAMERA_INDEX}")
            print(f"DEBUG-CAMERA: Configuración solicitada: {CAMERA_WIDTH}x{CAMERA_HEIGHT} @ {CAMERA_FPS}fps")
            print(f"DEBUG-CAMERA: Configuración real: {actual_width}x{actual_height} @ {actual_fps}fps")
            
            # Variables para control de frames
            retry_count = 0
            frame_count = 0
            
            # Bucle principal de procesamiento
            print("DEBUG-CAMERA: Iniciando bucle de procesamiento de frames")
            last_debug_time = time.time()
            
            while True:
                current_time = time.time()
                
                # Leer frame de la cámara
                ret, frame = cap.read()
                if not ret:
                    retry_count += 1
                    print(f"ERROR-CAMERA: No se pudo leer frame, intento {retry_count}/5")
                    if retry_count >= 5:
                        # Reintentar con la cámara
                        print("DEBUG-CAMERA: Intentando reiniciar la cámara")
                        cap.release()
                        cap = cv2.VideoCapture(CAMERA_INDEX)
                        if not cap.isOpened():
                            print("ERROR-CAMERA: No se pudo reiniciar la cámara.")
                            return
                        cap.set(cv2.CAP_PROP_FRAME_WIDTH, CAMERA_WIDTH)
                        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, CAMERA_HEIGHT)
                        retry_count = 0
                    await asyncio.sleep(0.1)
                    continue
                
                # Frame leído correctamente
                retry_count = 0
                frame_count += 1
                
                # Mostrar información cada segundo
                if current_time - last_debug_time > 1.0:
                    print(f"DEBUG-CAMERA: Procesados {frame_count} frames. Tamaño del frame: {frame.shape}")
                    last_debug_time = current_time
                
                try:
                    # Convertir a RGB para procesamiento
                    frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                    
                    # Procesar frame para detección de dedos
                    output_image, current_position, is_confirmed, selected_cell = finger_detector.process_frame(frame_rgb)
                    
                    # Enviar posición si se está apuntando
                    if finger_detector.is_pointing and finger_detector.current_cell:
                        row, col = finger_detector.current_cell
                        center = finger_detector.grid_system.get_cell_center(row, col)
                        if center:
                            # Una celda es válida si NO está ocupada
                            is_valid = not finger_detector.grid_system.is_cell_occupied(row, col)
                            grid_data = {
                                "x": center[0],
                                "y": center[1],
                                "valid": is_valid
                            }
                            print(f"DEBUG-WEBSOCKET: Enviando posición de cuadrícula: ({center[0]}, {center[1]}), válida: {is_valid}, tipo: {MESSAGE_TYPE_GRID_POSITION}")
                            # Convertir a JSON y mostrar para depuración
                            json_data = json.dumps(grid_data)
                            print(f"DEBUG-WEBSOCKET: JSON enviado: {json_data}")
                            try:
                                await websocket.send(bytes([MESSAGE_TYPE_GRID_POSITION]) + 
                                                   json_data.encode('utf-8'))
                                print(f"DEBUG-WEBSOCKET: Mensaje enviado correctamente")
                            except Exception as e:
                                print(f"ERROR-WEBSOCKET: Error al enviar mensaje: {e}")
                    
                    # Debug para posición estable
                    if current_position and finger_detector.is_pointing:
                        is_stable = finger_detector.is_position_stable(current_position)
                        if is_stable:
                            print(f"DEBUG: Posición estable detectada: {current_position}, tiempo: {finger_detector.start_time}")
                        
                    # Debug para confirmación
                    if is_confirmed and selected_cell:
                        print(f"DEBUG: ¡CELDA CONFIRMADA! {selected_cell}")
                    
                    # Enviar frame procesado a Unity
                    success, encoded_frame = encode_frame_to_jpeg(output_image)
                    if success:
                        await websocket.send(bytes([MESSAGE_TYPE_CAMERA_FRAME]) + encoded_frame)
                    
                        
                except Exception as e:
                    print(f"Error procesando frame: {e}")
                
                # Control de velocidad
                await asyncio.sleep(1/TRANSMISSION_FPS)
                
        except (websockets.exceptions.ConnectionClosed, asyncio.CancelledError):
            print("Modo combate detenido")
        except Exception as e:
            print(f"Error en modo combate: {e}")
        finally:
            # Liberar recursos
            if cap is not None and cap.isOpened():
                cap.release()
                
    def cleanup(self):
        """Clean up resources when shutting down."""
        self.finger_counter.stop_camera()