using System;
using UnityEngine;

public static class BeforeMergeTestLog
{
    private const string RootTag = "beforeMergeTest";

    public static void Info(string flow, string eventName, string message, UnityEngine.Object context)
    {
        Debug.Log(Format(flow, eventName, message), context);
    }

    public static void Warning(string flow, string eventName, string message, UnityEngine.Object context)
    {
        Debug.LogWarning(Format(flow, eventName, message), context);
    }

    private static string Format(string flow, string eventName, string message)
    {
        return $"[{RootTag}][{flow}][{eventName}][{DateTime.Now:HH:mm:ss.fff}][frame={Time.frameCount}][rt={Time.realtimeSinceStartup:F3}] {message}";
    }
}
