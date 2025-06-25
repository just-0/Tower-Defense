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
    Ultra-fast and reliable object detection optimized for colored paper sheets on walls.
    Simplified approach focusing on color discrimination and geometric validation.
    """
    
    def __init__(self):
        """Initialize the detection system."""
        print("Inicializando detector optimizado para hojas de colores...")
        
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
        
        print("Detector optimizado listo.")

    async def process_image(self, image, scene_type="pared", hand_points=None, aruco_corners=None, progress_callback=None, websocket=None):
        """
        Process image with optimized approach for colored sheets on walls.
        
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

        await send_progress("Iniciando detección optimizada...", 5)
        
        # Save debug input
        save_debug_image(image, DEBUG_INPUT_IMAGE)
        
        h, w = image.shape[:2]
        
        # Step 1: Fast preprocessing
        await send_progress("Pre-procesando imagen...", 15)
        processed_image = self._optimized_preprocess(image)
        
        # Step 2: Primary color-based detection (optimized for colored sheets)
        await send_progress("Detectando hojas de colores...", 40)
        
        # Use only the optimized color detection - it's the most reliable for this use case
        loop = asyncio.get_running_loop()
        color_mask = await loop.run_in_executor(
            self.executor, 
            self._optimized_color_detection, 
            processed_image
        )
        
        # Step 3: Optional SAM validation for edge cases (only if needed)
        sam_mask = None
        if self.use_sam and color_mask is not None:
            detected_ratio = np.sum(color_mask == 0) / (h * w)
            # Only use SAM if color detection seems uncertain
            if detected_ratio < 0.02 or detected_ratio > 0.4:
                await send_progress("Validando con SAM...", 70)
                sam_mask = await loop.run_in_executor(
                    self.executor, 
                    self._lightweight_sam_detection, 
                    cv2.resize(processed_image, (320, 240))
                )
        
        # Step 4: Combine results intelligently
        await send_progress("Procesando resultado final...", 85)
        final_mask = self._intelligent_combine(color_mask, sam_mask, h, w)
        
        # Step 5: Clear ArUco areas
        if aruco_corners is not None and final_mask is not None:
            final_mask = self._clear_aruco_area_from_mask(final_mask, aruco_corners)
        
        # Step 6: Final validation and cleanup
        await send_progress("Validando resultado...", 95)
        if final_mask is None:
            final_mask = self._simple_fallback(image)
        
        # Apply final cleanup
        final_mask = self._final_cleanup(final_mask)
        
        # Save debug output
        save_debug_image(final_mask, DEBUG_MASK_FINAL)
        
        processing_time = time.time() - start_time
        print(f"Detección completada en {processing_time:.2f} segundos")
        
        return mask_to_png_bytes(final_mask)

    def _optimized_preprocess(self, image):
        """Minimal but effective preprocessing for colored sheet detection."""
        # Light gaussian blur to reduce camera noise
        blurred = cv2.GaussianBlur(image, (3, 3), 0.8)
        
        # Enhance contrast slightly in LAB space
        lab = cv2.cvtColor(blurred, cv2.COLOR_RGB2LAB)
        l, a, b = cv2.split(lab)
        
        # Very mild CLAHE to preserve color relationships
        clahe = cv2.createCLAHE(clipLimit=1.5, tileGridSize=(8, 8))
        l = clahe.apply(l)
        
        enhanced_lab = cv2.merge([l, a, b])
        enhanced = cv2.cvtColor(enhanced_lab, cv2.COLOR_LAB2RGB)
        
        return enhanced

    def _optimized_color_detection(self, image):
        """
        Optimized detection specifically for colored paper sheets on walls.
        Focuses on high color purity and geometric constraints.
        """
        try:
            h, w = image.shape[:2]
            print("Detectando hojas de colores con método optimizado...")
            
            # Convert to HSV for better color analysis
            hsv = cv2.cvtColor(image, cv2.COLOR_RGB2HSV)
            hue, saturation, value = cv2.split(hsv)
            
            # Strategy 1: High saturation objects (colored papers)
            # Use adaptive threshold based on image statistics
            sat_mean = np.mean(saturation)
            sat_std = np.std(saturation)
            
            # More conservative threshold - we want clearly colored objects
            sat_threshold = max(60, int(sat_mean + sat_std * 1.2))
            print(f"Umbral de saturación: {sat_threshold}")
            
            # High saturation mask
            high_sat_mask = saturation > sat_threshold
            
            # Strategy 2: Color purity analysis
            # Detect regions with strong color dominance
            # Calculate color variance in small neighborhoods
            kernel = np.ones((5, 5), np.float32) / 25
            hue_smoothed = cv2.filter2D(hue.astype(np.float32), -1, kernel)
            hue_variance = np.abs(hue.astype(np.float32) - hue_smoothed)
            
            # Low variance indicates uniform colored regions
            color_purity_mask = hue_variance < 15  # Uniform color regions
            
            # Strategy 3: Specific color ranges (tighter ranges for better precision)
            color_ranges = [
                # Azul
                ([100, 60, 50], [120, 255, 255]),
                # Verde
                ([40, 60, 50], [80, 255, 255]),
                # Rojo (dos rangos)
                ([0, 60, 50], [10, 255, 255]),
                ([170, 60, 50], [180, 255, 255]),
                # Rosa/Magenta
                ([140, 60, 50], [160, 255, 255]),
                # Amarillo
                ([20, 60, 50], [30, 255, 255]),
            ]
            
            specific_colors_mask = np.zeros((h, w), dtype=np.uint8)
            for i, (lower, upper) in enumerate(color_ranges):
                lower = np.array(lower)
                upper = np.array(upper)
                range_mask = cv2.inRange(hsv, lower, upper)
                
                # Only keep regions with sufficient area
                contours, _ = cv2.findContours(range_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
                for contour in contours:
                    area = cv2.contourArea(contour)
                    if area > 800:  # Minimum area for a sheet
                        cv2.drawContours(specific_colors_mask, [contour], -1, 255, -1)
            
            # Combine strategies using intersection (more conservative)
            combined_mask = cv2.bitwise_and(
                high_sat_mask.astype(np.uint8) * 255,
                color_purity_mask.astype(np.uint8) * 255
            )
            
            # Add specific color detections
            combined_mask = cv2.bitwise_or(combined_mask, specific_colors_mask)
            
            # Morphological cleaning - conservative
            kernel_small = np.ones((3, 3), np.uint8)
            combined_mask = cv2.morphologyEx(combined_mask, cv2.MORPH_OPEN, kernel_small, iterations=1)
            
            kernel_close = np.ones((5, 5), np.uint8)
            combined_mask = cv2.morphologyEx(combined_mask, cv2.MORPH_CLOSE, kernel_close, iterations=2)
            
            # Geometric validation for sheet-like objects
            contours, _ = cv2.findContours(combined_mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
            final_mask = np.zeros((h, w), dtype=np.uint8)
            
            min_area = 1000  # Minimum area for a sheet
            max_area = h * w * 0.25  # Maximum reasonable area
            
            objects_found = 0
            for contour in contours:
                area = cv2.contourArea(contour)
                
                if min_area < area < max_area:
                    # Check if roughly rectangular (good for paper sheets)
                    x, y, w_rect, h_rect = cv2.boundingRect(contour)
                    aspect_ratio = max(w_rect, h_rect) / max(min(w_rect, h_rect), 1)
                    
                    # Calculate solidity (convex hull ratio)
                    hull = cv2.convexHull(contour)
                    hull_area = cv2.contourArea(hull)
                    solidity = area / hull_area if hull_area > 0 else 0
                    
                    # Calculate extent (bounding box ratio)
                    extent = area / (w_rect * h_rect)
                    
                    # Criteria for sheet-like objects
                    if (aspect_ratio < 4 and solidity > 0.6 and extent > 0.4):
                        cv2.drawContours(final_mask, [contour], -1, 255, -1)
                        objects_found += 1
                        print(f"Hoja {objects_found}: área={area:.0f}, ratio={aspect_ratio:.2f}, solidez={solidity:.2f}, extensión={extent:.2f}")
            
            print(f"Total de hojas detectadas: {objects_found}")
            
            # Create result mask (0 = obstacle/sheet, 255 = free space)
            result_mask = np.ones((h, w), dtype=np.uint8) * 255
            result_mask[final_mask > 0] = 0
            
            detected_ratio = np.sum(final_mask > 0) / (h * w)
            print(f"Ratio de hojas detectadas: {detected_ratio:.4f}")
            
            return result_mask
            
        except Exception as e:
            print(f"Error en detección optimizada por color: {e}")
            return None

    def _lightweight_sam_detection(self, image):
        """Lightweight SAM detection for validation purposes."""
        try:
            if not self.use_sam:
                return None
                
            h, w = image.shape[:2]
            
            self.sam_predictor.set_image(image)
            
            # Very lightweight mask generation
            mask_generator = SamAutomaticMaskGenerator(
                self.sam,
                points_per_side=12,  # Very reduced for speed
                pred_iou_thresh=0.9,
                stability_score_thresh=0.9,
                crop_n_layers=0,
                crop_n_points_downscale_factor=1,
                min_mask_region_area=200,
            )
            
            masks = mask_generator.generate(image)
            
            if not masks:
                return None
            
            # Only keep masks that look like sheets
            combined_mask = np.ones((h, w), dtype=np.uint8) * 255
            
            for mask_data in masks:
                mask = mask_data['segmentation'].astype(np.uint8)
                area = mask_data.get('area', np.sum(mask))
                
                # Filter by reasonable area for sheets
                area_ratio = area / (h * w)
                if 0.02 < area_ratio < 0.3:
                    combined_mask[mask > 0] = 0
            
            return combined_mask
            
        except Exception as e:
            print(f"Error en SAM ligero: {e}")
            return None

    def _intelligent_combine(self, color_mask, sam_mask, target_h, target_w):
        """Intelligent combination prioritizing color detection with SAM validation."""
        try:
            def resize_mask(mask):
                if mask is None:
                    return None
                if mask.shape[:2] != (target_h, target_w):
                    return cv2.resize(mask, (target_w, target_h), interpolation=cv2.INTER_NEAREST)
                return mask
            
            color_mask = resize_mask(color_mask)
            sam_mask = resize_mask(sam_mask)
            
            # Primary: use color detection
            if color_mask is not None:
                result = color_mask.copy()
                color_ratio = np.sum(color_mask == 0) / (target_h * target_w)
                print(f"Detección principal por color: {color_ratio:.4f}")
                
                # Use SAM as validation only if color detection seems inadequate
                if sam_mask is not None and color_ratio < 0.01:
                    sam_ratio = np.sum(sam_mask == 0) / (target_h * target_w)
                    if 0.01 < sam_ratio < 0.3:
                        print("Usando SAM como respaldo por baja detección de color")
                        result = sam_mask.copy()
                
                return result
            
            # Fallback to SAM if color detection failed
            elif sam_mask is not None:
                print("Usando SAM como método principal (color falló)")
                return sam_mask
            
            return None
            
        except Exception as e:
            print(f"Error combinando máscaras: {e}")
            return None

    def _simple_fallback(self, image):
        """Simple fallback using basic color thresholding."""
        h, w = image.shape[:2]
        print("Generando máscara de emergencia simple...")
        
        hsv = cv2.cvtColor(image, cv2.COLOR_RGB2HSV)
        
        # Very basic saturation thresholding
        _, saturation, _ = cv2.split(hsv)
        sat_thresh = cv2.threshold(saturation, 50, 255, cv2.THRESH_BINARY)[1]
        
        # Basic morphological cleaning
        kernel = np.ones((5, 5), np.uint8)
        cleaned = cv2.morphologyEx(sat_thresh, cv2.MORPH_CLOSE, kernel, iterations=2)
        cleaned = cv2.morphologyEx(cleaned, cv2.MORPH_OPEN, kernel, iterations=1)
        
        # Create result mask
        result_mask = np.ones((h, w), dtype=np.uint8) * 255
        result_mask[cleaned > 0] = 0
        
        return result_mask

    def _final_cleanup(self, mask):
        """Apply final morphological operations for clean results."""
        if mask is None:
            return None
            
        # Light cleaning only
        kernel_small = np.ones((3, 3), np.uint8)
        mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel_small)
        
        # Light hole filling
        kernel_medium = np.ones((4, 4), np.uint8)
        mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel_medium)
        
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

    def cleanup(self):
        """Clean up resources."""
        if self.executor:
            self.executor.shutdown(wait=True)

# Backward compatibility - keep the original class name
class SAMProcessor(FastObjectDetector):
    """Alias for backward compatibility."""
    pass