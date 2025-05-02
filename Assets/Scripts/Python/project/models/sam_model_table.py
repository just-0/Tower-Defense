"""
Segment Anything Model (SAM) implementation for object detection.
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

    def process_image(self, image, hand_points=None):
        """
        Process an image to generate object masks.
        
        Args:
            image (numpy.ndarray): Input image in RGB format
            hand_points (list, optional): List of [x, y] coordinates for guided segmentation
            
        Returns:
            bytes: PNG encoded binary mask or None if processing failed
        """
        # Save debug input image
        save_debug_image(image, DEBUG_INPUT_IMAGE)
        
        # Enhance image quality
        enhanced_image = enhance_image(image)
        h, w = image.shape[:2]
        
        # Process with SAM
        if hand_points is not None and len(hand_points) > 0:
            masks = self._process_with_points(enhanced_image, hand_points)
        else:
            masks = self._process_automatic(enhanced_image)
        
        if not masks:
            print("No masks found!")
            return None

        # Create combined mask
        combined_mask = self._combine_masks(masks, h, w)
        
        # Validate the final mask
        is_valid, black_ratio = validate_mask(combined_mask, MIN_BLACK_RATIO, MAX_BLACK_RATIO)
        if not is_valid:
            print(f"Warning: Mask may be incorrect ({(black_ratio*100):.1f}% black)")
            combined_mask.fill(255)  # Reset to white background
        
        # Save debug output mask
        save_debug_image(combined_mask, DEBUG_MASK_FINAL)
        
        # Convert to PNG bytes
        return mask_to_png_bytes(combined_mask)

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

    def _combine_masks(self, masks, height, width):
        """
        Combine multiple masks into a single binary mask.
        
        Args:
            masks (list): List of mask data dictionaries
            height (int): Image height
            width (int): Image width
            
        Returns:
            numpy.ndarray: Combined binary mask
        """
        # Start with a white (255) background
        combined_mask = np.ones((height, width), dtype=np.uint8) * 255
        
        # Sort masks by area (largest first)
        masks = sorted(masks, key=(lambda x: x['area']), reverse=True)
        
        # Take only the largest masks (limiting to 3)
        for mask_data in masks[:3]:
            mask = mask_data['segmentation'].astype(np.uint8)
            
            # Clean up the mask
            cleaned_mask = clean_mask(mask)
            
            # Apply only if the area is significant (more than 5% of the image)
            if mask_data['area'] > (height * width * 0.05):
                combined_mask[cleaned_mask > 0] = 0
        
        return combined_mask