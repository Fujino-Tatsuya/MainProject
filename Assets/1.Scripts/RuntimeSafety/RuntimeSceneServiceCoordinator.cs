using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace VeyTrace.RuntimeSafety
{
    public readonly struct RuntimeSceneServiceReport
    {
        public RuntimeSceneServiceReport(
            int enabledAudioListeners,
            int enabledEventSystems,
            int suppressedAudioListeners,
            int suppressedEventSystems)
        {
            EnabledAudioListeners = enabledAudioListeners;
            EnabledEventSystems = enabledEventSystems;
            SuppressedAudioListeners = suppressedAudioListeners;
            SuppressedEventSystems = suppressedEventSystems;
        }

        public int EnabledAudioListeners { get; }
        public int EnabledEventSystems { get; }
        public int SuppressedAudioListeners { get; }
        public int SuppressedEventSystems { get; }
    }

    public static class RuntimeSceneServiceCoordinator
    {
        private static readonly HashSet<AudioListener> SuppressedListeners = new();
        private static readonly HashSet<EventSystem> SuppressedEventSystems = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallSceneHooks()
        {
            SceneManager.sceneLoaded -= HandleSceneChanged;
            SceneManager.sceneLoaded += HandleSceneChanged;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
            SuppressedListeners.Clear();
            SuppressedEventSystems.Clear();
        }

        public static RuntimeSceneServiceReport Reconcile()
        {
            AudioListener[] listeners = Object.FindObjectsByType<AudioListener>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            EventSystem[] eventSystems = Object.FindObjectsByType<EventSystem>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            AudioListener selectedListener = SelectPreferred(
                listeners,
                SuppressedListeners);
            EventSystem selectedEventSystem = SelectPreferred(
                eventSystems,
                SuppressedEventSystems);

            int enabledListeners = ReconcileBehaviours(
                listeners,
                selectedListener,
                SuppressedListeners);
            int enabledEventSystems = ReconcileBehaviours(
                eventSystems,
                selectedEventSystem,
                SuppressedEventSystems);

            return new RuntimeSceneServiceReport(
                enabledListeners,
                enabledEventSystems,
                SuppressedListeners.Count,
                SuppressedEventSystems.Count);
        }

        public static void RestoreAll()
        {
            RestoreSuppressed(SuppressedListeners);
            RestoreSuppressed(SuppressedEventSystems);
        }

        private static T SelectPreferred<T>(
            T[] behaviours,
            HashSet<T> suppressed)
            where T : Behaviour
        {
            T selected = null;
            int selectedPriority = int.MinValue;

            for (int i = 0; i < behaviours.Length; i++)
            {
                T candidate = behaviours[i];
                if (candidate == null ||
                    !candidate.gameObject.activeInHierarchy ||
                    (!candidate.enabled && !suppressed.Contains(candidate)))
                    continue;

                int priority = GetScenePriority(candidate.gameObject.scene);
                if (selected != null &&
                    (priority < selectedPriority ||
                     (priority == selectedPriority &&
                      candidate.GetInstanceID() >= selected.GetInstanceID())))
                    continue;

                selected = candidate;
                selectedPriority = priority;
            }

            return selected;
        }

        private static int ReconcileBehaviours<T>(
            T[] behaviours,
            T selected,
            HashSet<T> suppressed)
            where T : Behaviour
        {
            int enabledCount = 0;
            suppressed.RemoveWhere(item => item == null);

            for (int i = 0; i < behaviours.Length; i++)
            {
                T current = behaviours[i];
                if (current == null || !current.gameObject.activeInHierarchy)
                    continue;

                if (current == selected)
                {
                    if (suppressed.Remove(current))
                        current.enabled = true;
                    if (current.enabled)
                        enabledCount++;
                    continue;
                }

                if (!current.enabled)
                    continue;

                current.enabled = false;
                suppressed.Add(current);
            }

            return enabledCount;
        }

        private static int GetScenePriority(Scene scene)
        {
            return GetScenePriority(scene.IsValid() ? scene.name : string.Empty);
        }

        internal static int GetScenePriority(string sceneName)
        {
            if (sceneName.Contains("Loading"))
                return 0;
            if (sceneName.Contains("Lobby"))
                return 1;
            return 2;
        }

        private static void RestoreSuppressed<T>(HashSet<T> suppressed)
            where T : Behaviour
        {
            foreach (T behaviour in suppressed)
            {
                if (behaviour != null)
                    behaviour.enabled = true;
            }

            suppressed.Clear();
        }

        private static void HandleSceneChanged(Scene _, LoadSceneMode __)
        {
            Reconcile();
        }

        private static void HandleSceneUnloaded(Scene _)
        {
            Reconcile();
        }
    }
}
