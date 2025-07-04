# Contexto del Proyecto: Tower Defense por Gestos

## 1. Concepto General  

El proyecto es un juego Tower Defense para un curso de Interacción Humano-Computador, con el requisito de ser controlado exclusivamente por gestos, sin usar teclado ni mouse. El escenario de juego es una superficie física real (como una pared), donde el jugador puede colocar obstáculos. El sistema utiliza dos cámaras para capturar los gestos del jugador y el entorno.

## 2. Arquitectura Técnica  

El sistema se divide en dos componentes principales que se comunican en tiempo real:

### Cliente (Unity - C#)
Renderiza el juego, gestiona la lógica (oleadas, torretas, recursos del jugador) y visualiza la información de los servidores. Incluye:
- **MenuGestureController.cs**: Controla la navegación del menú principal por gestos
- **GestureReceiver.cs**: Maneja la comunicación WebSocket con los servidores Python
- **UniversalPanelManager.cs**: Gestiona las diferentes pantallas del juego
- **SimpleGestureManager.cs**: Procesa los gestos de navegación en menús

### Servidor (Python)  
Procesa la visión por computador usando un **sistema de control centralizado**:

#### run_backend.py - Servidor de Control (Puerto 8765)
- Actúa como coordinador central que recibe comandos de Unity
- Inicia/detiene servidores según el modo de juego seleccionado:
  - `start_menu`: Servidor de gestos para navegación del menú
  - `start_singleplayer`: Ambos servidores para un jugador
  - `start_multiplayer_placer`: Solo servidor principal para rol Placer
  - `start_multiplayer_selector`: Solo servidor de gestos para rol Selector

#### game_server.py - Servidor Principal (Puerto 8767)
Utiliza la cámara principal que apunta al escenario (CAMERA_INDEX). Maneja:
- **Fase de Planificación**: Procesamiento SAM, detección ArUco, pathfinding A*
- **Fase de Combate**: Sistema de rejilla para posicionamiento de torretas
- Envía: máscaras de segmentación, rutas calculadas, posiciones de rejilla

#### gesture_server.py - Servidor de Gestos (Puertos 8768/8770)
Utiliza una cámara dedicada para rastrear manos con MediaPipe:
- Puerto 8768: Gestos de combate (selección de torretas)
- Puerto 8770: Gestos de menú principal (navegación)
- Envía: stream de video de manos, conteo de dedos, lista de cámaras disponibles

## 3. Modos de Juego

### Single Player
Un jugador controla ambas fases usando ambas cámaras:
- Cámara principal: Escenario y posicionamiento
- Cámara de gestos: Selección de torretas y navegación

### Multiplayer
Dos jugadores con roles especializados:

#### Rol Placer (game_server.py)
- Controla la **Fase de Planificación**: Escanea el escenario, genera rutas
- Controla el **posicionamiento** en Fase de Combate usando la cámara principal
- Ve el stream del Selector para coordinación

#### Rol Selector (gesture_server.py)  
- Controla la **selección de torretas** en Fase de Combate usando gestos de mano
- Ve el stream del Placer para coordinación
- Navega por menús usando gestos

## 4. Flujo de Juego y Fases

### Fase de Planificación (GamePhase.Planning)
- **Objetivo**: Escanear el escenario para generar el camino de los enemigos
- **Interacción**: El jugador/Placer muestra 3 dedos y los mantiene
- **Proceso**: 
  1. Unity envía comando `PROCESS_SAM` al servidor principal
  2. Servidor captura frame del escenario
  3. Usa SAM (Segment Anything Model) para detectar obstáculos
  4. Detecta marcador ArUco como punto final del camino
  5. Calcula ruta usando algoritmo A*
  6. Envía máscara (MESSAGE_TYPE_MASK) y puntos del camino (MESSAGE_TYPE_PATH)
- **Transición**: 5 dedos para cambiar a Fase de Combate

### Fase de Combate (GamePhase.Combat)
- **Objetivo**: Seleccionar y colocar torretas para defender de oleadas

#### En Single Player:
1. **Selección**: Mano izquierda selecciona tipo de torreta (1-3 dedos)
2. **Posicionamiento**: Mano derecha actúa como cursor en escenario
3. **Confirmación**: Mantener dedo quieto para colocar torreta

#### En Multiplayer:
- **Selector**: Usa gestos de mano para seleccionar tipo de torreta
- **Placer**: Usa cámara principal para posicionar torretas en la rejilla
- **Coordinación**: Ambos ven los streams del otro jugador

- **Transición**: 4 dedos para volver a Planificación

## 5. Comunicación WebSocket

### Tipos de Mensajes
- `MESSAGE_TYPE_CAMERA_FRAME`: Stream de video en tiempo real
- `MESSAGE_TYPE_FINGER_COUNT`: Conteo de dedos detectados  
- `MESSAGE_TYPE_MASK`: Máscara de segmentación SAM
- `MESSAGE_TYPE_PATH`: Ruta calculada con A*
- `MESSAGE_TYPE_GRID_POSITION`: Posición del cursor en rejilla
- `MESSAGE_TYPE_GRID_CONFIRMATION`: Confirmación de colocación
- `MESSAGE_TYPE_CAMERA_INFO`: Información de resolución de cámara
- `MESSAGE_TYPE_CAMERA_LIST`: Lista de cámaras disponibles
- `MESSAGE_TYPE_SWITCH_CAMERA`: Cambio de cámara en tiempo real

### Gestión de Cámaras
- **utils/camera.py**: CameraManager mejorado con detección automática
- **finger_tracking.py**: Función `scan_for_available_cameras()` 
- Cambio dinámico de cámaras sin reiniciar servidores
- Detección automática de resolución y FPS reales

## 6. Lógica de Juego (Unity/C#)

### Gestión de Torretas (TurretManager.cs)
- Lista de TurretData (ScriptableObjects) con diferentes tipos
- Coste en oro por torreta
- Instanciación de torretas seleccionadas

### Gestión de Oleadas (MonsterManager.cs)  
- Oleadas complejas multi-partes (Wave y SubWave)
- Sistema de dificultad infinita con multiplicadores

### Economía y Estado (GameManager.cs, PlayerBase.cs)
- GameManager: oro del jugador (ganar/gastar)
- PlayerBase: vida de la base, Game Over

### Interfaz de Usuario (UIManager.cs)
- Información crítica en tiempo real
- UI que se oculta/muestra automáticamente por fase
- Feedback visual (parpadeo de texto, etc.)

## 7. Características Técnicas Avanzadas

### Robustez del Sistema
- Manejo robusto de errores en procesamiento SAM
- Reconexión automática de cámaras
- Mensajes de progreso durante operaciones largas
- Limpieza automática de recursos

### Multijugador en Tiempo Real
- Streams bidireccionales entre jugadores
- Sincronización de estados entre cliente Unity y servidores Python
- Roles especializados con interfaces adaptadas

### Visión por Computador
- MediaPipe para tracking de manos robusto
- SAM para segmentación semántica de escenarios reales
- ArUco para detección de puntos de referencia
- Sistema de rejilla para mapeo preciso de gestos a coordenadas

Este sistema proporciona una experiencia de Tower Defense completamente controlada por gestos, escalable desde single player hasta multijugador cooperativo, con procesamiento avanzado de visión por computador en tiempo real. 