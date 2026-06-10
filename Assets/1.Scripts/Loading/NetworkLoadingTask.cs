using System.Collections;
using UnityEngine;

public abstract class NetworkLoadingTask : MonoBehaviour
{
    [SerializeField] private string taskName;
    [SerializeField, Min(0.01f)] private float weight = 1f;

    public string TaskName => string.IsNullOrWhiteSpace(taskName) ? name : taskName;
    public float Weight => weight;
    public float Progress { get; private set; }
    public bool IsRunning { get; private set; }
    public bool IsDone { get; private set; }
    public bool HasFailed { get; private set; }
    public string Error { get; private set; }

    public IEnumerator Run()
    {
        Progress = 0f;
        IsDone = false;
        HasFailed = false;
        Error = string.Empty;
        IsRunning = true;

        var routine = Execute();
        while (true)
        {
            object current;
            try
            {
                if (!routine.MoveNext())
                {
                    break;
                }

                current = routine.Current;
            }
            catch (System.Exception exception)
            {
                HasFailed = true;
                Error = exception.Message;
                Debug.LogException(exception, this);
                break;
            }

            yield return current;
        }

        Progress = HasFailed ? Progress : 1f;
        IsRunning = false;
        IsDone = true;
    }

    protected void SetProgress(float progress)
    {
        Progress = Mathf.Clamp01(progress);
    }

    protected abstract IEnumerator Execute();
}
