"""
Main Game WebSocket server for the Placer player, with robust camera handling.
"""

import asyncio
import websockets
import json
import cv2

from utils.camera import CameraManager
from utils.image_processings import encode_frame_to_jpeg
from models.sam_model import FastObjectDetector as SAMProcessor 
from utils.pathfinding import handle_astar_from_mask
from models.finger_pointer import GridSystem, FingerPositionDetector
from models.aruco import ArucoDetector

from config.settings import (
    WEBSOCKET_HOST, WEBSOCKET_PORT,
    MESSAGE_TYPE_CAMERA_FRAME, MESSAGE_TYPE_MASK, MESSAGE_TYPE_PATH,
    MESSAGE_TYPE_GRID_POSITION, CAMERA_INDEX, CAMERA_WIDTH_PREFERRED, CAMERA_HEIGHT_PREFERRED,
    CAMERA_FPS, MESSAGE_TYPE_GRID_CONFIRMATION, TRANSMISSION_FPS, MESSAGE_TYPE_PROGRESS_UPDATE,
    MESSAGE_TYPE_CAMERA_INFO
)

class GameServer:
    """
    WebSocket server for handling main game logic (SAM, Pathfinding, Combat).
    Replicates original threaded combat mode camera handling and adds planning phase streaming.
    """
    
    def __init__(self):
        """Initialize the Game server."""
        self.server = None
        # This manager is for the planning phase (SAM and preview)
        self.planning_camera_manager = CameraManager(
            camera_index=CAMERA_INDEX, 
            width=CAMERA_WIDTH_PREFERRED, 
            height=CAMERA_HEIGHT_PREFERRED, 
            fps=CAMERA_FPS
        )
        self.sam_processor = SAMProcessor()
        self.aruco_detector = ArucoDetector()
        # GridSystem and FingerDetector will be initialized on-demand when combat starts,
        # using the actual camera resolution.
        self.grid_system = None
        self.finger_detector = None
        
        self.active_connections = set()

    async def start(self):
        """Start the game WebSocket server."""
        self.server = await websockets.serve(self.handle_client, WEBSOCKET_HOST, WEBSOCKET_PORT)
        print(f"Main Game WebSocket server started at ws://{WEBSOCKET_HOST}:{WEBSOCKET_PORT}")
        await self.server.wait_closed()

    async def handle_client(self, websocket):
        """Handle a client connection for game processing."""
        print("New game client connected")
        self.active_connections.add(websocket)
        
        # State variables per client connection
        send_frames_task = None
        combat_task = None
        combat_mode_active = False

        try:
            async for message in websocket:
                if isinstance(message, str):
                    print(f"Received game command: {message}")
                    
                    if message == "START_CAMERA" and not combat_mode_active:
                        if not self.planning_camera_manager.is_running:
                            self.planning_camera_manager.start_camera()

                        # Send camera dimensions to client using the new get_resolution method
                        if self.planning_camera_manager.is_running:
                            try:
                                width, height = self.planning_camera_manager.get_resolution()
                                info_payload = {"width": width, "height": height}
                                await websocket.send(bytes([MESSAGE_TYPE_CAMERA_INFO]) + json.dumps(info_payload).encode('utf-8'))
                                print(f"Sent planning camera info: {width}x{height}")
                            except Exception as e:
                                print(f"Could not get/send planning camera resolution: {e}")

                        if send_frames_task is None or send_frames_task.done():
                            send_frames_task = asyncio.create_task(
                                self.send_planning_frames(websocket)
                            )

                    elif message == "STOP_CAMERA" and not combat_mode_active:
                        if send_frames_task and not send_frames_task.done():
                            send_frames_task.cancel()
                        if self.planning_camera_manager.is_running:
                            self.planning_camera_manager.stop_camera()

                    elif message == "PROCESS_SAM":
                        # Stop streaming during processing to avoid conflicts
                        if send_frames_task and not send_frames_task.done():
                            send_frames_task.cancel()
                        await self.process_sam(websocket)

                    elif message == "START_COMBAT":
                        combat_mode_active = True
                        # Ensure planning camera and stream are stopped
                        if send_frames_task and not send_frames_task.done():
                            send_frames_task.cancel()
                        if self.planning_camera_manager.is_running:
                            self.planning_camera_manager.stop_camera()
                        
                        if combat_task is None or combat_task.done():
                            combat_task = asyncio.create_task(self.handle_combat_mode(websocket))
                            
                    elif message == "STOP_COMBAT":
                        combat_mode_active = False
                        if combat_task and not combat_task.done():
                            combat_task.cancel()
                            print("Combat mode stopped by command.")
                    
        except websockets.exceptions.ConnectionClosed:
            print("Game client disconnected")
        finally:
            self.active_connections.remove(websocket)
            if send_frames_task and not send_frames_task.done():
                send_frames_task.cancel()
            if combat_task and not combat_task.done():
                combat_task.cancel()
            if self.planning_camera_manager.is_running:
                self.planning_camera_manager.stop_camera()

    async def send_planning_frames(self, websocket):
        """Continuously send frames from the planning camera."""
        try:
            while self.planning_camera_manager.is_running:
                frame = self.planning_camera_manager.get_current_frame()
                if frame is not None:
                    success, encoded_frame = encode_frame_to_jpeg(frame)
                    if success:
                        await websocket.send(bytes([MESSAGE_TYPE_CAMERA_FRAME]) + encoded_frame)
                await asyncio.sleep(1 / TRANSMISSION_FPS)
        except (websockets.exceptions.ConnectionClosed, asyncio.CancelledError):
            print("Planning camera frame sending stopped.")
        except Exception as e:
            print(f"Error in send_planning_frames: {e}")

    async def send_progress_update(self, websocket, step, progress):
        """Envía una actualización de progreso al cliente."""
        try:
            progress_data = {"step": step, "progress": progress}
            progress_json = json.dumps(progress_data)
            await websocket.send(bytes([MESSAGE_TYPE_PROGRESS_UPDATE]) + progress_json.encode('utf-8'))
            await asyncio.sleep(0.01) # Ceder control para que el mensaje se envíe
        except Exception as e:
            print(f"Error enviando actualización de progreso: {e}")

    async def process_sam(self, websocket):
        """Process the current frame with SAM and send the result, with error handling."""
        print("Starting SAM process...")
        try:
            # Informar al cliente que el proceso ha comenzado
            await self.send_progress_update(websocket, "Iniciando proceso...", 5)
            
            if not self.planning_camera_manager.is_running:
                self.planning_camera_manager.start_camera()
                await asyncio.sleep(1.5)

            frame = self.planning_camera_manager.get_current_frame()
            if frame is None:
                await self.send_progress_update(websocket, "Error: no se pudo capturar fotograma.", 0)
                print("Error: Could not get frame for SAM processing.")
                return

            await self.send_progress_update(websocket, "Detectando marcador ArUco...", 15)
            if frame.shape[2] == 3:
                frame_bgr_for_aruco = cv2.cvtColor(frame, cv2.COLOR_RGB2BGR)
            else:
                frame_bgr_for_aruco = frame

            ids, centers, aruco_corners, _ = self.aruco_detector.detect(frame_bgr_for_aruco, draw=False)
            await self.send_progress_update(websocket, "Detección de ArUco finalizada.", 30)

            goal = None
            if centers and len(centers) > 0:
                cx, cy = centers[0]
                goal = (int(cy), int(cx))
                print(f"ArUco marker found at {goal}.")
            else:
                print("Warning: ArUco marker not found.")
            
            print("Processing frame with SAM...")
            await self.send_progress_update(websocket, "Iniciando SAM...", 40)
            
            mask_bytes = await self.sam_processor.process_image(
                frame, 
                scene_type="pared", 
                aruco_corners=aruco_corners,
                progress_callback=self.send_progress_update,
                websocket=websocket
            )
            
            if not mask_bytes:
                await self.send_progress_update(websocket, "Error en procesamiento SAM", 0)
                print("Error: SAM processing did not return a mask.")
                return
            
            await self.send_progress_update(websocket, "Máscara recibida. Calculando ruta...", 80)
            await websocket.send(bytes([MESSAGE_TYPE_MASK]) + mask_bytes)
            print("Mask sent. Calculating A* path...")

            path = handle_astar_from_mask(mask_bytes, False, goal=goal)
            if path:
                path_data = [{"x": x, "y": y} for x, y in path]
                path_json = json.dumps(path_data)
                await self.send_progress_update(websocket, "Ruta calculada. Enviando...", 95)
                await websocket.send(bytes([MESSAGE_TYPE_PATH]) + path_json.encode('utf-8'))
                print("Path sent successfully.")
            else:
                await self.send_progress_update(websocket, "Error: no se pudo calcular la ruta A*.", 0)
                print("Warning: A* path could not be calculated.")

        except Exception as e:
            import traceback
            print("--- FATAL ERROR DURING SAM PROCESSING ---")
            print(f"Error Type: {type(e).__name__}")
            print(f"Error Message: {e}")
            traceback.print_exc()
            print("-----------------------------------------")
        finally:
            # Always ensure the planning camera is stopped after the process to free the resource
            if self.planning_camera_manager.is_running:
                self.planning_camera_manager.stop_camera()
                print("Planning camera stopped after SAM process.")

    async def handle_combat_mode(self, websocket):
        """Handle the combat mode with enhanced camera manager."""



        try:
            # --- Use enhanced camera manager for combat mode ---
            combat_camera = CameraManager(
                camera_index=CAMERA_INDEX, 
                width=CAMERA_WIDTH_PREFERRED, 
                height=CAMERA_HEIGHT_PREFERRED, 
                fps=CAMERA_FPS
            )
            
            if not combat_camera.start_camera():
                print(f"ERROR: Could not start camera {CAMERA_INDEX} for combat mode.")
                return
            
            # Get the ACTUAL resolution from the camera manager
            actual_width, actual_height = combat_camera.get_resolution()
            actual_fps = combat_camera.get_fps()
            print(f"Combat camera {CAMERA_INDEX} opened with actual resolution: {actual_width}x{actual_height} @ {actual_fps}fps")
            
            # Send this information to the Unity client
            info_payload = {"width": actual_width, "height": actual_height}
            await websocket.send(bytes([MESSAGE_TYPE_CAMERA_INFO]) + json.dumps(info_payload).encode('utf-8'))

            # Initialize or update GridSystem and FingerDetector with the correct, real resolution
            if self.grid_system is None or self.grid_system.width != actual_width or self.grid_system.height != actual_height:
                print(f"Initializing GridSystem with new resolution: {actual_width}x{actual_height}")
                self.grid_system = GridSystem(actual_width, actual_height)
                self.finger_detector = FingerPositionDetector(self.grid_system)

            is_active = True
            while is_active:
                # Get frame from camera manager (already in RGB format)
                frame_rgb = combat_camera.get_current_frame()
                if frame_rgb is None:
                    await asyncio.sleep(0.01)
                    continue
                
                # Ensure finger detector is ready
                if self.finger_detector is None:
                    await asyncio.sleep(0.01)
                    continue

                output_image, _, is_confirmed, selected_cell = self.finger_detector.process_frame(frame_rgb)
                
                success, encoded_frame = encode_frame_to_jpeg(output_image, quality=85)
                if success:
                    await websocket.send(bytes([MESSAGE_TYPE_CAMERA_FRAME]) + encoded_frame)

                if self.finger_detector.is_pointing and self.finger_detector.current_cell is not None:
                    row, col = self.finger_detector.current_cell
                    center = self.finger_detector.grid_system.get_cell_center(row, col)
                    if center:
                        is_valid = not self.finger_detector.grid_system.is_cell_occupied(row, col)
                        position_data = {"x": float(center[0]), "y": float(center[1]), "valid": is_valid}
                        await websocket.send(bytes([MESSAGE_TYPE_GRID_POSITION]) + json.dumps(position_data).encode('utf-8'))

                if is_confirmed and selected_cell is not None:
                    row, col = selected_cell
                    center = self.finger_detector.grid_system.get_cell_center(row, col)
                    if center:
                        confirmed_data = {"x": float(center[0]), "y": float(center[1]), "valid": True}
                        await websocket.send(bytes([MESSAGE_TYPE_GRID_CONFIRMATION]) + json.dumps(confirmed_data).encode('utf-8'))
                        print(f"Sent confirmation for cell {selected_cell}")

                await asyncio.sleep(1 / (actual_fps * 1.5)) # Adjusted sleep based on actual FPS

        except asyncio.CancelledError:
            is_active = False
            print("Combat mode task cancelled.")
        except websockets.exceptions.ConnectionClosed:
            is_active = False
            print("Client disconnected during combat mode.")
        finally:
            # Clean up camera manager
            if 'combat_camera' in locals():
                combat_camera.stop_camera()
            print("Exiting combat mode and cleaning up resources.")
    
    def cleanup(self):
        """Cleanup server resources."""
        if self.planning_camera_manager.is_running:
            self.planning_camera_manager.stop_camera()
        print("Game server cleaned up.")

def create_server():
    """Create and return the GameServer instance."""
    return GameServer()

async def start_server(server):
    """Start the server's async tasks."""
    try:
        await server.start()
    except asyncio.CancelledError:
        print("Game server start cancelled.")
    finally:
        server.cleanup() 