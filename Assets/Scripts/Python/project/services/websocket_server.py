"""
WebSocket server for real-time communication.
"""

import asyncio
import websockets
import json
import time
import cv2

from utils.camera import CameraManager
from utils.image_processings import encode_frame_to_jpeg
from utils.finger_tracking import FingerCounter
from models.sam_model import FastObjectDetector as SAMProcessor 
from utils.pathfinding import handle_astar_from_mask
from models.finger_pointer import GridSystem, FingerPositionDetector
from models.aruco import ArucoDetector

from config.settings import (
    WEBSOCKET_HOST, WEBSOCKET_PORT, FINGER_TRACKING_PORT, TRANSMISSION_FPS,
    MESSAGE_TYPE_CAMERA_FRAME, MESSAGE_TYPE_MASK, MESSAGE_TYPE_PATH, 
    MESSAGE_TYPE_FINGER_COUNT, FINGER_CAMERA_INDEX, FINGER_CAMERA_WIDTH_PREFERRED,
    FINGER_CAMERA_HEIGHT_PREFERRED, FINGER_CAMERA_FPS, FINGER_TRANSMISSION_FPS,
    MESSAGE_TYPE_GRID_POSITION, CAMERA_INDEX, CAMERA_WIDTH_PREFERRED, CAMERA_HEIGHT_PREFERRED,
    CAMERA_FPS, MESSAGE_TYPE_GRID_CONFIRMATION, MESSAGE_TYPE_PROGRESS_UPDATE
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
            width=FINGER_CAMERA_WIDTH_PREFERRED,
            height=FINGER_CAMERA_HEIGHT_PREFERRED,
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
                        
                        # Initialize grid system and finger detector with actual camera resolution
                        if grid_system is None:
                            # Use camera manager to get actual resolution
                            temp_camera = CameraManager(
                                camera_index=CAMERA_INDEX, 
                                width=CAMERA_WIDTH_PREFERRED, 
                                height=CAMERA_HEIGHT_PREFERRED, 
                                fps=CAMERA_FPS
                            )
                            temp_camera.start_camera()
                            actual_width, actual_height = temp_camera.get_resolution()
                            temp_camera.stop_camera()
                            
                            grid_system = GridSystem(actual_width, actual_height)
                            finger_detector = FingerPositionDetector(grid_system)
                            print(f"Grid system initialized with resolution: {actual_width}x{actual_height}")
                        
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
                
    async def send_progress_update(self, websocket, step, progress):
        """Envía una actualización de progreso al cliente."""
        try:
            progress_data = {"step": step, "progress": progress}
            progress_json = json.dumps(progress_data)
            await websocket.send(bytes([MESSAGE_TYPE_PROGRESS_UPDATE]) + progress_json.encode('utf-8'))
            # Cedemos el control para asegurar que el mensaje se envíe antes de operaciones bloqueantes
            await asyncio.sleep(0.01)
        except Exception as e:
            print(f"Error enviando actualización de progreso: {e}")

    async def process_sam(self, websocket, camera_manager, sam_processor):
        """Process the current frame with SAM and send the result."""
        await self.send_progress_update(websocket, "Obteniendo fotograma...", 5)
        frame = camera_manager.get_current_frame()
        if frame is None:
            await self.send_progress_update(websocket, "Error: no se pudo capturar el fotograma.", 0)
            return

        # --- DETECCIÓN ARUCO PRIMERO ---
        await self.send_progress_update(websocket, "Detectando marcador ArUco...", 15)
        if frame.shape[2] == 3:
            frame_bgr_for_aruco = cv2.cvtColor(frame, cv2.COLOR_RGB2BGR)
        else:
            frame_bgr_for_aruco = frame # Asumir que ya está en BGR si no es RGB
        
        aruco_detector = ArucoDetector()
        ids, centers, aruco_corners, _ = aruco_detector.detect(frame_bgr_for_aruco, draw=False)
        await self.send_progress_update(websocket, "Marcador ArUco procesado.", 30)
        
        goal = None
        if centers and len(centers) > 0:
            cx, cy = centers[0]
            goal = (int(cy), int(cx))
            print(f"Destino ARUCO detectado en: {goal}")
        else:
            print("No se detectó ARUCO, usando destino por defecto (extremo izquierdo)")
            
        await self.send_progress_update(websocket, "Iniciando procesamiento SAM...", 40)
        
        # --- CAMBIO CLAVE: Pasar el callback a sam_processor ---
        mask_bytes = await sam_processor.process_image(
            frame, 
            scene_type="pared", 
            aruco_corners=aruco_corners,
            progress_callback=self.send_progress_update,
            websocket=websocket
        )
        
        if not mask_bytes:
            await self.send_progress_update(websocket, "Error durante el procesamiento SAM.", 0)
            return
        
        await self.send_progress_update(websocket, "Máscara de segmentación generada.", 80)
        await websocket.send(bytes([MESSAGE_TYPE_MASK]) + mask_bytes)
        print("Sent mask data")
        
        await self.send_progress_update(websocket, "Calculando ruta A*...", 90)
        path = handle_astar_from_mask(mask_bytes, False, goal=goal)
        if path:
            path_data = {"points": [{"x": x, "y": y} for x, y in path]}
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
        """Handle the combat mode with enhanced camera management."""
        try:
            print(f"INFO: Iniciando modo combate con cámara {CAMERA_INDEX}")
            # Use enhanced camera manager for combat mode
            combat_camera = CameraManager(
                camera_index=CAMERA_INDEX, 
                width=CAMERA_WIDTH_PREFERRED, 
                height=CAMERA_HEIGHT_PREFERRED, 
                fps=CAMERA_FPS
            )
            
            if not combat_camera.start_camera():
                print(f"ERROR: No se pudo iniciar la cámara {CAMERA_INDEX}")
                return
            
            # Get actual resolution
            actual_width, actual_height = combat_camera.get_resolution()
            actual_fps = combat_camera.get_fps()
            print(f"INFO: Modo combate iniciado con cámara configurada a {actual_width}x{actual_height} @ {actual_fps}fps")
            
            # Bucle principal para procesar frames - mucho más simple con CameraManager
            frame_count = 0
            total_frames = 0
            last_fps_time = time.time()
            last_position_send_time = 0
            grid_position_cache = None
            
            while True:
                current_time = time.time()
                
                # Get frame from camera manager (already in RGB format)
                frame = combat_camera.get_current_frame()
                if frame is None:
                    await asyncio.sleep(0.01)
                    continue
                
                # Incrementar contador total de frames
                total_frames += 1
                
                # Solo procesar cada N frames si estamos por encima de cierto FPS 
                # para evitar sobrecarga en sistemas lentos
                if total_frames % 2 != 0 and frame_count / (current_time - last_fps_time + 0.001) > 25:
                    continue  # Saltar este frame
                
                # Procesar el frame
                try:
                    # Procesamiento básico para detección de manos (frame ya está en RGB)
                    frame_rgb = cv2.convertScaleAbs(frame, alpha=1.2, beta=10)
                    
                    # Procesar frame para detección de dedos
                    output_image, current_position, is_confirmed, selected_cell = finger_detector.process_frame(frame_rgb)
                    
                    # Enviar frame procesado lo antes posible para mantener fluidez visual
                    success, encoded_frame = encode_frame_to_jpeg(output_image, quality=85)
                    if success:
                        await websocket.send(bytes([MESSAGE_TYPE_CAMERA_FRAME]) + encoded_frame)
                    
                    # Gestión de alta frecuencia para envío de posiciones
                    position_interval = 1.0 / 30.0  # 30 actualizaciones por segundo máximo
                    should_send_position = (
                        finger_detector.is_pointing and 
                        finger_detector.current_cell and
                        current_time - last_position_send_time > position_interval
                    )
                    
                    if should_send_position:
                        row, col = finger_detector.current_cell
                        center = finger_detector.grid_system.get_cell_center(row, col)
                        if center:
                            is_valid = not finger_detector.grid_system.is_cell_occupied(row, col)
                            
                            # Serialización eficiente
                            x = float(center[0]) if not isinstance(center[0], (int, float)) else center[0]
                            y = float(center[1]) if not isinstance(center[1], (int, float)) else center[1]
                            
                            # Caché de posiciones para evitar duplicados
                            current_data_str = f"{x:.1f}_{y:.1f}_{is_valid}"
                            if grid_position_cache != current_data_str:
                                grid_position_cache = current_data_str
                                
                                # Preparar JSON compacto
                                grid_data = {"x": x, "y": y, "valid": bool(is_valid)}
                                json_data = json.dumps(grid_data)
                                
                                # Enviar posición a Unity
                                await websocket.send(bytes([MESSAGE_TYPE_GRID_POSITION]) + 
                                                   json_data.encode('utf-8'))
                                last_position_send_time = current_time
                    
                    # Notificar confirmaciones
                    if is_confirmed and selected_cell:
                        print(f"INFO: Celda confirmada: {selected_cell}")
                        row, col = selected_cell
                        center = finger_detector.grid_system.get_cell_center(row, col)
                        if center:
                            is_valid = not finger_detector.grid_system.is_cell_occupied(row, col)
                            if is_valid:
                                x = float(center[0])
                                y = float(center[1])
                                confirmed_data = {"x": x, "y": y, "valid": True}
                                json_data = json.dumps(confirmed_data)
                                await websocket.send(bytes([MESSAGE_TYPE_GRID_CONFIRMATION]) + json_data.encode('utf-8'))
                                print(f"Sent grid confirmation for cell {selected_cell}")
                    
                    # Métricas de rendimiento
                    frame_count += 1
                    if current_time - last_fps_time > 5.0:
                        fps = frame_count / (current_time - last_fps_time)
                        print(f"INFO: Procesamiento FPS: {fps:.2f}, Frames totales: {total_frames}")
                        frame_count = 0
                        total_frames = 0
                        last_fps_time = current_time
                    
                except Exception as e:
                    print(f"ERROR: Procesamiento: {str(e)}")
                    import traceback
                    traceback.print_exc()
                
                # Control de velocidad adaptativo - solo esperar si vamos muy rápido
                process_time = time.time() - current_time
                target_frame_time = 1.0 / 60.0  # Apuntar a 60 FPS máximo
                if process_time < target_frame_time:
                    sleep_time = target_frame_time - process_time
                    await asyncio.sleep(max(0.001, sleep_time))
            
        except asyncio.CancelledError:
            print("INFO: Modo combate detenido")
        except Exception as e:
            print(f"ERROR: Error general en modo combate: {str(e)}")
            import traceback
            traceback.print_exc()
        finally:
            # Limpiar recursos
            try:
                if 'combat_camera' in locals():
                    combat_camera.stop_camera()
                
                print("INFO: Recursos de modo combate liberados")
            except Exception as e:
                print(f"ERROR: Error al liberar recursos: {str(e)}")
                import traceback
                traceback.print_exc()
                
    def cleanup(self):
        """Clean up resources when shutting down."""
        self.finger_counter.stop_camera()