"""
Enhanced and Optimized Object Detection for Tower Defense Game.
Combines SAM with traditional CV methods for fast and reliable detection of colored objects.
"""

import torch
import numpy as np
import cv2
import asyncio
import time
import threading
import concurrent.futures
from mobile_sam import sam_model_registry, SamPredictor, SamAutomaticMaskGenerator
from utils.image_processings import (
    enhance_image, clean_mask, validate_mask, 
    save_debug_image, mask_to_png_bytes
)
from config.settings import (
    MODEL_TYPE, MODEL_CHECKPOINT, 
    POINTS_PER_SIDE, PRED_IOU_THRESH as CFG_PRED_IOU_THRESH,
    STABILITY_SCORE_THRESH as CFG_STABILITY_SCORE_THRESH,
    CROP_N_LAYERS, CROP_N_POINTS_DOWNSCALE_FACTOR, MIN_MASK_REGION_AREA,
    DEBUG_INPUT_IMAGE, DEBUG_MASK_FINAL, MIN_BLACK_RATIO, MAX_BLACK_RATIO
)

class FastObjectDetector:
    """
    Ultra-fast and reliable object detection combining SAM with traditional CV methods.
    Specifically optimized for detecting colored objects like paper sheets on surfaces.
    """
    
    def __init__(self):
        """Initialize the detection system."""
        print("Inicializando detector rápido de objetos...")
        
        self.device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
        print(f"Usando dispositivo: {self.device}")

        # Initialize SAM with optimized settings for speed
        self.sam = None
        self.sam_predictor = None
        self.use_sam = True
        
        try:
            self.sam = sam_model_registry[MODEL_TYPE](checkpoint=MODEL_CHECKPOINT)
            self.sam.to(device=self.device)
            self.sam_predictor = SamPredictor(self.sam)
            print("SAM inicializado correctamente.")
        except Exception as e:
            print(f"Advertencia: No se pudo cargar SAM: {e}. Usando solo métodos tradicionales.")
            self.use_sam = False
        
        # Thread pool for parallel processing
        self.executor = concurrent.futures.ThreadPoolExecutor(max_workers=2)
        
        print("Detector rápido de objetos listo.")

    async def process_image(self, image, scene_type="pared", hand_points=None, aruco_corners=None, progress_callback=None, websocket=None):
        """
        Process image with ultra-fast multi-method approach.
        
        Args:
            image (numpy.ndarray): Input image in RGB format
            scene_type (str): Scene type ("pared" or "mesa")
            hand_points (list, optional): Guided points
            aruco_corners (list, optional): ArUco marker corners
            progress_callback (func, optional): Progress callback
            websocket: WebSocket connection
            
        Returns:
            bytes: PNG encoded binary mask
        """
        start_time = time.time()
        
        async def send_progress(step, progress):
            if progress_callback and websocket:
                await progress_callback(websocket, step, progress)

        await send_progress("Iniciando detección ultra-rápida...", 5)
        
        # Save debug input
        save_debug_image(image, DEBUG_INPUT_IMAGE)
        
        h, w = image.shape[:2]
        
        # Step 1: Fast preprocessing (10ms)
        await send_progress("Pre-procesando imagen...", 15)
        processed_image = self._fast_preprocess(image)
        
        # Step 2: Parallel detection using multiple methods
        await send_progress("Ejecutando detección paralela...", 30)
        
        # Run multiple detection methods in parallel
        loop = asyncio.get_running_loop()
        
        # Create tasks for parallel execution
        tasks = []
        
        # Task 1: Color-based detection (very fast)
        tasks.append(loop.run_in_executor(
            self.executor, 
            self._color_based_detection, 
            processed_image
        ))
        
        # Task 2: Contour-based detection (fast)
        tasks.append(loop.run_in_executor(
            self.executor, 
            self._contour_based_detection, 
            processed_image
        ))
        
        # Task 3: SAM detection (if available and for high-quality results)
        if self.use_sam and image.shape[0] * image.shape[1] < 640*480:  # Only for smaller images
            tasks.append(loop.run_in_executor(
                self.executor, 
                self._sam_detection, 
                cv2.resize(processed_image, (320, 240))  # Reduced resolution for speed
            ))
        
        # Wait for all tasks to complete
        await send_progress("Combinando resultados...", 70)
        results = await asyncio.gather(*tasks, return_exceptions=True)
        
        # Process results
        color_mask = results[0] if len(results) > 0 and not isinstance(results[0], Exception) else None
        contour_mask = results[1] if len(results) > 1 and not isinstance(results[1], Exception) else None
        sam_mask = results[2] if len(results) > 2 and not isinstance(results[2], Exception) else None
        
        # Step 3: Intelligent mask combination
        await send_progress("Fusionando máscaras...", 85)
        combined_mask = self._combine_masks(color_mask, contour_mask, sam_mask, h, w)
        
        # Step 4: Clear ArUco areas
        if aruco_corners is not None and combined_mask is not None:
            combined_mask = self._clear_aruco_area_from_mask(combined_mask, aruco_corners)
        
        # Step 5: Final validation and cleanup
        await send_progress("Validando resultado...", 95)
        if combined_mask is None:
            combined_mask = self._generate_emergency_fallback(image)
        
        # Apply final morphological operations for clean edges
        combined_mask = self._final_cleanup(combined_mask)
        
        # Save debug output
        save_debug_image(combined_mask, DEBUG_MASK_FINAL)
        
        processing_time = time.time() - start_time
        print(f"Detección completada en {processing_time:.2f} segundos")
        
        return mask_to_png_bytes(combined_mask)

    def _fast_preprocess(self, image):
        """Ultra-fast preprocessing optimized for colored objects."""
        # Apply minimal but effective preprocessing
        # 1. Slight gaussian blur to reduce noise
        blurred = cv2.GaussianBlur(image, (3, 3), 0.5)
        
        # 2. Enhance contrast in LAB space (faster than multiple operations)
        lab = cv2.cvtColor(blurred, cv2.COLOR_RGB2LAB)
        l, a, b = cv2.split(lab)
        
        # Apply CLAHE only to L channel
        clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
        l = clahe.apply(l)
        
        # Merge and convert back
        enhanced_lab = cv2.merge([l, a, b])
        enhanced = cv2.cvtColor(enhanced_lab, cv2.COLOR_LAB2RGB)
        
        return enhanced

    def _color_based_detection(self, image):
        """Ultra-fast color-based detection for colored objects like paper sheets."""
        try:
            h, w = image.shape[:2]
            
            # Convert to HSV for better color separation
            hsv = cv2.cvtColor(image, cv2.COLOR_RGB2HSV)
            
            # Define color ranges for common paper/object colors
            color_ranges = [
                # Red range
                ([0, 50, 50], [10, 255, 255]),
                ([170, 50, 50], [180, 255, 255]),
                # Green range
                ([40, 50, 50], [80, 255, 255]),
                # Blue range  
                ([100, 50, 50], [130, 255, 255]),
                # Yellow range
                ([20, 50, 50], [40, 255, 255]),
                # Purple/Magenta range
                ([140, 50, 50], [170, 255, 255]),
            ]
            
            combined_color_mask = np.zeros((h, w), dtype=np.uint8)
            
            # Apply each color range
            for (lower, upper) in color_ranges:
                lower = np.array(lower)
                upper = np.array(upper)
                
                mask = cv2.inRange(hsv, lower, upper)
                
                # Remove noise
                kernel = np.ones((3, 3), np.uint8)
                mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)
                mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
                
                # Only keep significant areas
                contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
                for contour in contours:
                    area = cv2.contourArea(contour)
                    if area > 500:  # Minimum area threshold
                        cv2.drawContours(combined_color_mask, [contour], -1, 255, -1)
            
            # Create binary mask (0 = obstacle, 255 = free)
            result_mask = np.ones((h, w), dtype=np.uint8) * 255
            result_mask[combined_color_mask > 0] = 0
            
            return result_mask
            
        except Exception as e:
            print(f"Error en detección por color: {e}")
            return None

    def _contour_based_detection(self, image):
        """Fast edge and contour-based detection."""
        try:
            h, w = image.shape[:2]
            
            # Convert to grayscale
            gray = cv2.cvtColor(image, cv2.COLOR_RGB2GRAY)
            
            # Apply adaptive histogram equalization
            clahe = cv2.createCLAHE(clipLimit=3.0, tileGridSize=(8, 8))
            enhanced_gray = clahe.apply(gray)
            
            # Edge detection with multiple scales
            edges1 = cv2.Canny(enhanced_gray, 50, 150)
            edges2 = cv2.Canny(enhanced_gray, 30, 100)
            
            # Combine edges
            combined_edges = cv2.bitwise_or(edges1, edges2)
            
            # Dilate edges to create connected regions
            kernel = np.ones((3, 3), np.uint8)
            dilated_edges = cv2.dilate(combined_edges, kernel, iterations=2)
            
            # Find contours
            contours, _ = cv2.findContours(dilated_edges, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
            
            # Create mask
            contour_mask = np.zeros((h, w), dtype=np.uint8)
            
            for contour in contours:
                area = cv2.contourArea(contour)
                # Filter by area and aspect ratio
                if 300 < area < (h * w * 0.3):
                    # Check if contour is roughly rectangular (good for paper sheets)
                    epsilon = 0.02 * cv2.arcLength(contour, True)
                    approx = cv2.approxPolyDP(contour, epsilon, True)
                    
                    if len(approx) >= 4:  # At least 4 corners (roughly rectangular)
                        cv2.drawContours(contour_mask, [contour], -1, 255, -1)
            
            # Create binary mask (0 = obstacle, 255 = free)
            result_mask = np.ones((h, w), dtype=np.uint8) * 255
            result_mask[contour_mask > 0] = 0
            
            return result_mask
            
        except Exception as e:
            print(f"Error en detección por contornos: {e}")
            return None

    def _sam_detection(self, image):
        """Fast SAM detection on reduced resolution."""
        try:
            if not self.use_sam:
                return None
                
            h, w = image.shape[:2]
            
            # Set image for SAM predictor
            self.sam_predictor.set_image(image)
            
            # Generate automatic masks with optimized parameters for speed
            mask_generator = SamAutomaticMaskGenerator(
                self.sam,
                points_per_side=16,  # Reduced for speed
                pred_iou_thresh=0.8,
                stability_score_thresh=0.85,
                crop_n_layers=0,  # No cropping for speed
                crop_n_points_downscale_factor=1,
                min_mask_region_area=100,
            )
            
            masks = mask_generator.generate(image)
            
            if not masks:
                return None
            
            # Combine valid masks
            combined_mask = np.ones((h, w), dtype=np.uint8) * 255
            
            for mask_data in masks:
                mask = mask_data['segmentation'].astype(np.uint8)
                area = mask_data.get('area', np.sum(mask))
                
                # Filter masks by area
                area_ratio = area / (h * w)
                if 0.01 < area_ratio < 0.5:  # Reasonable object size
                    combined_mask[mask > 0] = 0
            
            return combined_mask
            
        except Exception as e:
            print(f"Error en detección SAM: {e}")
            return None

    def _combine_masks(self, color_mask, contour_mask, sam_mask, target_h, target_w):
        """Intelligently combine masks from different detection methods."""
        try:
            # Create base mask
            combined = np.ones((target_h, target_w), dtype=np.uint8) * 255
            
            # Resize masks if needed
            def resize_mask(mask):
                if mask is None:
                    return None
                if mask.shape[:2] != (target_h, target_w):
                    return cv2.resize(mask, (target_w, target_h), interpolation=cv2.INTER_NEAREST)
                return mask
            
            color_mask = resize_mask(color_mask)
            contour_mask = resize_mask(contour_mask)
            sam_mask = resize_mask(sam_mask)
            
            # Voting system: if 2+ methods agree on a pixel being an obstacle, mark it
            votes = np.zeros((target_h, target_w), dtype=np.uint8)
            
            if color_mask is not None:
                votes[color_mask == 0] += 1
                
            if contour_mask is not None:
                votes[contour_mask == 0] += 1
                
            if sam_mask is not None:
                votes[sam_mask == 0] += 1
            
            # Apply voting threshold
            threshold = 1 if sam_mask is None else 2  # Lower threshold if SAM not available
            combined[votes >= threshold] = 0
            
            # If no significant detection, try color-only approach with lower threshold
            obstacle_ratio = np.sum(combined == 0) / (target_h * target_w)
            if obstacle_ratio < 0.01 and color_mask is not None:
                print("Aplicando detección de color con umbral reducido...")
                combined = color_mask.copy()
            
            return combined
            
        except Exception as e:
            print(f"Error combinando máscaras: {e}")
            return None

    def _final_cleanup(self, mask):
        """Apply final morphological operations for clean results."""
        if mask is None:
            return None
            
        # Remove small noise
        kernel_small = np.ones((3, 3), np.uint8)
        mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel_small)
        
        # Fill small holes
        kernel_medium = np.ones((5, 5), np.uint8)
        mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel_medium)
        
        # Final smoothing
        mask = cv2.medianBlur(mask, 3)
        
        return mask

    def _clear_aruco_area_from_mask(self, mask, aruco_corners):
        """Clear ArUco marker areas from the mask."""
        if aruco_corners is None or len(aruco_corners) == 0:
            return mask

        cleared_mask = mask.copy()
        for corners in aruco_corners:
            pts = np.array(corners[0], dtype=np.int32)
            cv2.fillPoly(cleared_mask, [pts], 255)
        
        return cleared_mask

    def _generate_emergency_fallback(self, image):
        """Generate a simple fallback mask when all methods fail."""
        h, w = image.shape[:2]
        
        # Convert to HSV and look for high saturation areas (colored objects)
        hsv = cv2.cvtColor(image, cv2.COLOR_RGB2HSV)
        _, saturation, _ = cv2.split(hsv)
        
        # Threshold on saturation to find colored areas
        _, sat_thresh = cv2.threshold(saturation, 60, 255, cv2.THRESH_BINARY)
        
        # Clean up
        kernel = np.ones((5, 5), np.uint8)
        sat_thresh = cv2.morphologyEx(sat_thresh, cv2.MORPH_CLOSE, kernel)
        sat_thresh = cv2.morphologyEx(sat_thresh, cv2.MORPH_OPEN, kernel)
        
        # Create result mask
        result_mask = np.ones((h, w), dtype=np.uint8) * 255
        result_mask[sat_thresh > 0] = 0
        
        print("Usando máscara de emergencia basada en saturación.")
        return result_mask

    def cleanup(self):
        """Clean up resources."""
        if self.executor:
            self.executor.shutdown(wait=True)

# Backward compatibility - keep the original class name
class SAMProcessor(FastObjectDetector):
    """Alias for backward compatibility."""
    pass