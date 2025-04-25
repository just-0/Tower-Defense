using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

public class AStarPathfinder : MonoBehaviour
{
    public RawImage maskDisplay;
    public GameObject pathMarkerPrefab;

    private Texture2D maskTexture;
    private int[,] grid;
    private List<Vector2Int> path = new List<Vector2Int>();

    public Vector2Int startPoint;
    public Vector2Int endPoint;

    public bool showGridPoints = false;

    void Start()
    {
        // Control manual por el usuario
    }

    public void InitializeGrid()
    {
        if (maskDisplay == null || maskDisplay.texture == null)
        {
            Debug.LogError("No hay RawImage o textura asignada en maskDisplay");
            return;
        }

        RenderTexture renderTexture = RenderTexture.GetTemporary(
            maskDisplay.texture.width,
            maskDisplay.texture.height,
            0,
            RenderTextureFormat.ARGB32
        );

        Graphics.Blit(maskDisplay.texture, renderTexture);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = renderTexture;

        maskTexture = new Texture2D(
            renderTexture.width,
            renderTexture.height,
            TextureFormat.RGBA32,
            false
        );

        maskTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        maskTexture.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(renderTexture);

        grid = new int[maskTexture.width, maskTexture.height];
        Color32[] pixels = maskTexture.GetPixels32();

        for (int y = 0; y < maskTexture.height; y++)
        {
            for (int x = 0; x < maskTexture.width; x++)
            {
                int index = y * maskTexture.width + x;
                if (index < pixels.Length)
                {
                    grid[x, y] = (pixels[index].r < 128) ? 1 : 0;

                    if (showGridPoints)
                        CreateDebugPoint(new Vector2Int(x, y), grid[x, y] == 1 ? Color.black : Color.white);
                }
                else
                {
                    grid[x, y] = 0;
                }
            }
        }

        if (startPoint == Vector2Int.zero)
            startPoint = new Vector2Int(maskTexture.width - 1, maskTexture.height / 2);
        if (endPoint == Vector2Int.zero)
            endPoint = new Vector2Int(0, maskTexture.height / 2);
    }

    public void CalculatePath()
    {
        if (grid == null)
        {
            Debug.LogWarning("Grid no inicializado. Llamando a InitializeGrid primero.");
            InitializeGrid();
            if (grid == null) return;
        }

        ClearVisualizations();

        startPoint.x = Mathf.Clamp(startPoint.x, 0, maskTexture.width - 1);
        startPoint.y = Mathf.Clamp(startPoint.y, 0, maskTexture.height - 1);
        endPoint.x = Mathf.Clamp(endPoint.x, 0, maskTexture.width - 1);
        endPoint.y = Mathf.Clamp(endPoint.y, 0, maskTexture.height - 1);

        Debug.Log($"Calculando ruta desde ({startPoint.x}, {startPoint.y}) hasta ({endPoint.x}, {endPoint.y})");

        path = AStar(startPoint, endPoint);

        if (path.Count > 0)
        {
            Debug.Log($"Ruta calculada con {path.Count} puntos.");
            VisualizePath();
        }

        DrawDebugSpheres();
    }

