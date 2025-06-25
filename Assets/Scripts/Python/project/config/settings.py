"""
Configuration settings for the object detection application.
"""

# WebSocket settings
WEBSOCKET_HOST = "localhost"
WEBSOCKET_PORT = 8767
FINGER_TRACKING_PORT = 8768  # New port for finger tracking WebSocket
MENU_GESTURE_PORT = 8766 # Port for the main menu gesture server

# Camera settings for SAM
CAMERA_INDEX = 1  # Index of the camera to use for SAM
# Preferred resolution - the system will try to set this but will adapt to actual camera capabilities
CAMERA_WIDTH_PREFERRED = 640
CAMERA_HEIGHT_PREFERRED = 480
CAMERA_FPS = 30

# Camera settings for finger tracking
FINGER_CAMERA_INDEX = 0  # Index of the camera to use for finger tracking
# Preferred resolution for finger tracking
FINGER_CAMERA_WIDTH_PREFERRED = 640
FINGER_CAMERA_HEIGHT_PREFERRED = 480
FINGER_CAMERA_FPS = 30
FINGER_TRANSMISSION_FPS = 10  # How often to send finger count updates

# Auto-detection settings
AUTO_DETECT_CAMERA_RESOLUTION = True  # Automatically detect and use actual camera resolution
MAX_RESOLUTION_WIDTH = 1280  # Maximum width to prevent performance issues
MAX_RESOLUTION_HEIGHT = 720  # Maximum height to prevent performance issues
MIN_RESOLUTION_WIDTH = 320   # Minimum width for proper detection
MIN_RESOLUTION_HEIGHT = 240  # Minimum height for proper detection

# Legacy compatibility (will be overridden by actual detection)
CAMERA_WIDTH = CAMERA_WIDTH_PREFERRED
CAMERA_HEIGHT = CAMERA_HEIGHT_PREFERRED
FINGER_CAMERA_WIDTH = FINGER_CAMERA_WIDTH_PREFERRED
FINGER_CAMERA_HEIGHT = FINGER_CAMERA_HEIGHT_PREFERRED

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
MESSAGE_TYPE_GRID_POSITION = 6  # New message type for grid position
MESSAGE_TYPE_GRID_CONFIRMATION = 7 # New message for confirmed grid position
MESSAGE_TYPE_SERVER_STATUS = 8 # New message for server status (e.g., camera ready)
MESSAGE_TYPE_SWITCH_CAMERA = 9 # New message to request a camera switch
MESSAGE_TYPE_CAMERA_LIST = 10 # New message to send the list of available cameras
MESSAGE_TYPE_PROGRESS_UPDATE = 11 # For sending progress updates during long tasks
MESSAGE_TYPE_CAMERA_INFO = 12 # For sending camera resolution info

# Mask validation settings
MIN_BLACK_RATIO = 0.05
MAX_BLACK_RATIO = 0.85