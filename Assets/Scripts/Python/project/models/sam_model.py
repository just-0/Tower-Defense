"""
Enhanced Segment Anything Model (SAM) implementation for object detection.
Optimized for both wall and table scenarios with scene-specific processing.
"""

import torch
import numpy as np
import cv2
import asyncio # Importar asyncio
import time # Importar time para la simulación
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

class SAMProcessor:
    """
    Handles processing images using the Segment Anything Model (SAM).
    Enhanced to handle different scenarios: wall-facing and table-facing.
    """
    
    def __init__(self):
        """Initialize the SAM model."""
        print("Initializing Mobile SAM model...")
        
        self.device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
        print(f"Using device: {self.device}")

        self.sam = sam_model_registry[MODEL_TYPE](checkpoint=MODEL_CHECKPOINT)
        self.sam.to(device=self.device)
        
        # Optimized parameters for better obstacle detection on walls
        current_pred_iou_thresh = 0.85 
        current_stability_score_thresh = 0.90
        
        print(f"Using SAM params: PRED_IOU_THRESH={current_pred_iou_thresh}, STABILITY_SCORE_THRESH={current_stability_score_thresh}")
        
        self.mask_generator = SamAutomaticMaskGenerator(
            self.sam,
            points_per_side=POINTS_PER_SIDE,
            pred_iou_thresh=current_pred_iou_thresh,
            stability_score_thresh=current_stability_score_thresh,
            crop_n_layers=CROP_N_LAYERS,
            crop_n_points_downscale_factor=CROP_N_POINTS_DOWNSCALE_FACTOR,
            min_mask_region_area=MIN_MASK_REGION_AREA,
        )
        print("Mobile SAM model initialized.")

    async def process_image(self, image, scene_type="pared", hand_points=None, aruco_corners=None, progress_callback=None, websocket=None):
        """
        Process an image to generate object masks with scene-specific optimizations.
        Includes simulated progress for the main SAM generation task.
        
        Args:
            image (numpy.ndarray): Input image in RGB format
            scene_type (str): Either "wall" or "table" to specify the scenario
            hand_points (list, optional): List of [x, y] coordinates for guided segmentation
            aruco_corners (list, optional): List of corner coordinates for ArUco markers.
                                          Each element is an array of shape (1, 4, 2) for a marker.
            progress_callback (func, optional): Async function to call for progress updates.
            websocket (WebSocketServerProtocol, optional): WebSocket connection for the callback.
            
        Returns:
            bytes: PNG encoded binary mask or None if processing failed
        """
        async def send_progress(step, progress):
            if progress_callback and websocket:
                await progress_callback(websocket, step, progress)

        await send_progress("Pre-procesando imagen...", 45)
        # Save debug input image
        save_debug_image(image, DEBUG_INPUT_IMAGE)
        
        # Scene-specific preprocessing
        if scene_type.lower() == "table":
            preprocessed_image = self._preprocess_table_scene(image)
        else:  # Default to wall processing
            preprocessed_image = self._preprocess_wall_scene(image)
        
        h, w = image.shape[:2]
        
        # --- SIMULACIÓN DE PROGRESO PARA TAREA PESADA ---
        await send_progress("Generando máscaras con SAM...", 50)
        loop = asyncio.get_running_loop()

        async def simulate_progress(task_to_monitor):
            estimated_duration = 30.0  # Duración estimada en segundos (ajustado a 30s)
            start_progress = 50.0
            target_progress = 75.0
            start_time = time.time()
            
            while not task_to_monitor.done():
                elapsed_time = time.time() - start_time
                simulated_p = min(1.0, elapsed_time / estimated_duration)
                current_simulated_progress = start_progress + simulated_p * (target_progress - start_progress)
                
                await send_progress("Procesando imagen (SAM)...", current_simulated_progress)
                await asyncio.sleep(1)

        if hand_points and len(hand_points) > 0:
            masks = await loop.run_in_executor(None, self._process_with_points, preprocessed_image, hand_points)
        else:
            sam_task = loop.run_in_executor(None, self._process_automatic, preprocessed_image)
            simulation_task = asyncio.create_task(simulate_progress(sam_task))
            masks = await sam_task
            simulation_task.cancel()
            try:
                await simulation_task
            except asyncio.CancelledError:
                pass # Cancelación esperada

        if not masks:
            print(f"No masks found in {scene_type} scene!")
            await send_progress("No se encontraron máscaras.", 55)
            return None

        # --- FIN DE SIMULACIÓN, INICIO DE PROGRESO REAL ---
        await send_progress("Post-procesando máscaras...", 75)

        # Scene-specific mask processing
        if scene_type.lower() == "table":
            combined_mask = await self._process_table_masks(masks, h, w, image, progress_callback, websocket)
        else:
            combined_mask = await self._process_wall_masks(masks, h, w, image, progress_callback, websocket)
        
        # Clear ArUco marker areas from the mask
        if aruco_corners is not None and combined_mask is not None:
            await send_progress("Limpiando área de marcador...", 88)
            combined_mask = self._clear_aruco_area_from_mask(combined_mask, aruco_corners)
            print("Cleared ArUco areas from mask.")

        # Validate the final mask
        is_valid, black_ratio = validate_mask(combined_mask, MIN_BLACK_RATIO, MAX_BLACK_RATIO)
        if not is_valid:
            print(f"Warning: Mask may be incorrect ({(black_ratio*100):.1f}% black)")
            combined_mask = self._generate_fallback_mask(image, scene_type)
        
        # Save debug output mask
        save_debug_image(combined_mask, DEBUG_MASK_FINAL)
        
        # Convert to PNG bytes
        return mask_to_png_bytes(combined_mask)

    def _clear_aruco_area_from_mask(self, mask, aruco_corners):
        """
        Clears the areas occupied by ArUco markers from the segmentation mask.
        Args:
            mask (numpy.ndarray): The combined binary mask (0 for obstacle, 255 for free).
            aruco_corners (list): List of corner coordinates for ArUco markers.
                                  Each element is an array of shape (1, 4, 2) from cv2.aruco.detectMarkers.
        Returns:
            numpy.ndarray: The mask with ArUco areas cleared (set to 255).
        """
        if aruco_corners is None or len(aruco_corners) == 0:
            return mask

        # Create a copy to modify
        cleared_mask = mask.copy()

        for corners in aruco_corners:
            # Corners from detectMarkers are float, convert to int for drawing
            pts = np.array(corners[0], dtype=np.int32)
            # Fill the polygon defined by ArUco corners with white (255 - free space)
            cv2.fillPoly(cleared_mask, [pts], 255)
        
        return cleared_mask

    def _preprocess_wall_scene(self, image):
        """
        Preprocess image for wall scenario.
        For walls, we enhance contrast to make objects stand out.
        
        Args:
            image (numpy.ndarray): Original image in RGB format
            
        Returns:
            numpy.ndarray: Preprocessed image in RGB format
        """
        # 1. Usar la función de realce genérica si existe y es beneficiosa
        # (Asumiendo que enhance_image devuelve RGB si la entrada es RGB)
        enhanced_initial = enhance_image(image) 

        # 2. Convertir a Lab para trabajar con el canal de Luminosidad (L)
        lab_image = cv2.cvtColor(enhanced_initial, cv2.COLOR_RGB2Lab)
        l_channel, a_channel, b_channel = cv2.split(lab_image)

        # 3. Aplicar CLAHE al canal L para mejorar el contraste
        clahe = cv2.createCLAHE(clipLimit=2.5, tileGridSize=(8, 8)) # clipLimit un poco más alto
        cl_channel = clahe.apply(l_channel)

        # 4. Unir los canales de nuevo y convertir de vuelta a RGB
        updated_lab_image = cv2.merge((cl_channel, a_channel, b_channel))
        contrasted_rgb_image = cv2.cvtColor(updated_lab_image, cv2.COLOR_Lab2RGB)

        # 5. Aplicar filtro bilateral para suavizar ruido después del realce de contraste,
        #    manteniendo los bordes nítidos.
        #    Los parámetros (d, sigmaColor, sigmaSpace) pueden necesitar ajuste.
        #    d: Diámetro del vecindario de cada píxel. Valores más grandes significan más desenfoque.
        #    sigmaColor: Filtra colores similares. Valores más grandes influyen en más colores.
        #    sigmaSpace: Filtra píxeles cercanos. Valores más grandes influyen en píxeles más lejanos.
        bilateral_filtered_rgb = cv2.bilateralFilter(contrasted_rgb_image, d=7, sigmaColor=75, sigmaSpace=75)
        # Aumenté un poco sigmaColor y sigmaSpace para un suavizado más fuerte si el contraste introdujo artefactos.
        
        return bilateral_filtered_rgb

    def _preprocess_table_scene(self, image):
        """
        Preprocess image for table scenario.
        For tables viewed from above, we try to identify the table surface first.
        
        Args:
            image (numpy.ndarray): Original image
            
        Returns:
            numpy.ndarray: Preprocessed image
        """
        # Apply basic enhancement
        enhanced = enhance_image(image)
        
        # Apply bilateral filter to reduce noise while preserving edges
        bilateral = cv2.bilateralFilter(enhanced, 9, 75, 75)
        
        # Try to detect edges - useful for finding table boundaries
        gray = cv2.cvtColor(bilateral, cv2.COLOR_RGB2GRAY)
        edges = cv2.Canny(gray, 50, 150)
        
        # Dilate edges to make them more prominent
        kernel = np.ones((3,3), np.uint8)
        dilated_edges = cv2.dilate(edges, kernel, iterations=1)
        
        # Add edges back to the enhanced image to highlight boundaries
        edge_overlay = enhanced.copy()
        edge_overlay[dilated_edges > 0] = [255, 255, 255]  # Highlight edges
        
        # Blend original enhanced image with edge overlay
        alpha = 0.2
        enhanced = cv2.addWeighted(enhanced, 1-alpha, edge_overlay, alpha, 0)
        
        return enhanced

    def _process_with_points(self, image, points):
        """
        Process an image with guided points.
        
        Args:
            image (numpy.ndarray): Enhanced image
            points (list): List of [x, y] coordinates
            
        Returns:
            list: List of mask data dictionaries
        """
        predictor = SamPredictor(self.sam)
        predictor.set_image(image)
        
        input_points = np.array(points)
        input_labels = np.ones(len(points))  # All points are foreground
        
        masks, scores, _ = predictor.predict(
            point_coords=input_points,
            point_labels=input_labels,
            multimask_output=True,
        )
        
        # Select the best mask
        best_mask_idx = np.argmax(scores)
        return [{'segmentation': masks[best_mask_idx], 'area': np.sum(masks[best_mask_idx])}]

    def _process_automatic(self, image):
        """
        Process an image automatically without guidance.
        
        Args:
            image (numpy.ndarray): Enhanced image
            
        Returns:
            list: List of mask data dictionaries
        """
        return self.mask_generator.generate(image)

    async def _process_wall_masks(self, masks, height, width, original_image_rgb, progress_callback=None, websocket=None):
        """
        Combine masks for wall scenario. Reports detailed progress.
        """
        async def send_progress(step, progress):
            if progress_callback and websocket:
                await progress_callback(websocket, step, progress)

        combined_mask = np.ones((height, width), dtype=np.uint8) * 255
        
        if not masks:
            print("SAM returned no masks. Using fallback.")
            return self._generate_fallback_mask(original_image_rgb, "pared")

        sorted_masks = sorted(masks, key=lambda x: x.get('stability_score', 0) if 'stability_score' in x else x.get('area', 0), reverse=True)
        
        min_area_ratio_threshold = 0.0002
        min_absolute_pixel_area_threshold = 100
        max_area_ratio_threshold = 0.20

        valid_masks_segments = []
        
        total_masks_to_process = len(sorted_masks)
        # Rango de progreso para esta sección: 75% a 88%
        start_progress = 75.0
        end_progress = 88.0

        if total_masks_to_process > 0:
            for i, mask_data in enumerate(sorted_masks):
                progress_ratio = (i + 1) / total_masks_to_process
                current_progress = start_progress + progress_ratio * (end_progress - start_progress)
                await send_progress(f"Filtrando máscara {i+1}/{total_masks_to_process}", current_progress)
                
                segmentation_mask = mask_data['segmentation'].astype(np.uint8)
                current_mask_area_pixels = np.sum(segmentation_mask)
                current_mask_area_ratio = current_mask_area_pixels / (height * width)

                if current_mask_area_pixels == 0: continue
                if current_mask_area_ratio > max_area_ratio_threshold: continue
                if current_mask_area_ratio < min_area_ratio_threshold and current_mask_area_pixels < min_absolute_pixel_area_threshold: continue
                
                kernel_open = np.ones((3,3),np.uint8)
                opened_segment = cv2.morphologyEx(segmentation_mask, cv2.MORPH_OPEN, kernel_open, iterations=1)
                
                kernel_close = np.ones((5,5),np.uint8)
                cleaned_segment = cv2.morphologyEx(opened_segment, cv2.MORPH_CLOSE, kernel_close, iterations=2)
                
                current_mask_area_pixels = np.sum(cleaned_segment)
                if current_mask_area_pixels == 0: continue
                
                valid_masks_segments.append(cleaned_segment)
        
        if valid_masks_segments:
            for segment in valid_masks_segments:
                combined_mask[segment > 0] = 0
        else:
            print("No SAM masks passed filtering. Using fallback.")
            return self._generate_fallback_mask(original_image_rgb, "pared")
        
        return combined_mask

    async def _process_table_masks(self, masks, height, width, original_image, progress_callback=None, websocket=None):
        """
        Combine masks for table scenario.
        For tables, we first identify the table surface, then objects on it.
        
        Args:
            masks (list): List of mask data dictionaries
            height (int): Image height
            width (int): Image width
            original_image (numpy.ndarray): Original input image
            progress_callback (func, optional): Async function for progress updates.
            websocket (WebSocketServerProtocol, optional): WebSocket connection.
            
        Returns:
            numpy.ndarray: Combined binary mask
        """
        # (La implementación de progreso detallado para la mesa se omite por brevedad,
        # pero seguiría un patrón similar al de _process_wall_masks si fuera necesario)
        
        combined_mask = np.ones((height, width), dtype=np.uint8) * 255
        table_mask = self._identify_table_surface(masks, height, width, original_image)
        masks = sorted(masks, key=lambda x: x['area'], reverse=True)
        objects_mask = np.zeros((height, width), dtype=np.uint8)
        
        for mask_data in masks:
            mask = mask_data['segmentation'].astype(np.uint8)
            area_ratio = mask_data['area'] / (height * width)
            
            if area_ratio < 0.01 or area_ratio > 0.7:
                continue
                
            if table_mask is not None:
                overlap = np.logical_and(mask, table_mask)
                overlap_ratio = np.sum(overlap) / mask_data['area']
                if overlap_ratio < 0.5:
                    continue
            
            cleaned_mask = clean_mask(mask)
            objects_mask = np.logical_or(objects_mask, cleaned_mask).astype(np.uint8)
        
        if np.any(objects_mask):
            combined_mask[objects_mask > 0] = 0
        else:
            hsv = cv2.cvtColor(original_image, cv2.COLOR_RGB2HSV)
            s_channel = hsv[:,:,1]
            v_channel = hsv[:,:,2]
            kernel_size = 5
            s_mean = cv2.blur(s_channel, (kernel_size, kernel_size))
            s_sqmean = cv2.blur(s_channel * s_channel, (kernel_size, kernel_size))
            s_variance = s_sqmean - s_mean * s_mean
            v_mean = cv2.blur(v_channel, (kernel_size, kernel_size))
            v_sqmean = cv2.blur(v_channel * v_channel, (kernel_size, kernel_size))
            v_variance = v_sqmean - v_mean * v_mean
            total_variance = s_variance + v_variance
            threshold = np.mean(total_variance) + np.std(total_variance)
            object_areas = (total_variance > threshold).astype(np.uint8) * 255
            kernel = np.ones((5,5), np.uint8)
            object_areas = cv2.morphologyEx(object_areas, cv2.MORPH_CLOSE, kernel)
            object_areas = cv2.morphologyEx(object_areas, cv2.MORPH_OPEN, kernel)
            combined_mask[object_areas > 0] = 0
        
        return combined_mask

    def _identify_table_surface(self, masks, height, width, original_image):
        """
        Try to identify the table surface in the image.
        
        Args:
            masks (list): List of mask dictionaries
            height (int): Image height
            width (int): Image width
            original_image (numpy.ndarray): Original image
            
        Returns:
            numpy.ndarray or None: Table mask if found, None otherwise
        """
        # Start with the largest mask, which might be the table
        large_masks = [m for m in masks if m['area'] > (height * width * 0.3)]
        
        if not large_masks:
            # If no large mask found, try to detect the table using color
            return self._detect_table_by_color(original_image)
        
        # Sort by area, largest first
        large_masks = sorted(large_masks, key=lambda x: x['area'], reverse=True)
        
        # Check if the largest mask is likely the table
        table_candidate = large_masks[0]['segmentation'].astype(np.uint8)
        
        # Check if the mask is centered in the image
        mask_center_y, mask_center_x = np.where(table_candidate)
        if len(mask_center_y) > 0 and len(mask_center_x) > 0:
            center_y = np.mean(mask_center_y)
            center_x = np.mean(mask_center_x)
            
            # Check if center is within the central region of the image
            y_offset = abs(center_y - height/2) / (height/2)
            x_offset = abs(center_x - width/2) / (width/2)
            
            if y_offset < 0.5 and x_offset < 0.5:
                return table_candidate
        
        return self._detect_table_by_color(original_image)

    def _detect_table_by_color(self, image):
        """
        Detect table by color homogeneity.
        
        Args:
            image (numpy.ndarray): Original image
            
        Returns:
            numpy.ndarray or None: Table mask if found, None otherwise
        """
        # Convert to HSV
        hsv = cv2.cvtColor(image, cv2.COLOR_RGB2HSV)
        
        # Use K-means to find dominant colors
        pixels = hsv.reshape(-1, 3)
        pixels = np.float32(pixels)
        
        # Define criteria and apply kmeans
        criteria = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 10, 1.0)
        k = 3  # Try to identify 3 main color regions
        _, labels, centers = cv2.kmeans(pixels, k, None, criteria, 10, cv2.KMEANS_RANDOM_CENTERS)
        
        # Count pixels in each cluster
        cluster_counts = np.bincount(labels.flatten())
        
        # Find the second largest cluster (often the table)
        sorted_indices = np.argsort(cluster_counts)
        
        if len(sorted_indices) >= 2:
            table_cluster = sorted_indices[-2]  # Second largest cluster
            
            # Create a mask for this cluster
            mask = (labels.flatten() == table_cluster).reshape(image.shape[:2])
            
            # Clean up the mask
            kernel = np.ones((5,5), np.uint8)
            mask = cv2.morphologyEx(mask.astype(np.uint8), cv2.MORPH_CLOSE, kernel)
            
            # Check if this looks like a table (fairly central, fairly large)
            if np.sum(mask) > (image.shape[0] * image.shape[1] * 0.2):
                return mask
        
        return None

    def _color_based_segmentation(self, image):
        """
        Perform color-based segmentation as a fallback method.
        
        Args:
            image (numpy.ndarray): Original image
            
        Returns:
            numpy.ndarray: Binary mask
        """
        # Convert to HSV for better color segmentation
        hsv = cv2.cvtColor(image, cv2.COLOR_RGB2HSV)
        
        # Calculate gradient magnitude
        gray = cv2.cvtColor(image, cv2.COLOR_RGB2GRAY)
        sobelx = cv2.Sobel(gray, cv2.CV_64F, 1, 0, ksize=3)
        sobely = cv2.Sobel(gray, cv2.CV_64F, 0, 1, ksize=3)
        gradient_magnitude = np.sqrt(sobelx**2 + sobely**2)
        
        # Normalize gradient magnitude
        gradient_magnitude = cv2.normalize(gradient_magnitude, None, 0, 255, cv2.NORM_MINMAX).astype(np.uint8)
        
        # Threshold gradient to find edges
        _, edge_mask = cv2.threshold(gradient_magnitude, 30, 255, cv2.THRESH_BINARY)
        
        # Use clustering to separate foreground from background
        pixels = hsv.reshape(-1, 3)
        pixels = np.float32(pixels)
        
        criteria = (cv2.TERM_CRITERIA_EPS + cv2.TERM_CRITERIA_MAX_ITER, 10, 1.0)
        k = 3  # Assuming we have background, foreground, and transitional areas
        _, labels, _ = cv2.kmeans(pixels, k, None, criteria, 10, cv2.KMEANS_RANDOM_CENTERS)
        
        # Count pixels in each cluster
        counts = np.zeros(k)
        for i in range(k):
            counts[i] = np.sum(labels == i)
        
        # The largest cluster is likely the background
        background_cluster = np.argmax(counts)
        
        # Create a binary mask (255 for background, 0 for objects)
        result_mask = np.ones(image.shape[:2], dtype=np.uint8) * 255
        
        # Mark non-background clusters as objects (black)
        flat_mask = result_mask.flatten()
        flat_mask[labels.flatten() != background_cluster] = 0
        result_mask = flat_mask.reshape(image.shape[:2])
        
        # Also consider edges as part of objects
        result_mask[edge_mask > 0] = 0
        
        # Clean up with morphological operations
        kernel = np.ones((3,3), np.uint8)
        result_mask = cv2.morphologyEx(result_mask, cv2.MORPH_CLOSE, kernel)
        result_mask = cv2.morphologyEx(result_mask, cv2.MORPH_OPEN, kernel)
        
        return result_mask

    def _generate_fallback_mask(self, image, scene_type):
        """
        Generate a fallback mask when validation fails or SAM fails.
        
        Args:
            image (numpy.ndarray): Original image
            scene_type (str): Either "wall" or "table"
            
        Returns:
            numpy.ndarray: Binary mask
        """
        h, w = image.shape[:2]
        mask = np.ones((h, w), dtype=np.uint8) * 255
        
        # Convert to grayscale and threshold
        gray = cv2.cvtColor(image, cv2.COLOR_RGB2GRAY)
        
        # Apply different thresholding based on scene type
        if scene_type.lower() == "table":
            # For table, use adaptive thresholding to find objects
            binary = cv2.adaptiveThreshold(gray, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C, 
                                          cv2.THRESH_BINARY_INV, 11, 2)
        else:
            # For wall, use Otsu's method with enhancements
            # Aplicar CLAHE para mejorar el contraste local y manejar mejor las variaciones de iluminación
            clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8,8))
            processed_gray = clahe.apply(gray) 
            
            _, binary = cv2.threshold(processed_gray, 0, 255, cv2.THRESH_BINARY_INV + cv2.THRESH_OTSU)
        
        # Clean up the binary image
        # Usar kernels distintos para apertura y cierre en el fallback si es necesario
        kernel_open_fallback = np.ones((3,3), np.uint8)
        kernel_close_fallback = np.ones((5,5), np.uint8) # Kernel más grande para cerrar bien

        binary = cv2.morphologyEx(binary, cv2.MORPH_OPEN, kernel_open_fallback, iterations=1)
        binary = cv2.morphologyEx(binary, cv2.MORPH_CLOSE, kernel_close_fallback, iterations=2) 

        # Solo para el escenario de pared, si Otsu (invertido) da una máscara mayormente negra, 
        # es probable que haya interpretado una pared vacía/uniforme como "objeto".
        if scene_type.lower() != "table": # Esta lógica es específica para la pared
            fallback_black_ratio_check = np.sum(binary == 0) / (h * w) # Porcentaje de píxeles negros en la máscara binaria (invertida)
            # Si más del 70% es negro (obstáculo) en el fallback de pared, es probable un error.
            if fallback_black_ratio_check > 0.70: 
                print("Fallback (Otsu para pared) produjo una máscara predominantemente negra, devolviendo máscara blanca.")
                return np.ones((h, w), dtype=np.uint8) * 255 # Devolver todo blanco

        # Find contours
        contours, _ = cv2.findContours(binary, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        
        # Filter contours by area - ajustados para mayor sensibilidad a objetos pequeños y restricción a muy grandes
        # Aseguramos un mínimo absoluto de píxeles y un porcentaje del área total.
        min_pixel_area_fallback = 50 # Mínimo de 50 píxeles para ser considerado un objeto.
        min_area_ratio_fallback = 0.0005 # Objetos que ocupen al menos 0.05% de la imagen
        min_area = max(min_pixel_area_fallback, min_area_ratio_fallback * h * w)
        max_area = 0.20 * h * w    # Máximo 20% del área de la imagen para evitar confundir fondo con objeto
        
        for contour in contours:
            area = cv2.contourArea(contour)
            if min_area < area < max_area:
                # Fill in the contour on our mask
                cv2.drawContours(mask, [contour], -1, 0, -1) # -1 para rellenar
        
        return mask