    List<Vector2Int> AStar(Vector2Int start, Vector2Int end)
{
    // Add debugging at the start
    Debug.Log($"Starting A* from ({start.x}, {start.y}) to ({end.x}, {end.y})");
    
    // Check if start or end are in obstacles
    if (grid[start.x, start.y] == 1)
    {
        Debug.LogError("Start point is inside an obstacle!");
        return new List<Vector2Int>();
    }
    
    if (grid[end.x, end.y] == 1)
    {
        Debug.LogError("End point is inside an obstacle!");
        return new List<Vector2Int>();
    }
    
    int width = grid.GetLength(0);
    int height = grid.GetLength(1);
    
    // Priority queue using a binary heap would be more efficient,
    // but we'll use a list and sort it for simplicity
    List<Node> openSet = new List<Node>();
    
    // Use Dictionary instead of HashSet for closed nodes for better performance
    Dictionary<Vector2Int, bool> closedSet = new Dictionary<Vector2Int, bool>();
    
    // Track all nodes for easy lookup
    Dictionary<Vector2Int, Node> allNodes = new Dictionary<Vector2Int, Node>();
    
    Node startNode = new Node(start);
    startNode.gCost = 0;
    startNode.hCost = CalculateHCost(start, end);
    startNode.CalculateFCost();
    
    openSet.Add(startNode);
    allNodes[start] = startNode;
    
    Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
    
    int iterations = 0;
    int maxIterations = width * height * 2; // Safety limit to prevent infinite loops
    
    while (openSet.Count > 0)
    {
        iterations++;
        if (iterations > maxIterations)
        {
            Debug.LogError("A* exceeded maximum iterations. Path might be impossible.");
            break;
        }
        
        // Sort and get the lowest F-cost node
        // Add a tie-breaker for better path selection when F costs are equal
        openSet.Sort((a, b) => {
            int fCompare = a.fCost.CompareTo(b.fCost);
            if (fCompare != 0) return fCompare;
            return a.hCost.CompareTo(b.hCost); // If F costs are equal, prefer the node closer to the target
        });
        
        Node currentNode = openSet[0];
        openSet.RemoveAt(0);
        
        closedSet[currentNode.position] = true;
        
        // Check if we reached the end
        if (currentNode.position == end)
        {
            Debug.Log($"Path found in {iterations} iterations!");
            return ReconstructPath(cameFrom, end);
        }
        
        // Check all 8 neighbors
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                // Skip the current node
                if (dx == 0 && dy == 0) continue;
                
                Vector2Int neighborPos = new Vector2Int(
                    currentNode.position.x + dx,
                    currentNode.position.y + dy
                );
                
                // Skip invalid positions
                if (neighborPos.x < 0 || neighborPos.x >= width || 
                    neighborPos.y < 0 || neighborPos.y >= height)
                {
                    continue;
                }
                
                // Skip obstacles and already processed nodes
                if (grid[neighborPos.x, neighborPos.y] == 1 || closedSet.ContainsKey(neighborPos))
                {
                    continue;
                }
                
                // Calculate movement cost (diagonal costs more)
                float movementCost = (dx != 0 && dy != 0) ? 1.414f : 1.0f;
                
                // Add a small cost for changing direction (helps create smoother paths)
                if (cameFrom.ContainsKey(currentNode.position))
                {
                    Vector2Int previous = cameFrom[currentNode.position];
                    int prevDx = currentNode.position.x - previous.x;
                    int prevDy = currentNode.position.y - previous.y;
                    
                    // If direction changed, add a tiny cost
                    if (prevDx != dx || prevDy != dy)
                    {
                        movementCost += 0.001f;
                    }
                }
                
                float tentativeGCost = currentNode.gCost + movementCost;
                
                // Check if we need to update an existing node or add a new one
                bool needsUpdate = false;
                Node neighborNode;
                
                if (allNodes.TryGetValue(neighborPos, out neighborNode))
                {
                    // We've seen this node before
                    if (tentativeGCost < neighborNode.gCost)
                    {
                        // Found a better path to this node
                        neighborNode.gCost = tentativeGCost;
                        neighborNode.CalculateFCost();
                        cameFrom[neighborPos] = currentNode.position;
                        
                        // If it's not in open set, add it back
                        if (!openSet.Contains(neighborNode))
                        {
                            openSet.Add(neighborNode);
                        }
                    }
                }
                else
                {
                    // First time seeing this node
                    neighborNode = new Node(neighborPos);
                    neighborNode.gCost = tentativeGCost;
                    neighborNode.hCost = CalculateHCost(neighborPos, end);
                    neighborNode.CalculateFCost();
                    
                    allNodes[neighborPos] = neighborNode;
                    openSet.Add(neighborNode);
                    cameFrom[neighborPos] = currentNode.position;
                }
            }
        }
    }
    
    Debug.LogWarning("No path found after exhaustive search!");
    return new List<Vector2Int>();
}

// Using Euclidean Distance instead of Manhattan for better diagonal pathfinding
float CalculateHCost(Vector2Int a, Vector2Int b)
{
    // Euclidean distance works better for 8-directional movement
    float dx = Mathf.Abs(a.x - b.x);
    float dy = Mathf.Abs(a.y - b.y);
    
    // Use a small tie-breaker to encourage paths that are closer to the straight line from start to end
    float tieBreaker = 0.001f;
    
    return Mathf.Sqrt(dx * dx + dy * dy) * (1.0f + tieBreaker);
}

List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int end)
{
    List<Vector2Int> path = new List<Vector2Int>();
    Vector2Int current = end;
    
    path.Add(current);
    
    // Security to avoid infinite loops
    int maxIterations = cameFrom.Count;
    int iterations = 0;
    
    while (cameFrom.ContainsKey(current) && iterations < maxIterations)
    {
        current = cameFrom[current];
        path.Add(current);
        iterations++;
    }
    
    path.Reverse();
    
    // Optional: Path smoothing to remove unnecessary zigzags
    if (path.Count > 2)
    {
       // path = SmoothPath(path);
    }
    
    return path;
}

