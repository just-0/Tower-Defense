import asyncio
import websockets
import signal
import sys
from services.multiplayer import game_server, gesture_server

CONTROL_PORT = 8765  # Puerto para la comunicación de control con Unity
current_server_tasks = []

async def stop_current_servers():
    """Detiene y limpia las tareas de servidor activas."""
    global current_server_tasks
    if not current_server_tasks:
        return

    print(f"Deteniendo {len(current_server_tasks)} servidor(es) activo(s)...")
    for task in current_server_tasks:
        task.cancel()
    
    await asyncio.gather(*current_server_tasks, return_exceptions=True)
    current_server_tasks = []
    print("Servidores anteriores detenidos.")

async def handle_unity_commands(websocket):
    """Maneja los comandos entrantes de Unity para controlar los servidores."""
    global current_server_tasks
    print(f"Cliente de Unity conectado desde {websocket.remote_address}")
    
    try:
        async for message in websocket:
            print(f"Comando recibido de Unity: '{message}'")
            await stop_current_servers()

            if message == "start_singleplayer":
                print("Iniciando backend en modo Single-Player...")
                placer_instance = game_server.create_server()
                selector_instance = gesture_server.create_server()
                
                task_placer = asyncio.create_task(game_server.start_server(placer_instance))
                task_selector = asyncio.create_task(gesture_server.start_server(selector_instance))
                current_server_tasks.extend([task_placer, task_selector])

            elif message == "start_multiplayer_placer":
                print("Iniciando backend en modo Multiplayer (Rol: Placer)...")
                placer_instance = game_server.create_server()
                task_placer = asyncio.create_task(game_server.start_server(placer_instance))
                current_server_tasks.append(task_placer)

            elif message == "start_multiplayer_selector":
                print("Iniciando backend en modo Multiplayer (Rol: Selector)...")
                selector_instance = gesture_server.create_server()
                task_selector = asyncio.create_task(gesture_server.start_server(selector_instance))
                current_server_tasks.append(task_selector)
            
            elif message == "stop":
                 print("Comando de detención recibido. Los servidores han sido parados.")

            else:
                print(f"Comando desconocido: '{message}'")
    
    except websockets.exceptions.ConnectionClosed:
        print("Cliente de Unity desconectado.")
    finally:
        await stop_current_servers()

async def main():
    """Función principal para iniciar el servidor de control."""
    loop = asyncio.get_running_loop()
    stop = loop.create_future()
    loop.add_signal_handler(signal.SIGINT, stop.set_result, None)
    loop.add_signal_handler(signal.SIGTERM, stop.set_result, None)

    async with websockets.serve(handle_unity_commands, "localhost", CONTROL_PORT):
        print(f"Servidor de control iniciado en ws://localhost:{CONTROL_PORT}")
        print("Esperando conexión de Unity... (Presiona Ctrl+C para salir)")
        await stop

    print("Cerrando el servidor de control.")


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("Backend cerrado por el usuario.") 