"""
Configuration settings for the object detection application.
"""

# WebSocket settings
WEBSOCKET_HOST = "localhost"
WEBSOCKET_PORT = 8767
FINGER_TRACKING_PORT = 8768  # New port for finger tracking WebSocket

# Camera settings for SAM
CAMERA_INDEX = 1  # Index of the camera to use for SAM
CAMERA_WIDTH = 640
CAMERA_HEIGHT = 480
CAMERA_FPS = 30

# Camera settings for finger tracking
FINGER_CAMERA_INDEX = 0  # Index of the camera to use for finger tracking
FINGER_CAMERA_WIDTH = 640
FINGER_CAMERA_HEIGHT = 480
FINGER_CAMERA_FPS = 30
FINGER_TRANSMISSION_FPS = 10  # How often to send finger count updates

# Frame transmission settings
TRANSMISSION_FPS = 15
JPEG_QUALITY = 80

# SAM model settings
MODEL_TYPE = "vit_t"
MODEL_CHECKPOINT = "./models/mobile_sam.pt"

# Mask generation settings
POINTS_PER_SIDE = 32
PRED_IOU_THRESH = 0.88
STABILITY_SCORE_THRESH = 0.92
CROP_N_LAYERS = 1
CROP_N_POINTS_DOWNSCALE_FACTOR = 2
MIN_MASK_REGION_AREA = 100

# Debug settings
DEBUG_ENABLED = True
DEBUG_INPUT_IMAGE = "debug_input.png"
DEBUG_MASK_FINAL = "debug_mask_final.png"

# Message types for websocket communication
MESSAGE_TYPE_CAMERA_FRAME = 1
MESSAGE_TYPE_MASK = 3
MESSAGE_TYPE_PATH = 4
MESSAGE_TYPE_FINGER_COUNT = 5  # New message type for finger count

# Mask validation settings
MIN_BLACK_RATIO = 0.05
MAX_BLACK_RATIO = 0.85