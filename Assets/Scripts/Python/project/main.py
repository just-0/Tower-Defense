"""
Main entry point for the application.
"""

import asyncio
import signal
import sys
from services.websocket_server import WebSocketServer

# Global server instance for cleanup
server = None

def signal_handler(sig, frame):
    """Handle termination signals to clean up resources."""
    print("Shutting down gracefully...")
    if server:
        server.cleanup()
    sys.exit(0)

async def main():
    """Run the main application."""
    global server
    
    # Setup signal handlers for graceful shutdown
    signal.signal(signal.SIGINT, signal_handler)
    signal.signal(signal.SIGTERM, signal_handler)
    
    # Create and start the WebSocket server
    server = WebSocketServer()
    
    try:
        await server.start()
    except KeyboardInterrupt:
        print("Application terminated by user")
    finally:
        if server:
            server.cleanup()

if __name__ == "__main__":
    asyncio.run(main())