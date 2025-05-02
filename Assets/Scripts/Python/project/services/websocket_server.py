"""
WebSocket server for real-time communication.
"""

import asyncio
import websockets
import json
import threading

from utils.camera import CameraManager
from utils.image_processings import encode_frame_to_jpeg
from utils.finger_tracking import FingerCounter
from models.sam_model import SAMProcessor
from utils.pathfinding import *

from config.settings import (
    WEBSOCKET_HOST, WEBSOCKET_PORT, FINGER_TRACKING_PORT, TRANSMISSION_FPS,
    MESSAGE_TYPE_CAMERA_FRAME, MESSAGE_TYPE_MASK, MESSAGE_TYPE_PATH, 
    MESSAGE_TYPE_FINGER_COUNT, FINGER_CAMERA_INDEX, FINGER_CAMERA_WIDTH,
    FINGER_CAMERA_HEIGHT, FINGER_CAMERA_FPS, FINGER_TRANSMISSION_FPS
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
        # Start the main SAM WebSocket server
        self.server = await websockets.serve(
            self.handle_client, 
            WEBSOCKET_HOST, 
            WEBSOCKET_PORT
        )
        print(f"SAM WebSocket server started at ws://{WEBSOCKET_HOST}:{WEBSOCKET_PORT}")
        
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
        
        print("New SAM client connected")
        
        try:
            async for message in websocket:
                if isinstance(message, str):
                    print(f"Received SAM command: {message}")
                    
                    if message == "START_CAMERA":
                        camera_manager.start_camera()
                        send_frames = True
                        
                        # Start sending frames in a separate task
                        if frame_task is None or frame_task.done():
                            frame_task = asyncio.create_task(
                                self.send_camera_frames(websocket, camera_manager)
                            )
                        
                    elif message == "STOP_CAMERA":
                        send_frames = False
                        camera_manager.stop_camera()
                        # Cancel the frame sending task if it exists
                        if frame_task and not frame_task.done():
                            frame_task.cancel()
                        
                    elif message == "PROCESS_SAM":
                        await self.process_sam(websocket, camera_manager, sam_processor)
                    
        except websockets.exceptions.ConnectionClosed:
            print("SAM client disconnected")
        finally:
            camera_manager.stop_camera()
            # Ensure frame task is canceled if it exists
            if frame_task and not frame_task.done():
                frame_task.cancel()
                
    async def process_sam(self, websocket, camera_manager, sam_processor):
        """
        Process the current frame with SAM and send the result.
        
        Args:
            websocket: WebSocket connection object
            camera_manager (CameraManager): Camera manager instance
            sam_processor (SAMProcessor): SAM processor instance
        """
        frame = camera_manager.get_current_frame()
        if frame is not None:
            # Process the frame with SAM
            mask_bytes = sam_processor.process_image(frame)
            if mask_bytes:
                # Send the mask (type 3)
                await websocket.send(bytes([MESSAGE_TYPE_MASK]) + mask_bytes)
                print("Sent mask data")
                
                # Process and send A* path
                path = handle_astar_from_mask(mask_bytes, True)
                if path:
                    # Convert to list of dictionaries with keys "x" and "y"
                    path_data = [{"x": x, "y": y} for x, y in path]
                    path_json = json.dumps(path_data)  # Serialize to JSON
                    try:
                        await websocket.send(bytes([MESSAGE_TYPE_PATH]) + path_json.encode('utf-8'))
                        print("Sent A* path as message type 4")
                    except Exception as e:
                        print("ERROR sending A* path:", str(e))
            
    async def send_camera_frames(self, websocket, camera_manager):
        """
        Send camera frames to the client.
        
        Args:
            websocket: WebSocket connection object
            camera_manager (CameraManager): Camera manager instance
        """
        try:
            while camera_manager.is_running:
                frame = camera_manager.get_current_frame()
                if frame is not None:
                    # Encode the frame as JPEG
                    success, encoded_frame = encode_frame_to_jpeg(frame)
                    if success:
                        # Send camera frame (type 1)
                        await websocket.send(bytes([MESSAGE_TYPE_CAMERA_FRAME]) + encoded_frame)
                
                # Control frame rate
                await asyncio.sleep(1/TRANSMISSION_FPS)
                
        except (websockets.exceptions.ConnectionClosed, asyncio.CancelledError):
            print("Camera frame sending stopped")
            
    def cleanup(self):
        """Clean up resources when shutting down."""
        self.finger_counter.stop_camera()