List<Vector2Int> SmoothPath(List<Vector2Int> originalPath)
{
    List<Vector2Int> smoothPath = new List<Vector2Int>();
    smoothPath.Add(originalPath[0]); // Add start point
    
    int currentIndex = 0;
    
    // Try to skip nodes if we can draw a straight line without obstacles
    while (currentIndex < originalPath.Count - 1)
    {
        int furthestVisible = currentIndex + 1;
        
        for (int i = currentIndex + 2; i < originalPath.Count; i++)
        {
            if (IsPathClear(originalPath[currentIndex], originalPath[i]))
            {
                furthestVisible = i;
            }
            else
            {
                break;
            }
        }
        
        smoothPath.Add(originalPath[furthestVisible]);
        currentIndex = furthestVisible;
    }
    
    return smoothPath;
}

bool IsPathClear(Vector2Int a, Vector2Int b)
{
    // Using Bresenham's line algorithm to check if path between two points is clear
    int x = a.x;
    int y = a.y;
    int dx = Mathf.Abs(b.x - a.x);
    int dy = Mathf.Abs(b.y - a.y);
    int sx = a.x < b.x ? 1 : -1;
    int sy = a.y < b.y ? 1 : -1;
    int err = dx - dy;
    
    while (x != b.x || y != b.y)
    {
        // Check if current position is an obstacle
        if (grid[x, y] == 1)
        {
            return false;
        }
        
        int e2 = 2 * err;
        if (e2 > -dy)
        {
            err -= dy;
            x += sx;
        }
        if (e2 < dx)
        {
            err += dx;
            y += sy;
        }
    }
    
    return true;
}

private class Node
{
    public Vector2Int position;
    public float gCost; // Cost from start
    public float hCost; // Heuristic cost to end
    public float fCost; // Total cost
    
    public Node(Vector2Int pos)
    {
        position = pos;
    }
    
    public void CalculateFCost()
    {
        fCost = gCost + hCost;
    }
}
    void ClearVisualizations()
    {
        foreach (Transform child in transform)
        {
            Destroy(child.gameObject);
        }
    }

    void DrawDebugSpheres()
    {
        Vector3 startWorldPos = GridToWorldPoint(startPoint);
        Vector3 endWorldPos = GridToWorldPoint(endPoint);

        GameObject startSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        startSphere.transform.position = startWorldPos;
        startSphere.transform.localScale = Vector3.one * 0.3f;
        startSphere.GetComponent<Renderer>().material.color = Color.green;
        startSphere.name = "StartPoint";
        startSphere.transform.parent = transform;

        GameObject endSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        endSphere.transform.position = endWorldPos;
        endSphere.transform.localScale = Vector3.one * 0.3f;
        endSphere.GetComponent<Renderer>().material.color = Color.red;
        endSphere.name = "EndPoint";
        endSphere.transform.parent = transform;

        Debug.Log($"Posición start: Grid=({startPoint.x}, {startPoint.y}), World={startWorldPos}");
        Debug.Log($"Posición end: Grid=({endPoint.x}, {endPoint.y}), World={endWorldPos}");
    }

    Vector3 GridToWorldPoint(Vector2Int gridPoint)
    {
        float normalizedX = (float)gridPoint.x / maskTexture.width;
        float normalizedY = (float)gridPoint.y / maskTexture.height;

        RectTransform rectTransform = maskDisplay.GetComponent<RectTransform>();

        Vector3 localPos = new Vector3(
            Mathf.Lerp(-rectTransform.rect.width / 2, rectTransform.rect.width / 2, normalizedX),
            Mathf.Lerp(-rectTransform.rect.height / 2, rectTransform.rect.height / 2, 1 - normalizedY),
            0
        );

        return rectTransform.TransformPoint(localPos);
    }

    void CreateDebugPoint(Vector2Int gridPoint, Color color)
    {
        Vector3 worldPos = GridToWorldPoint(gridPoint);
        GameObject point = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        point.transform.position = worldPos;
        point.transform.localScale = Vector3.one * 0.1f;
        point.GetComponent<Renderer>().material.color = color;
        point.name = "GridPoint";
        point.transform.parent = transform;
    }

    void VisualizePath()
    {
        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int point = path[i];
            Vector3 worldPos = GridToWorldPoint(point);
            GameObject marker = Instantiate(pathMarkerPrefab, worldPos, Quaternion.identity);
            marker.name = $"PathMarker_{i}";
            marker.transform.parent = transform;
            marker.transform.localScale = Vector3.one * 0.05f; // o 0.1f, prueba

        }
    }
}