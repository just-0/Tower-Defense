"""
Enhanced Segment Anything Model (SAM) implementation for object detection.
Optimized for both wall and table scenarios with scene-specific processing.
"""

import torch
import numpy as np
import cv2
from mobile_sam import sam_model_registry, SamPredictor, SamAutomaticMaskGenerator
from utils.image_processings import (
    enhance_image, clean_mask, validate_mask, 
    save_debug_image, mask_to_png_bytes
)
from config.settings import (
    MODEL_TYPE, MODEL_CHECKPOINT, 
    POINTS_PER_SIDE, PRED_IOU_THRESH, STABILITY_SCORE_THRESH,
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
        
        # Set the device based on availability
        self.device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
        print(f"Using device: {self.device}")

        # Load the model
        self.sam = sam_model_registry[MODEL_TYPE](checkpoint=MODEL_CHECKPOINT)
        self.sam.to(device=self.device)
        
        # Initialize the mask generator with configured settings
        self.mask_generator = SamAutomaticMaskGenerator(
            self.sam,
            points_per_side=POINTS_PER_SIDE,
            pred_iou_thresh=PRED_IOU_THRESH,
            stability_score_thresh=STABILITY_SCORE_THRESH,
            crop_n_layers=CROP_N_LAYERS,
            crop_n_points_downscale_factor=CROP_N_POINTS_DOWNSCALE_FACTOR,
            min_mask_region_area=MIN_MASK_REGION_AREA,
        )
        print("Mobile SAM model initialized.")

    def process_image(self, image, scene_type="pared", hand_points=None):
        """
        Process an image to generate object masks with scene-specific optimizations.
        
        Args:
            image (numpy.ndarray): Input image in RGB format
            scene_type (str): Either "wall" or "table" to specify the scenario
            hand_points (list, optional): List of [x, y] coordinates for guided segmentation
            
        Returns:
            bytes: PNG encoded binary mask or None if processing failed
        """
        # Save debug input image
        save_debug_image(image, DEBUG_INPUT_IMAGE)
        
        # Scene-specific preprocessing
        if scene_type.lower() == "table":
            preprocessed_image = self._preprocess_table_scene(image)
        else:  # Default to wall processing
            preprocessed_image = self._preprocess_wall_scene(image)
        
        h, w = image.shape[:2]
        
        # Process with SAM
        if hand_points is not None and len(hand_points) > 0:
            masks = self._process_with_points(preprocessed_image, hand_points)
        else:
            masks = self._process_automatic(preprocessed_image)
        
        if not masks:
            print(f"No masks found in {scene_type} scene!")
            return None

        # Scene-specific mask processing
        if scene_type.lower() == "table":
            combined_mask = self._process_table_masks(masks, h, w, image)
        else:
            combined_mask = self._process_wall_masks(masks, h, w, image)
        
        # Validate the final mask
        is_valid, black_ratio = validate_mask(combined_mask, MIN_BLACK_RATIO, MAX_BLACK_RATIO)
        if not is_valid:
            print(f"Warning: Mask may be incorrect ({(black_ratio*100):.1f}% black)")
            # Instead of resetting to all white, we can use a fallback strategy
            combined_mask = self._generate_fallback_mask(image, scene_type)
        
        # Save debug output mask
        save_debug_image(combined_mask, DEBUG_MASK_FINAL)
        
        # Convert to PNG bytes
        return mask_to_png_bytes(combined_mask)

    def _preprocess_wall_scene(self, image):
        """
        Preprocess image for wall scenario.
        For walls, we enhance contrast to make objects stand out.
        
        Args:
            image (numpy.ndarray): Original image
            
        Returns:
            numpy.ndarray: Preprocessed image
        """
        # Apply basic enhancement
        enhanced = enhance_image(image)
        
        # Additional processing for wall scenes
        # Convert to HSV to better handle lighting variations
        hsv = cv2.cvtColor(enhanced, cv2.COLOR_RGB2HSV)
        
        # Apply adaptive histogram equalization to the V channel
        clahe = cv2.createCLAHE(clipLimit=2.0, tileGridSize=(8, 8))
        hsv[:,:,2] = clahe.apply(hsv[:,:,2])
        
        # Convert back to RGB
        enhanced = cv2.cvtColor(hsv, cv2.COLOR_HSV2RGB)
        
        return enhanced

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

    def _process_wall_masks(self, masks, height, width, original_image):
        """
        Combine masks for wall scenario.
        For walls, we prioritize smaller, distinct objects.
        
        Args:
            masks (list): List of mask data dictionaries
            height (int): Image height
            width (int): Image width
            original_image (numpy.ndarray): Original input image
            
        Returns:
            numpy.ndarray: Combined binary mask
        """
        # Start with a white (255) background
        combined_mask = np.ones((height, width), dtype=np.uint8) * 255
        
        # For wall scenes, we prioritize distinct objects
        # Sort masks by stability score (if available) or area
        if 'stability_score' in masks[0]:
            masks = sorted(masks, key=lambda x: x.get('stability_score', 0), reverse=True)
        else:
            masks = sorted(masks, key=lambda x: x['area'])  # Smaller objects first for wall scenes
        
        # Filter masks by their properties
        valid_masks = []
        for mask_data in masks:
            mask = mask_data['segmentation'].astype(np.uint8)
            mask_area = np.sum(mask)
            area_ratio = mask_area / (height * width)
            
            # Exclude very large masks (likely the wall)
            if area_ratio > 0.6:
                continue
                
            # Exclude very small masks (likely noise)
            if area_ratio < 0.01:
                continue
                
            # Check mask shape - prefer more compact shapes for objects on walls
            contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
            if contours:
                main_contour = max(contours, key=cv2.contourArea)
                perimeter = cv2.arcLength(main_contour, True)
                if perimeter > 0:
                    circularity = 4 * np.pi * mask_area / (perimeter * perimeter)
                    # More circular objects tend to be more valid (not the wall)
                    if circularity > 0.2:
                        valid_masks.append(mask_data)
        
        # If we have valid masks, use them
        if valid_masks:
            # For wall scenario, take up to 5 distinct objects
            for mask_data in valid_masks[:5]:
                mask = mask_data['segmentation'].astype(np.uint8)
                cleaned_mask = clean_mask(mask)
                combined_mask[cleaned_mask > 0] = 0
        else:
            # Fallback: use color-based segmentation to find objects against the wall
            combined_mask = self._color_based_segmentation(original_image)
        
        return combined_mask

    def _process_table_masks(self, masks, height, width, original_image):
        """
        Combine masks for table scenario.
        For tables, we first identify the table surface, then objects on it.
        
        Args:
            masks (list): List of mask data dictionaries
            height (int): Image height
            width (int): Image width
            original_image (numpy.ndarray): Original input image
            
        Returns:
            numpy.ndarray: Combined binary mask
        """
        # Start with a white (255) background
        combined_mask = np.ones((height, width), dtype=np.uint8) * 255
        
        # First, try to identify the table surface
        table_mask = self._identify_table_surface(masks, height, width, original_image)
        
        # Sort masks by area (larger first)
        masks = sorted(masks, key=lambda x: x['area'], reverse=True)
        
        # Set of objects we've found
        objects_mask = np.zeros((height, width), dtype=np.uint8)
        
        for mask_data in masks:
            mask = mask_data['segmentation'].astype(np.uint8)
            area_ratio = mask_data['area'] / (height * width)
            
            # Skip very small or very large objects
            if area_ratio < 0.01 or area_ratio > 0.7:
                continue
                
            # Check if this object is mostly within the table boundary
            if table_mask is not None:
                overlap = np.logical_and(mask, table_mask)
                overlap_ratio = np.sum(overlap) / mask_data['area']
                
                # If less than 50% of the object is on the table, skip it
                if overlap_ratio < 0.5:
                    continue
            
            # Clean and add object to our collection
            cleaned_mask = clean_mask(mask)
            objects_mask = np.logical_or(objects_mask, cleaned_mask).astype(np.uint8)
        
        # If we found objects, add them to the combined mask
        if np.any(objects_mask):
            combined_mask[objects_mask > 0] = 0
        else:
            # Fallback: use alternative segmentation method
            hsv = cv2.cvtColor(original_image, cv2.COLOR_RGB2HSV)
            
            # Assuming table is relatively uniform, look for non-uniform regions
            s_channel = hsv[:,:,1]
            v_channel = hsv[:,:,2]
            
            # Calculate local variance in saturation and value
            kernel_size = 5
            s_mean = cv2.blur(s_channel, (kernel_size, kernel_size))
            s_sqmean = cv2.blur(s_channel * s_channel, (kernel_size, kernel_size))
            s_variance = s_sqmean - s_mean * s_mean
            
            v_mean = cv2.blur(v_channel, (kernel_size, kernel_size))
            v_sqmean = cv2.blur(v_channel * v_channel, (kernel_size, kernel_size))
            v_variance = v_sqmean - v_mean * v_mean
            
            # Combine variances
            total_variance = s_variance + v_variance
            
            # Threshold to find areas of high variance (likely objects)
            threshold = np.mean(total_variance) + np.std(total_variance)
            object_areas = (total_variance > threshold).astype(np.uint8) * 255
            
            # Clean up with morphological operations
            kernel = np.ones((5,5), np.uint8)
            object_areas = cv2.morphologyEx(object_areas, cv2.MORPH_CLOSE, kernel)
            object_areas = cv2.morphologyEx(object_areas, cv2.MORPH_OPEN, kernel)
            
            # Invert to get our final mask (white background, black objects)
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
        Generate a fallback mask when validation fails.
        
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
            # For wall, use Otsu's method
            _, binary = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY_INV + cv2.THRESH_OTSU)
        
        # Clean up the binary image
        kernel = np.ones((3,3), np.uint8)
        binary = cv2.morphologyEx(binary, cv2.MORPH_OPEN, kernel)
        binary = cv2.morphologyEx(binary, cv2.MORPH_CLOSE, kernel)
        
        # Find contours
        contours, _ = cv2.findContours(binary, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        
        # Filter contours by area
        min_area = 0.005 * h * w  # Minimum size for an object
        max_area = 0.5 * h * w    # Maximum size for an object
        
        for contour in contours:
            area = cv2.contourArea(contour)
            if min_area < area < max_area:
                # Fill in the contour on our mask
                cv2.drawContours(mask, [contour], -1, 0, -1)
        
        return mask