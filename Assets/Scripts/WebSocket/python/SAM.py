import asyncio
import websockets
import cv2
import numpy as np
import io
from PIL import Image
import torch
import threading
import time
import base64
import os
from mobile_sam import sam_model_registry, SamPredictor
import torch

# from mobile_sam import sam_model_registry, SamPredictor, SamAutomaticMaskGenerator # no necesario
#from fastsam import FastSAM, FastSAMPrompt # no necesario 
# from segment_anything import sam_model_registry, SamPredictor  # no necesario principal
class CameraManager:
    def __init__(self):
        self.camera = None
        self.is_running = False
        self.current_frame = None
        self.lock = threading.Lock()
        
    def start_camera(self):
        if self.camera is None:
            self.camera = cv2.VideoCapture(1)  
            if not self.camera.isOpened():
                print("Error: Could not open camera")
                return False
                

            self.camera.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
            self.camera.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
            
            self.is_running = True
            threading.Thread(target=self._camera_thread, daemon=True).start()
            print("Camera started")
            return True
        return False
    
    def _camera_thread(self):
        while self.is_running:
            ret, frame = self.camera.read()
            if ret:
                frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
                with self.lock:
                    self.current_frame = frame_rgb
            time.sleep(1/30)  
    
    def get_current_frame(self):
        with self.lock:
            if self.current_frame is not None:
                return self.current_frame.copy()
            return None
    
    def stop_camera(self):
        self.is_running = False
        if self.camera is not None:
            self.camera.release()
            self.camera = None
            print("Camera stopped")

class SAMProcessor:
    def __init__(self):
        print("Initializing Mobile SAM model...")
        self.device = torch.device('cuda' if torch.cuda.is_available() else 'cpu')
        print(f"Using device: {self.device}")

        model_type = "vit_t"  # El tipo para MobileSAM es vit_t
        #model_type = "vit_b"  # El tipo para MobileSAM es vit_t
        # checkpoint = "./models/mobile_sam.pt"  # Ruta al checkpoint de MobileSAM
        checkpoint = os.path.expanduser(r"~\Downloads\mobile_sam.pt")  # Ruta al checkpoint de MobileSAM
        #checkpoint = "./models/sam_vit_b_01ec64.pth"  # Ruta al checkpoint de MobileSAM

        self.sam = sam_model_registry[model_type](checkpoint=checkpoint)
        self.sam.to(device=self.device)
        self.predictor = SamPredictor(self.sam)
        print("Mobile SAM model initialized.")

    def process_image(self, image):
        """Process an image with SAM and return the segmentation visualization"""

        self.predictor.set_image(image)
        

        h, w = image.shape[:2]
        input_point = np.array([[w//2, h//2]])  # Center point
        input_label = np.array([1])  # 1 for foreground
        

        masks, scores, logits = self.predictor.predict(
            point_coords=input_point,
            point_labels=input_label,
            multimask_output=True,
        )
        

        best_mask_idx = np.argmax(scores)
        mask = masks[best_mask_idx]
        

        overlay = np.zeros((h, w, 4), dtype=np.uint8)
        

        contours, _ = cv2.findContours(
            mask.astype(np.uint8), 
            cv2.RETR_EXTERNAL, 
            cv2.CHAIN_APPROX_SIMPLE
        )
        

        cv2.drawContours(overlay, contours, -1, (0, 255, 0, 255), 2)
        

        for point in input_point:
            cv2.circle(overlay, (int(point[0]), int(point[1])), 5, (255, 0, 0, 255), -1)
        

        overlay_pil = Image.fromarray(overlay)
        buffer = io.BytesIO()
        overlay_pil.save(buffer, format="PNG") 
        
        return buffer.getvalue()

def detectar_aruco_zona_segura(image):
    """Detecta un marcador ArUco y devuelve el centro de su posición"""
    aruco_dict = cv2.aruco.getPredefinedDictionary(cv2.aruco.DICT_4X4_50)
    parameters = cv2.aruco.DetectorParameters()
    detector = cv2.aruco.ArucoDetector(aruco_dict, parameters)

    corners, ids, _ = detector.detectMarkers(image)

    if ids is not None:
        # Toma el primer marcador detectado (puedes filtrar por ID si quieres uno específico)
        c = corners[0][0]
        cx = int(np.mean(c[:, 0]))
        cy = int(np.mean(c[:, 1]))
        return (cx, cy)
    
    return None


async def handle_client(websocket):
    camera_manager = CameraManager()
    sam_processor = SAMProcessor()
    send_frames = False
    
    print("New client connected")
    
    try:
        async for message in websocket:
            if isinstance(message, str):
                print(f"Received text command: {message}")
                
                if message == "START_CAMERA":
                    camera_manager.start_camera()
                    send_frames = True
                    

                    asyncio.create_task(send_camera_frames(websocket, camera_manager))
                    
                elif message == "STOP_CAMERA":
                    send_frames = False
                    camera_manager.stop_camera()

                elif message == "GET_SAFE_ZONE":
                    frame = camera_manager.get_current_frame()
                    if frame is not None:
                        zona = detectar_aruco_zona_segura(frame)
                        if zona is not None:
                            mensaje = f"SAFE_ZONE:{zona[0]},{zona[1]}"
                            await websocket.send(mensaje.encode("utf-8"))
                            print("Sent SAFE_ZONE coordinates")
                        else:
                            await websocket.send("SAFE_ZONE:NOT_FOUND".encode("utf-8"))
                            print("No ArUco marker detected")
                    
                elif message == "PROCESS_SAM":

                    frame = camera_manager.get_current_frame()
                    if frame is not None:

                        overlay_bytes = sam_processor.process_image(frame)
                        

                        await websocket.send(bytes([2]) + overlay_bytes)
                        print("Sent segmentation overlay")
    except websockets.exceptions.ConnectionClosed:
        print("Client disconnected")
    finally:
        camera_manager.stop_camera()

async def send_camera_frames(websocket, camera_manager):
    try:
        while camera_manager.is_running:
            frame = camera_manager.get_current_frame()
            if frame is not None:
             
                success, encoded_frame = cv2.imencode('.jpg', cv2.cvtColor(frame, cv2.COLOR_RGB2BGR), [cv2.IMWRITE_JPEG_QUALITY, 80])
                if success:
                    # Send camera frame (type 1)
                    await websocket.send(bytes([1]) + encoded_frame.tobytes())
            
            # Control frame rate (adjust as needed)
            await asyncio.sleep(1/15)  # ~15 FPS to reduce bandwidth
    except (websockets.exceptions.ConnectionClosed, asyncio.CancelledError):
        print("Camera frame sending stopped")

async def main():
    server = await websockets.serve(handle_client, "localhost", 8767)
    print("WebSocket server started at ws://localhost:8767")
    await server.wait_closed()

if __name__ == "__main__":
    asyncio.run(main())
