using System.Collections.Generic;
using UnityEngine;

public class NetworkLoadingTaskRegistry : MonoBehaviour
{
    public static NetworkLoadingTaskRegistry Active { get; private set; }

    [SerializeField] private bool collectTasksInChildren = true;
    [SerializeField] private List<NetworkLoadingTask> tasks = new List<NetworkLoadingTask>();

    public IReadOnlyList<NetworkLoadingTask> Tasks => tasks;

    private void Awake()
    {
        Active = this;

        if (collectTasksInChildren)
        {
            CollectTasksInChildren();
        }
    }

    private void OnDestroy()
    {
        if (Active == this)
        {
            Active = null;
        }
    }

    public void Register(NetworkLoadingTask task)
    {
        if (task != null && !tasks.Contains(task))
        {
            tasks.Add(task);
        }
    }

    public void Unregister(NetworkLoadingTask task)
    {
        tasks.Remove(task);
    }

    private void CollectTasksInChildren()
    {
        tasks.Clear();
        GetComponentsInChildren(true, tasks);
    }
}
