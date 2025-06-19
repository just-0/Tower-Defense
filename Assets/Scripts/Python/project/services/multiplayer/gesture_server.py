"""
Gesture-only WebSocket server for the Selector player.
"""

import asyncio
import websockets
import json
import cv2

from utils.finger_tracking import FingerCounter
from utils.image_processings import encode_frame_to_jpeg

from config.settings import (
    WEBSOCKET_HOST, FINGER_TRACKING_PORT, TRANSMISSION_FPS,
    MESSAGE_TYPE_CAMERA_FRAME, MESSAGE_TYPE_FINGER_COUNT, FINGER_CAMERA_INDEX,
    FINGER_CAMERA_WIDTH, FINGER_CAMERA_HEIGHT, FINGER_CAMERA_FPS,
    FINGER_TRANSMISSION_FPS
)

class GestureServer:
    """
    WebSocket server for handling finger tracking for one client.
    """
    
    def __init__(self):
        """Initialize the Gesture server."""
        self.server = None
        self.finger_counter = FingerCounter(
            camera_index=FINGER_CAMERA_INDEX,
            width=FINGER_CAMERA_WIDTH,
            height=FINGER_CAMERA_HEIGHT,
            fps=FINGER_CAMERA_FPS
        )
        
    async def start(self):
        """Start the gesture WebSocket server."""
        self.server = await websockets.serve(
            self.handle_finger_client,
            WEBSOCKET_HOST,
            FINGER_TRACKING_PORT
        )
        print(f"Gesture WebSocket server started at ws://{WEBSOCKET_HOST}:{FINGER_TRACKING_PORT}")
        
        self.finger_counter.start_camera()
        
        await self.server.wait_closed()
        
    async def handle_finger_client(self, websocket):
        """
        Handle a client connection for finger tracking.
        """
        print("New finger tracking client connected")
        finger_frame_task = None
        finger_count_task = None
        
        try:
            finger_frame_task = asyncio.create_task(self.send_finger_frames(websocket))
            finger_count_task = asyncio.create_task(self.send_finger_counts(websocket))
            
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

def create_server():
    """Create and return the GestureServer instance."""
    return GestureServer()

async def start_server(server):
    """Start the server's async tasks."""
    try:
        await server.start()
    except asyncio.CancelledError:
        print("Gesture server start cancelled.")
    finally:
        server.cleanup() 