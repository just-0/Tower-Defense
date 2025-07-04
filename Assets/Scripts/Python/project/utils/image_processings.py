"""
Utility functions for image processing and manipulation.
"""

import cv2
import numpy as np
import io
from PIL import Image
from config.settings import (
    JPEG_QUALITY, DEBUG_ENABLED, 
    DEBUG_INPUT_IMAGE, DEBUG_MASK_FINAL
)

def convert_to_rgb(image):
    """
    Convert BGR image to RGB if needed.
    
    Args:
        image (numpy.ndarray): Input image
        
    Returns:
        numpy.ndarray: RGB image
    """
    if image.shape[2] == 3 and image.dtype == 'uint8':
        return cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    return image

def enhance_image(image):
    """
    Apply basic enhancement to improve image quality.
    
    Args:
        image (numpy.ndarray): Input image
        
    Returns:
        numpy.ndarray: Enhanced image
    """
    # Apply Gaussian blur to reduce noise
    return cv2.GaussianBlur(image, (3, 3), 0)

def save_debug_image(image, filename):
    """
    Save an image for debugging purposes.
    
    Args:
        image (numpy.ndarray): Image to save
        filename (str): Filename to save the image as
    """
    if DEBUG_ENABLED:
        # Check if the image is 2D (grayscale), and convert it to 3D (RGB)
        if len(image.shape) == 2:  # Grayscale image
            image = cv2.cvtColor(image, cv2.COLOR_GRAY2BGR)

        # Now, it's safe to check shape[2]
        if image.shape[2] == 3:
            save_image = cv2.cvtColor(image, cv2.COLOR_RGB2BGR)
        else:
            save_image = image
        cv2.imwrite(filename, save_image)


def clean_mask(mask):
    """
    Clean a binary mask using morphological operations.
    
    Args:
        mask (numpy.ndarray): Binary mask
        
    Returns:
        numpy.ndarray: Cleaned mask
    """
    kernel = np.ones((3, 3), np.uint8)
    # Open operation (erosion followed by dilation) to remove small noise
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel, iterations=1)
    # Close operation (dilation followed by erosion) to fill small holes
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel, iterations=1)
    return mask

def validate_mask(mask, min_ratio=0.05, max_ratio=0.85):
    """
    Validate a mask to ensure it's reasonable.
    
    Args:
        mask (numpy.ndarray): Binary mask
        min_ratio (float): Minimum acceptable ratio of black pixels
        max_ratio (float): Maximum acceptable ratio of black pixels
        
    Returns:
        tuple: (is_valid, ratio)
    """
    h, w = mask.shape[:2]
    black_ratio = np.sum(mask == 0) / (h * w)
    
    # Check if the mask covers too much or too little of the image
    if black_ratio < min_ratio or black_ratio > max_ratio:
        return False, black_ratio
    return True, black_ratio

def mask_to_png_bytes(mask):
    """
    Convert a binary mask to PNG bytes.
    
    Args:
        mask (numpy.ndarray): Binary mask
        
    Returns:
        bytes: PNG encoded mask
    """
    mask_pil = Image.fromarray(mask)
    buffer_mask = io.BytesIO()
    mask_pil.save(buffer_mask, format="PNG")
    return buffer_mask.getvalue()

def encode_frame_to_jpeg(frame, quality=None):
    """
    Encode a frame to JPEG bytes.
    
    Args:
        frame (numpy.ndarray): Frame to encode (expects BGR format from OpenCV)
        quality (int, optional): JPEG quality (0-100). If None, uses JPEG_QUALITY from settings.
        
    Returns:
        tuple: (success (bool), encoded_bytes (bytes))
    """
    # Verificar que frame sea válido
    if frame is None or not isinstance(frame, np.ndarray) or frame.size == 0:
        return False, None
        
    try:
        # ARREGLO: Los frames de finger_tracking.py siempre vienen en BGR
        # No necesitamos heurísticas complicadas que causan el tinte azul intermitente
        frame_bgr = frame
        
        # Asegurar que sea uint8
        if frame_bgr.dtype != np.uint8:
            frame_bgr = frame_bgr.astype(np.uint8)
            
        # Debug ocasional para confirmar formato correcto
        if hasattr(encode_frame_to_jpeg, '_debug_counter'):
            encode_frame_to_jpeg._debug_counter += 1
        else:
            encode_frame_to_jpeg._debug_counter = 1
            
        if encode_frame_to_jpeg._debug_counter % 200 == 0:  # Cada 200 codificaciones
            print(f"[encode_frame_to_jpeg] #{encode_frame_to_jpeg._debug_counter}: Codificando frame BGR {frame_bgr.shape}, dtype={frame_bgr.dtype}")
            
        # Usar calidad personalizada o la configurada
        jpeg_quality = quality if quality is not None else JPEG_QUALITY
        
        success, encoded_frame = cv2.imencode(
            '.jpg', 
            frame_bgr, 
            [cv2.IMWRITE_JPEG_QUALITY, jpeg_quality]
        )
        
        if success:
            return success, encoded_frame.tobytes()
        return False, None
    except Exception as e:
        print(f"Error al codificar imagen: {e}")
        return False, None