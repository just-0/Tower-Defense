"""
Camera management utilities for the application.
Enhanced with automatic resolution detection and optimization.
"""

import cv2
import threading
import time
import numpy as np
from config.settings import (
    AUTO_DETECT_CAMERA_RESOLUTION, MAX_RESOLUTION_WIDTH, MAX_RESOLUTION_HEIGHT,
    MIN_RESOLUTION_WIDTH, MIN_RESOLUTION_HEIGHT
)

def detect_optimal_camera_resolution(camera_index, preferred_width=640, preferred_height=480):
    """
    Detects the optimal resolution for a camera by testing different configurations.
    
    Args:
        camera_index (int): Camera index to test
        preferred_width (int): Preferred width
        preferred_height (int): Preferred height
        
    Returns:
        tuple: (actual_width, actual_height, fps) or None if camera not available
    """
    print(f"Detectando resolución óptima para cámara {camera_index}...")
    
    cap = cv2.VideoCapture(camera_index)
    if not cap.isOpened():
        print(f"No se pudo abrir la cámara {camera_index}")
        return None
    
    try:
        # First, try to set the preferred resolution
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, preferred_width)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, preferred_height)
        cap.set(cv2.CAP_PROP_FPS, 30)
        
        # Try to use MJPG format for better performance if available
        cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*'MJPG'))
        
        # Read a few frames to stabilize
        for _ in range(5):
            ret, frame = cap.read()
            if not ret:
                break
        
        # Get actual resolution
        actual_width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
        actual_height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
        actual_fps = cap.get(cv2.CAP_PROP_FPS)
        
        # Validate resolution is within acceptable bounds
        if (actual_width < MIN_RESOLUTION_WIDTH or actual_height < MIN_RESOLUTION_HEIGHT or
            actual_width > MAX_RESOLUTION_WIDTH or actual_height > MAX_RESOLUTION_HEIGHT):
            
            print(f"Resolución {actual_width}x{actual_height} fuera de límites, probando alternativas...")
            
            # Try common resolutions in order of preference
            test_resolutions = [
                (640, 480),   # VGA
                (800, 600),   # SVGA
                (1280, 720),  # HD
                (960, 720),   # Custom
                (320, 240),   # QVGA
            ]
            
            best_resolution = None
            for test_w, test_h in test_resolutions:
                if (MIN_RESOLUTION_WIDTH <= test_w <= MAX_RESOLUTION_WIDTH and
                    MIN_RESOLUTION_HEIGHT <= test_h <= MAX_RESOLUTION_HEIGHT):
                    
                    cap.set(cv2.CAP_PROP_FRAME_WIDTH, test_w)
                    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, test_h)
                    
                    # Test if this resolution works
                    for _ in range(3):
                        ret, frame = cap.read()
                        if ret and frame is not None:
                            achieved_w = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
                            achieved_h = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
                            
                            if achieved_w == test_w and achieved_h == test_h:
                                best_resolution = (achieved_w, achieved_h)
                                break
                    
                    if best_resolution:
                        break
            
            if best_resolution:
                actual_width, actual_height = best_resolution
                actual_fps = cap.get(cv2.CAP_PROP_FPS)
            else:
                print(f"No se encontró resolución válida para cámara {camera_index}")
                return None
        
        print(f"Cámara {camera_index}: Resolución detectada {actual_width}x{actual_height} @ {actual_fps}fps")
        return (actual_width, actual_height, actual_fps)
        
    except Exception as e:
        print(f"Error detectando resolución de cámara {camera_index}: {e}")
        return None
    finally:
        cap.release()

