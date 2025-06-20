import asyncio
import websockets
import signal
import sys
import platform
from services.multiplayer import game_server, gesture_server
from config.settings import WEBSOCKET_HOST, MENU_GESTURE_PORT

CONTROL_PORT = 8765
current_server_tasks = []
menu_server_instance = None # Guardamos la instancia del servidor de menú

async def stop_current_servers():
    """Detiene y limpia las tareas de servidor activas."""
    global current_server_tasks
    if not current_server_tasks:
        return

    print(f"Deteniendo {len(current_server_tasks)} servidor(es) de juego activo(s)...")
    for task in current_server_tasks:
        task.cancel()
    
    await asyncio.gather(*current_server_tasks, return_exceptions=True)
    current_server_tasks = []
    print("Servidores de juego anteriores detenidos.")

async def start_menu_server():
    """Inicia el servidor de gestos para el menú principal."""
    global current_server_tasks, menu_server_instance
    await stop_current_servers()

    # Usamos gesture_server pero en el puerto del MENÚ
    menu_server_instance = gesture_server.create_server(port=MENU_GESTURE_PORT)
    
    print(f"Iniciando servidor de gestos del menú en el puerto {MENU_GESTURE_PORT}...")
    task_menu = asyncio.create_task(gesture_server.start_server(menu_server_instance))
    current_server_tasks.append(task_menu)

async def handle_unity_commands(websocket):
    """Maneja los comandos entrantes de Unity para controlar los servidores."""
    global current_server_tasks, menu_server_instance
    print(f"Cliente de Unity conectado desde {websocket.remote_address}")
    
    try:
        async for message in websocket:
            print(f"Comando recibido de Unity: '{message}'")
            # Detenemos CUALQUIER servidor que esté corriendo antes de empezar uno nuevo
            await stop_current_servers()

            if message == "start_menu":
                await start_menu_server()

            elif message == "start_singleplayer":
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
    """Función principal para iniciar el servidor de control y el de menú."""
    # En Windows, add_signal_handler no está implementado para el event loop por defecto.
    # Usaremos un enfoque diferente para la detención.
    stop_event = asyncio.Event()

    # Función para manejar la señal de apagado de forma segura entre plataformas.
    def shutdown_handler():
        if not stop_event.is_set():
            print("Iniciando apagado ordenado...")
            stop_event.set()

    if platform.system() != "Windows":
        loop = asyncio.get_running_loop()
        for sig in (signal.SIGINT, signal.SIGTERM):
            loop.add_signal_handler(sig, shutdown_handler)
    else:
        # En Windows, el manejador de señales se simula de otra forma,
        # usualmente con la captura de KeyboardInterrupt, que ya está al final del script.
        pass

    # Iniciar el servidor de menú por defecto
    await start_menu_server()

    try:
        async with websockets.serve(handle_unity_commands, "localhost", CONTROL_PORT):
            print(f"Servidor de control iniciado en ws://localhost:{CONTROL_PORT}")
            print("Backend listo y esperando órdenes... (Presiona Ctrl+C para salir)")
            await stop_event.wait()

    except asyncio.CancelledError:
        # Esto es esperado durante el apagado normal.
        pass
    finally:
        print("Cerrando todos los servidores.")
        await stop_current_servers()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("Backend cerrado por el usuario (Ctrl+C).") 