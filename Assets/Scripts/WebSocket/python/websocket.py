import asyncio
import websockets
import json
import base64
import cv2
import mediapipe as mp

video_capture = cv2.VideoCapture(0)
video_capture.set(cv2.CAP_PROP_FRAME_WIDTH, 640)
video_capture.set(cv2.CAP_PROP_FRAME_HEIGHT, 480)
video_capture.set(cv2.CAP_PROP_FPS, 30)

mp_hands = mp.solutions.hands
hands = mp_hands.Hands()
mp_drawing = mp.solutions.drawing_utils

async def handle_connection(websocket):
    print("Cliente conectado!")
    while True:
        success, frame = video_capture.read()
        if not success:
            continue

        frame_rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
        result = hands.process(frame_rgb)

        raised_fingers = 0
        if result.multi_hand_landmarks:
            for hand_landmarks in result.multi_hand_landmarks:
                mp_drawing.draw_landmarks(frame, hand_landmarks, mp_hands.HAND_CONNECTIONS)

                hand_positions = [[lm.x, lm.y] for lm in hand_landmarks.landmark]

                if hand_positions[8][1] < hand_positions[6][1]: 
                    raised_fingers += 1
                if hand_positions[12][1] < hand_positions[10][1]:
                    raised_fingers += 1
                if hand_positions[16][1] < hand_positions[14][1]:
                    raised_fingers += 1
                if hand_positions[20][1] < hand_positions[18][1]:
                    raised_fingers += 1

        _, buffer = cv2.imencode('.jpg', frame)
        img_base64 = base64.b64encode(buffer).decode('utf-8')

        message = json.dumps({
            'fingers': raised_fingers,
            'image': img_base64
        })

        await websocket.send(message)
        await asyncio.sleep(0.033)


async def main():
    print("Servidor WebSocket iniciado en ws://localhost:8765")
    async with websockets.serve(handle_connection, "localhost", 8765):
        await asyncio.Future()

asyncio.run(main())