class CameraManager:
    """
    Enhanced camera manager with automatic resolution detection and optimization.
    """
    
    def __init__(self, camera_index=0, width=640, height=480, fps=30):
        """
        Initialize camera manager with automatic resolution detection.
        
        Args:
            camera_index (int): Camera index
            width (int): Preferred width
            height (int): Preferred height
            fps (int): Target FPS
        """
        self.camera_index = camera_index
        self.preferred_width = width
        self.preferred_height = height
        self.fps = fps
        
        # Actual camera properties (will be detected)
        self.width = width
        self.height = height
        self.actual_fps = fps
        
        self.cap = None
        self.is_running = False
        self.current_frame = None
        self.frame_lock = threading.Lock()
        self.capture_thread = None
        
        # Auto-detect resolution if enabled
        if AUTO_DETECT_CAMERA_RESOLUTION:
            detected = detect_optimal_camera_resolution(camera_index, width, height)
            if detected:
                self.width, self.height, self.actual_fps = detected
                print(f"CameraManager: Usando resolución detectada {self.width}x{self.height}")
            else:
                print(f"CameraManager: Usando resolución por defecto {self.width}x{self.height}")

    def start_camera(self):
        """Start the camera with optimized settings."""
        if self.is_running:
            return True
            
        print(f"Iniciando cámara {self.camera_index} con resolución {self.width}x{self.height}")
        
        self.cap = cv2.VideoCapture(self.camera_index)
        if not self.cap.isOpened():
            print(f"Error: No se pudo abrir la cámara {self.camera_index}")
            return False
        
        # Configure camera with detected/optimal settings
        try:
            # Set format to MJPG for better performance if supported
            self.cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*'MJPG'))
            
            # Set resolution
            self.cap.set(cv2.CAP_PROP_FRAME_WIDTH, self.width)
            self.cap.set(cv2.CAP_PROP_FRAME_HEIGHT, self.height)
            self.cap.set(cv2.CAP_PROP_FPS, self.fps)
            
            # Additional optimizations
            self.cap.set(cv2.CAP_PROP_BUFFERSIZE, 1)  # Minimize lag
            
            # Verify actual settings
            actual_width = int(self.cap.get(cv2.CAP_PROP_FRAME_WIDTH))
            actual_height = int(self.cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
            actual_fps = self.cap.get(cv2.CAP_PROP_FPS)
            
            if actual_width != self.width or actual_height != self.height:
                print(f"Advertencia: Resolución solicitada {self.width}x{self.height}, "
                      f"obtenida {actual_width}x{actual_height}")
                self.width = actual_width
                self.height = actual_height
            
            self.actual_fps = actual_fps
            
        except Exception as e:
            print(f"Error configurando cámara: {e}")
        
        # Test camera by reading a frame
        ret, frame = self.cap.read()
        if not ret or frame is None:
            print("Error: No se pudo leer de la cámara")
            self.cap.release()
            return False
        
        self.is_running = True
        
        # Start capture thread
        self.capture_thread = threading.Thread(target=self._capture_loop, daemon=True)
        self.capture_thread.start()
        
        print(f"Cámara iniciada exitosamente: {self.width}x{self.height} @ {self.actual_fps}fps")
        return True

    def _capture_loop(self):
        """Main capture loop running in separate thread."""
        while self.is_running and self.cap is not None:
            try:
                ret, frame = self.cap.read()
                if ret and frame is not None:
                    # Convert BGR to RGB for consistency
                    frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                    
                    with self.frame_lock:
                        self.current_frame = frame_rgb.copy()
                else:
                    time.sleep(0.01)  # Short sleep on read failure
                    
            except Exception as e:
                print(f"Error en captura de cámara: {e}")
                time.sleep(0.1)
        
        print(f"Bucle de captura de cámara {self.camera_index} terminado")

    def get_current_frame(self):
        """Get the current frame in RGB format."""
        with self.frame_lock:
            return self.current_frame.copy() if self.current_frame is not None else None

    def get_resolution(self):
        """Get the actual camera resolution."""
        return (self.width, self.height)
    
    def get_fps(self):
        """Get the actual camera FPS."""
        return self.actual_fps

    def stop_camera(self):
        """Stop the camera."""
        if not self.is_running:
            return
            
        print(f"Deteniendo cámara {self.camera_index}")
        self.is_running = False
        
        # Wait for capture thread to finish
        if self.capture_thread and self.capture_thread.is_alive():
            self.capture_thread.join(timeout=2.0)
        
        # Release camera
        if self.cap:
            self.cap.release()
            self.cap = None
        
        with self.frame_lock:
            self.current_frame = None
        
        print(f"Cámara {self.camera_index} detenida")

    def __del__(self):
        """Cleanup when object is destroyed."""
        self.stop_camera()
