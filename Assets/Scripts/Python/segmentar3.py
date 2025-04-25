import asyncio
import websockets
import cv2
import numpy as np
import io
from PIL import Image
import torch
import threading
import time

from mobile_sam import sam_model_registry, SamPredictor, SamAutomaticMaskGenerator
from hand_detector import HandDetector

class CameraManager:
    def __init__(self):
        self.camera = None
        self.is_running = False
        self.current_frame = None
        self.current_display_frame = None  # Frame con landmarks para mostrar
        self.lock = threading.Lock()
        self.hand_detector = HandDetector()
        self.tap_callback = None
        
    def set_tap_callback(self, callback):
        """Establecer callback para cuando se detecte un tap"""
        self.tap_callback = callback
        self.hand_detector.set_tap_callback(self._on_tap_detected)
        
    def _on_tap_detected(self):
        """Callback interno cuando el detector de manos detecta un tap"""
        if self.tap_callback:
            self.tap_callback()
        
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
                # Procesar el frame con el detector de manos
                display_frame, _ = self.hand_detector.process_frame(frame)
                
                with self.lock:
                    self.current_frame = frame.copy()
                    self.current_display_frame = display_frame
            
            time.sleep(1/30)  # ~30 FPS
    
    def get_current_frame(self):
        with self.lock:
            if self.current_frame is not None:
                return self.current_frame.copy()
            return None
    
    def get_display_frame(self):
        """Obtener el frame con visualización de landmarks"""
        with self.lock:
            if self.current_display_frame is not None:
                return self.current_display_frame.copy()
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
        checkpoint = "./models/mobile_sam.pt"  # Ruta al checkpoint de MobileSAM

        self.sam = sam_model_registry[model_type](checkpoint=checkpoint)
        self.sam.to(device=self.device)
        self.mask_generator = SamAutomaticMaskGenerator(self.sam)
        print("Mobile SAM model initialized.")

    def process_image(self, image):
        h, w = image.shape[:2]
        masks = self.mask_generator.generate(image)

        if not masks:
            print("No masks found!")
            return None

        # Creamos una máscara blanca (fondo) y pintamos los objetos en negro
        combined_mask = np.ones((h, w), dtype=np.uint8) * 255  # Fondo blanco

        for mask_data in masks:
            # Obtener la máscara binaria
            mask = mask_data['segmentation'].astype(np.uint8)
            # Aplicar la máscara invirtiendo los colores (donde hay objeto, ponemos negro)
            combined_mask[mask > 0] = 0  # Objetos en negro

        # Guarda la máscara para debug
        cv2.imwrite("debug_mask.png", combined_mask)

        # Convertimos a PNG en memoria
        mask_pil = Image.fromarray(combined_mask)
        buffer_mask = io.BytesIO()
        mask_pil.save(buffer_mask, format="PNG")
        mask_bytes = buffer_mask.getvalue()

        return mask_bytes

class WebSocketServer:
    def __init__(self, host="localhost", port=8767):
        self.host = host
        self.port = port
        self.clients = set()
        self.camera_manager = CameraManager()
        self.sam_processor = None  # Se inicializará cuando sea necesario
        
    async def handle_client(self, websocket):
        """Manejar conexión de un cliente websocket"""
        print(f"Cliente conectado: {websocket.remote_address}")
        self.clients.add(websocket)
        
        # Establecer callback para tap
        self.camera_manager.set_tap_callback(
            lambda: asyncio.create_task(self.notify_tap(websocket))
        )
        
        try:
            async for message in websocket:
                if isinstance(message, str):
                    print(f"Mensaje recibido: {message}")
                    
                    if message == "START_CAMERA":
                        self.camera_manager.start_camera()
                        asyncio.create_task(self.send_camera_frames(websocket))
                        print("Streaming de cámara iniciado")
                        
                    elif message == "STOP_CAMERA":
                        self.camera_manager.stop_camera()
                        
                    elif message == "PROCESS_SAM":
                        frame = self.camera_manager.get_current_frame()
                        if frame is not None:
                            # Inicializar SAM si es necesario
                            if self.sam_processor is None:
                                self.sam_processor = SAMProcessor()
                                
                            # Procesar con SAM
                            mask_bytes = self.sam_processor.process_image(frame)
                            if mask_bytes:
                                await websocket.send(bytes([3]) + mask_bytes)  # Tipo 3: máscara
                                print("Máscara SAM enviada")
                        else:
                            print("No hay frame disponible para procesar")
                else:
                    # Datos binarios - interpretarlo según tu protocolo
                    print(f"Datos binarios recibidos ({len(message)} bytes)")
                    
        except websockets.exceptions.ConnectionClosed:
            print(f"Cliente desconectado: {websocket.remote_address}")
        finally:
            self.clients.remove(websocket)
    
    async def notify_tap(self, websocket):
        """Notificar al cliente que se detectó un tap"""
        try:
            # Enviar mensaje de tap detectado (puedes adaptar este formato)
            await websocket.send("TAP_DETECTED")
            print("Tap notificado al cliente")
        except websockets.exceptions.ConnectionClosed:
            pass
    
    async def send_camera_frames(self, websocket):
        """Enviar frames de cámara al cliente"""
        try:
            while websocket in self.clients and self.camera_manager.is_running:
                # Usamos el frame con landmarks visualizados
                frame = self.camera_manager.get_display_frame()
                if frame is not None:
                    # Convertir a JPEG
                    success, encoded_frame = cv2.imencode('.jpg', frame, [cv2.IMWRITE_JPEG_QUALITY, 80])
                    if success:
                        # Enviar con prefijo 1 para indicar que es un frame de cámara
                        await websocket.send(bytes([1]) + encoded_frame.tobytes())
                
                # Control de velocidad de frames
                await asyncio.sleep(1/15)  # ~15 FPS para reducir ancho de banda
        except (websockets.exceptions.ConnectionClosed, asyncio.CancelledError):
            print("Envío de frames interrumpido")
    
    async def start(self):
        """Iniciar el servidor websocket"""
        server = await websockets.serve(self.handle_client, self.host, self.port)
        print(f"Servidor websocket iniciado en ws://{self.host}:{self.port}")
        await server.wait_closed()

if __name__ == "__main__":
    server = WebSocketServer()
    try:
        asyncio.run(server.start())
    except KeyboardInterrupt:
        print("Deteniendo servidor..")
