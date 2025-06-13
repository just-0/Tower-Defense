"""
Camera management module for capturing and processing video frames.
"""

import cv2
import threading
import time
from config.settings import CAMERA_WIDTH as DEFAULT_WIDTH, CAMERA_HEIGHT as DEFAULT_HEIGHT, CAMERA_FPS as DEFAULT_FPS, CAMERA_INDEX as DEFAULT_INDEX

class CameraManager:
    """
    Manages camera operations including initialization, frame capture, and cleanup.
    """
    
    def __init__(self, camera_index=DEFAULT_INDEX, width=DEFAULT_WIDTH, height=DEFAULT_HEIGHT, fps=DEFAULT_FPS):
        """Initialize the camera manager."""
        self.camera_index = camera_index
        self.width = width
        self.height = height
        self.fps = fps
        
        self.camera = None
        self.is_running = False
        self.current_frame = None
        self.lock = threading.Lock()
        
    def start_camera(self):
        """
        Start the camera and begin capturing frames.
        
        Returns:
            bool: True if camera started successfully, False otherwise.
        """
        if self.camera is None:
            self.camera = cv2.VideoCapture(self.camera_index)
            
            if not self.camera.isOpened():
                print(f"Error: Could not open camera with index {self.camera_index}")
                return False

            # Set camera properties
            self.camera.set(cv2.CAP_PROP_FRAME_WIDTH, self.width)
            self.camera.set(cv2.CAP_PROP_FRAME_HEIGHT, self.height)
            self.camera.set(cv2.CAP_PROP_FPS, self.fps)
            
            # Start the camera thread
            self.is_running = True
            threading.Thread(target=self._camera_thread, daemon=True).start()
            print(f"Camera {self.camera_index} started at {self.width}x{self.height} @ {self.fps} FPS")
            return True
        return False
    
    def _camera_thread(self):
        """Background thread that continuously captures frames from the camera."""
        while self.is_running:
            ret, frame = self.camera.read()
            if ret:
                # Convert to RGB for image processing
                frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                with self.lock:
                    self.current_frame = frame_rgb
            time.sleep(1/self.fps)  
    
    def get_current_frame(self):
        """
        Get the most recent camera frame.
        
        Returns:
            numpy.ndarray: Copy of the current frame or None if no frame is available.
        """
        with self.lock:
            if self.current_frame is not None:
                return self.current_frame.copy()
            return None
    
    def stop_camera(self):
        """Stop the camera and release resources."""
        self.is_running = False
        if self.camera is not None:
            self.camera.release()
            self.camera = None
            print(f"Camera {self.camera_index} stopped")
