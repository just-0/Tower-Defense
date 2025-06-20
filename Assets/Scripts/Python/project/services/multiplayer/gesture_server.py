"""
Gesture-only WebSocket server for the Selector player.
"""

import asyncio
import websockets
import json
import cv2

from utils.finger_tracking import FingerCounter, scan_for_available_cameras
from utils.image_processings import encode_frame_to_jpeg

from config.settings import (
    WEBSOCKET_HOST, FINGER_TRACKING_PORT, TRANSMISSION_FPS,
    MESSAGE_TYPE_CAMERA_FRAME, MESSAGE_TYPE_FINGER_COUNT, FINGER_CAMERA_INDEX,
    FINGER_CAMERA_WIDTH, FINGER_CAMERA_HEIGHT, FINGER_CAMERA_FPS,
    FINGER_TRANSMISSION_FPS, MENU_GESTURE_PORT, MESSAGE_TYPE_SERVER_STATUS,
    MESSAGE_TYPE_SWITCH_CAMERA, MESSAGE_TYPE_CAMERA_LIST
)

# Define a new message type for sending camera resolution info
MESSAGE_TYPE_CAMERA_INFO = 12

class GestureServer:
    """
    WebSocket server for handling finger tracking for one client.
    """
    
    def __init__(self, port=FINGER_TRACKING_PORT):
        """Initialize the Gesture server."""
        self.port = port
        self.server = None
        self.camera_ready = False
        self.finger_counter = FingerCounter(
            camera_index=None,
            width=FINGER_CAMERA_WIDTH,
            height=FINGER_CAMERA_HEIGHT,
            fps=FINGER_CAMERA_FPS
        )
        
    async def start(self):
        """Start the gesture WebSocket server."""
        self.server = await websockets.serve(
            self.handle_finger_client,
            WEBSOCKET_HOST,
            self.port
        )
        print(f"Gesture WebSocket server started at ws://{WEBSOCKET_HOST}:{self.port}")
        
        self.camera_ready = self.finger_counter.start_camera()
        
        if not self.camera_ready:
            print(f"ADVERTENCIA: El servidor en el puerto {self.port} se inició, pero la cámara no está disponible.")
        
        await self.server.wait_closed()
        
    async def handle_finger_client(self, websocket):
        """
        Handle a client connection for finger tracking.
        """
        print("New finger tracking client connected")

        status_payload = {"status": "camera_ok" if self.camera_ready else "no_camera"}
        status_message = bytes([MESSAGE_TYPE_SERVER_STATUS]) + json.dumps(status_payload).encode('utf-8')
        await websocket.send(status_message)

        if self.camera_ready:
            # Send camera info to the client
            try:
                # Assuming FingerCounter exposes the actual width and height
                width, height = self.finger_counter.width, self.finger_counter.height
                info_payload = {"width": width, "height": height}
                await websocket.send(bytes([MESSAGE_TYPE_CAMERA_INFO]) + json.dumps(info_payload).encode('utf-8'))
                print(f"Sent gesture camera info: {width}x{height}")
            except Exception as e:
                print(f"Could not get/send gesture camera resolution: {e}")

            available_cams = scan_for_available_cameras()
            cam_list_payload = {"available_cameras": available_cams}
            cam_list_message = bytes([MESSAGE_TYPE_CAMERA_LIST]) + json.dumps(cam_list_payload).encode('utf-8')
            await websocket.send(cam_list_message)

        if not self.camera_ready:
            try:
                await websocket.wait_closed()
            finally:
                print("Client disconnected from non-ready server.")
            return

        finger_frame_task = None
        finger_count_task = None
        
        try:
            finger_frame_task = asyncio.create_task(self.send_finger_frames(websocket))
            finger_count_task = asyncio.create_task(self.send_finger_counts(websocket))
            
            # Bucle para recibir mensajes del cliente
            async for message in websocket:
                if len(message) > 0:
                    message_type = message[0]
                    
                    if message_type == MESSAGE_TYPE_SWITCH_CAMERA:
                        try:
                            json_str = message[1:].decode('utf-8')
                            data = json.loads(json_str)
                            new_index = data.get('index')
                            if new_index is not None:
                                print(f"Servidor recibió petición para cambiar a cámara {new_index}")
                                self.finger_counter.switch_camera(new_index)
                        except Exception as e:
                            print(f"Error procesando mensaje de cambio de cámara: {e}")

            await asyncio.gather(finger_frame_task, finger_count_task)
            
        except websockets.exceptions.ConnectionClosed:
            print("Finger tracking client disconnected")
        finally:
            if finger_frame_task and not finger_frame_task.done():
                finger_frame_task.cancel()
            if finger_count_task and not finger_count_task.done():
                finger_count_task.cancel()
                
    async def send_finger_frames(self, websocket):
        """Send finger tracking camera frames to the client."""
        try:
            while self.finger_counter.is_running:
                frame = self.finger_counter.get_current_frame()
                if frame is not None:
                    success, encoded_frame = encode_frame_to_jpeg(frame)
                    if success:
                        await websocket.send(bytes([MESSAGE_TYPE_CAMERA_FRAME]) + encoded_frame)
                await asyncio.sleep(1/TRANSMISSION_FPS)
        except (websockets.exceptions.ConnectionClosed, asyncio.CancelledError):
            print("Finger camera frame sending stopped")
    
    async def send_finger_counts(self, websocket):
        """Send finger count updates to the client."""
        try:
            while self.finger_counter.is_running:
                finger_count = self.finger_counter.get_finger_count()
                finger_data = {"count": finger_count}
                finger_json = json.dumps(finger_data)
                await websocket.send(bytes([MESSAGE_TYPE_FINGER_COUNT]) + finger_json.encode('utf-8'))
                await asyncio.sleep(1/FINGER_TRANSMISSION_FPS)
        except (websockets.exceptions.ConnectionClosed, asyncio.CancelledError):
            print("Finger count sending stopped")
                
    def cleanup(self):
        """Cleanup resources."""
        if self.finger_counter.is_running:
            self.finger_counter.stop_camera()
        print("Gesture server cleaned up.")

def create_server(port=FINGER_TRACKING_PORT):
    """Create and return the GestureServer instance."""
    return GestureServer(port)

async def start_server(server):
    """Start the server's async tasks."""
    try:
        await server.start()
    except asyncio.CancelledError:
        print("Gesture server start cancelled.")
    finally:
        server.cleanup() 