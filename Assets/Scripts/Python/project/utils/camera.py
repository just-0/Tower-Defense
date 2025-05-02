"""
Camera management module for capturing and processing video frames.
"""

import cv2
import threading
import time
from config.settings import CAMERA_WIDTH, CAMERA_HEIGHT, CAMERA_FPS, CAMERA_INDEX

class CameraManager:
    """
    Manages camera operations including initialization, frame capture, and cleanup.
    """
    
    def __init__(self):
        """Initialize the camera manager."""
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
            self.camera = cv2.VideoCapture(CAMERA_INDEX)
            
            if not self.camera.isOpened():
                print("Error: Could not open camera")
                return False

            # Set camera properties
            self.camera.set(cv2.CAP_PROP_FRAME_WIDTH, CAMERA_WIDTH)
            self.camera.set(cv2.CAP_PROP_FRAME_HEIGHT, CAMERA_HEIGHT)
            
            # Start the camera thread
            self.is_running = True
            threading.Thread(target=self._camera_thread, daemon=True).start()
            print("Camera started")
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
            time.sleep(1/CAMERA_FPS)  
    
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
            print("Camera stopped")
