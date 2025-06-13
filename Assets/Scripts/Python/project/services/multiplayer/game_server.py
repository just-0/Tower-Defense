"""
Main Game WebSocket server for the Placer player, with robust camera handling.
"""

import asyncio
import websockets
import json
import cv2
import numpy as np
import threading
import time
import queue

from utils.camera import CameraManager
from utils.image_processings import encode_frame_to_jpeg
from models.sam_model import SAMProcessor 
from utils.pathfinding import handle_astar_from_mask
from models.finger_pointer import GridSystem, FingerPositionDetector
from models.aruco import ArucoDetector

from config.settings import (
    WEBSOCKET_HOST, WEBSOCKET_PORT,
    MESSAGE_TYPE_CAMERA_FRAME, MESSAGE_TYPE_MASK, MESSAGE_TYPE_PATH,
    MESSAGE_TYPE_GRID_POSITION, CAMERA_INDEX, CAMERA_WIDTH, CAMERA_HEIGHT,
    CAMERA_FPS, MESSAGE_TYPE_GRID_CONFIRMATION, TRANSMISSION_FPS
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
        self.planning_camera_manager = CameraManager(camera_index=CAMERA_INDEX, width=CAMERA_WIDTH, height=CAMERA_HEIGHT, fps=CAMERA_FPS)
        self.sam_processor = SAMProcessor()
        self.aruco_detector = ArucoDetector()
        self.grid_system = GridSystem(CAMERA_WIDTH, CAMERA_HEIGHT)
        self.finger_detector = FingerPositionDetector(self.grid_system)
        
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

    async def process_sam(self, websocket):
        """Process the current frame with SAM and send the result."""
        if not self.planning_camera_manager.is_running:
            self.planning_camera_manager.start_camera()
            await asyncio.sleep(1.5)

        frame = self.planning_camera_manager.get_current_frame()
        if frame is None:
            print("Could not get frame for SAM processing.")
            return

        if frame.shape[2] == 3:
            frame_bgr_for_aruco = cv2.cvtColor(frame, cv2.COLOR_RGB2BGR)
        else:
            frame_bgr_for_aruco = frame

        ids, centers, aruco_corners, _ = self.aruco_detector.detect(frame_bgr_for_aruco, draw=False)
        
        goal = None
        if centers and len(centers) > 0:
            cx, cy = centers[0]
            goal = (int(cy), int(cx))
        
        mask_bytes = self.sam_processor.process_image(frame, scene_type="pared", aruco_corners=aruco_corners)
        
        if mask_bytes:
            await websocket.send(bytes([MESSAGE_TYPE_MASK]) + mask_bytes)
            path = handle_astar_from_mask(mask_bytes, False, goal=goal)
            if path:
                path_data = [{"x": x, "y": y} for x, y in path]
                path_json = json.dumps(path_data)
                await websocket.send(bytes([MESSAGE_TYPE_PATH]) + path_json.encode('utf-8'))
                print("Sent SAM Mask and Path.")

    async def handle_combat_mode(self, websocket):
        """Handle the combat mode with dedicated, threaded finger detection."""
        cap = None
        capture_thread = None
        stop_event = threading.Event()
        frame_queue = queue.Queue(maxsize=2)

        def capture_frames_thread():
            nonlocal cap
            try:
                cap = cv2.VideoCapture(CAMERA_INDEX)
                if not cap.isOpened():
                    print(f"ERROR: Could not open camera {CAMERA_INDEX} for combat mode.")
                    return

                cap.set(cv2.CAP_PROP_FRAME_WIDTH, CAMERA_WIDTH)
                cap.set(cv2.CAP_PROP_FRAME_HEIGHT, CAMERA_HEIGHT)
                cap.set(cv2.CAP_PROP_FPS, CAMERA_FPS)
                print(f"Combat camera {CAMERA_INDEX} opened.")

                while not stop_event.is_set():
                    ret, frame = cap.read()
                    if not ret:
                        time.sleep(0.01)
                        continue
                    if frame_queue.full():
                        try:
                            frame_queue.get_nowait()
                        except queue.Empty:
                            pass
                    frame_queue.put(frame)
            finally:
                if cap and cap.isOpened():
                    cap.release()
                print("Combat camera thread finished.")

        try:
            capture_thread = threading.Thread(target=capture_frames_thread, daemon=True)
            capture_thread.start()
            
            is_active = True
            while is_active:
                try:
                    frame_bgr = frame_queue.get(timeout=1.0)
                except queue.Empty:
                    continue

                frame_rgb = cv2.cvtColor(frame_bgr, cv2.COLOR_BGR2RGB)
                
                success, encoded_frame = encode_frame_to_jpeg(frame_bgr, quality=85)
                if success:
                    await websocket.send(bytes([MESSAGE_TYPE_CAMERA_FRAME]) + encoded_frame)
                    
                result = self.finger_detector.process_frame(frame_rgb)
                if result:
                    grid_x, grid_y, is_valid, is_confirmed, _, _ = result
                    position_data = {"x": grid_x, "y": grid_y, "valid": is_valid}
                    await websocket.send(bytes([MESSAGE_TYPE_GRID_POSITION]) + json.dumps(position_data).encode('utf-8'))
                    if is_confirmed:
                        confirmed_data = {"x": grid_x, "y": grid_y, "valid": is_valid}
                        await websocket.send(bytes([MESSAGE_TYPE_GRID_CONFIRMATION]) + json.dumps(confirmed_data).encode('utf-8'))

                await asyncio.sleep(1 / (CAMERA_FPS * 1.5))

        except asyncio.CancelledError:
            is_active = False
            print("Combat mode task cancelled.")
        except websockets.exceptions.ConnectionClosed:
            is_active = False
            print("Client disconnected during combat mode.")
        finally:
            stop_event.set()
            if capture_thread:
                capture_thread.join(timeout=1.0)
            print("Exiting combat mode and cleaning up resources.")
    
    def cleanup(self):
        """Cleanup server resources."""
        print("Game server cleaned up.")

async def main():
    """Main function to run the server."""
    server = GameServer()
    try:
        await server.start()
    except KeyboardInterrupt:
        print("Server is shutting down.")
    finally:
        server.cleanup()

if __name__ == "__main__":
    asyncio.run(main()) 