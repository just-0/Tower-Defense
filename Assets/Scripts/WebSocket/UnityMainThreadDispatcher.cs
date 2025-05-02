using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>
/// Un dispatcher de hilo único para ejecutar acciones en el hilo principal de Unity
/// </summary>
public class UnityMainThreadDispatcher : MonoBehaviour
{
    private static readonly Queue<Action> _executionQueue = new Queue<Action>();
    private static UnityMainThreadDispatcher _instance = null;

    public static UnityMainThreadDispatcher Instance()
    {
        if (_instance == null)
        {
            _instance = FindObjectOfType<UnityMainThreadDispatcher>();
            if (_instance == null)
            {
                GameObject obj = new GameObject("UnityMainThreadDispatcher");
                _instance = obj.AddComponent<UnityMainThreadDispatcher>();
                DontDestroyOnLoad(obj);
            }
        }
        return _instance;
    }

    void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            DontDestroyOnLoad(this.gameObject);
        }
    }

    void Update()
    {
        lock(_executionQueue)
        {  
            while (_executionQueue.Count > 0)
            {
                _executionQueue.Dequeue().Invoke();
            }
        }
    }

    /// <summary>
    /// Encola una acción para ser ejecutada en el hilo principal de Unity
    /// </summary>
    /// <param name="action">Acción a ejecutar</param>
    public void Enqueue(Action action)
    {
        lock (_executionQueue)
        {
            _executionQueue.Enqueue(action);
        }
    }

    /// <summary>
    /// Encola una acción para ser ejecutada en el hilo principal de Unity
    /// y retorna una tarea que se completa cuando la acción se ha ejecutado
    /// </summary>
    /// <param name="action">Acción a ejecutar</param>
    /// <returns>Tarea que se completa cuando la acción se ha ejecutado</returns>
    public Task EnqueueAsync(Action action)
    {
        var tcs = new TaskCompletionSource<bool>();
        
        Enqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        
        return tcs.Task;
    }
}