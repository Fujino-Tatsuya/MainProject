#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MPPM(Play Mode Scenario) 를 툴바 Play 버튼과 같은 경로로 시작한다 — QA 창(Tools/QA) ▸ 맨 위 버튼과 <see cref="QAAutomation"/> 이 부른다.
/// 🔴 EditorApplication.isPlaying 토글은 가상 플레이어가 안 붙는다(메모리 규칙). 여기서는 MPPM 의
///    PlayModeManager 진입점을 리플렉션으로 찾아 부른다. 내부 API 라 버전이 바뀌면 QA 창 ▸ 디버그 ▸ MPPM API 덤프 로 다시 확인할 것.
/// </summary>
public static class QAScenarioPlay
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

    const string BootScene = "Assets/0.Scenes/MainFlow/0.BootStrapScene.unity";

    static Type FindType(string fullName)
    {
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = a.GetType(fullName);
            if (t != null)
                return t;
        }
        return null;
    }

    static object GetSingleton(Type t)
    {
        for (Type cur = t; cur != null; cur = cur.BaseType)
        {
            PropertyInfo p = cur.GetProperty("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (p != null)
                return p.GetValue(null);
        }
        return null;
    }

    /// <summary>활성 Play Mode 설정과 가상 플레이어 수를 콘솔에 남긴다.</summary>
    public static void Status()
    {
        Type managerType = FindType("Unity.PlayMode.Editor.PlayModeManager");
        object manager = managerType != null ? GetSingleton(managerType) : null;
        object config = manager != null ? managerType.GetProperty("ActivePlayModeConfig", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(manager) : null;
        string state = manager != null ? managerType.GetProperty("CurrentState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(manager)?.ToString() : "?";

        string detail = "";
        if (config != null && config.GetType().Name == "ScenarioConfig")
        {
            var virt = config.GetType().GetProperty("VirtualEditorInstances", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(config) as System.Collections.ICollection;
            var local = config.GetType().GetProperty("LocalInstances", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(config) as System.Collections.ICollection;
            detail = $" virtual={virt?.Count} local={local?.Count}";
        }

        Debug.Log($"[QA] MPPM 상태: manager={(manager != null)} state={state} config={(config as UnityEngine.Object)?.name ?? config?.ToString() ?? "null"} " +
                  $"type={config?.GetType().Name}{detail} playing={EditorApplication.isPlaying} " +
                  $"scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().path}");
    }

    /// <summary>
    /// MPPM 창의 Player 2·3 체크박스와 같은 경로(MultiplayerPlaymode.PlayerN.Activate)로 가상 플레이어를 켠다.
    /// 클론 에디터가 뜨는 데 시간이 걸린다 — 켠 뒤 상태 로그(PlayerState)로 Communicative 를 확인하고 Play 할 것.
    /// </summary>
    public static void ActivateVirtualPlayers()
    {
        EnsureClientTags();
        Type mppm = FindType("Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode");
        if (mppm == null)
        {
            Debug.LogError("[QA] MultiplayerPlaymode 타입을 찾지 못했다.");
            return;
        }

        foreach (string prop in new[] { "PlayerTwo", "PlayerThree" })
        {
            object player = mppm.GetProperty(prop, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            if (player == null)
            {
                Debug.LogError($"[QA] {prop} 없음");
                continue;
            }

            object state = player.GetType().GetProperty("PlayerState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(player);
            MethodInfo activate = player.GetType().GetMethod("Activate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (activate == null)
            {
                Debug.LogError($"[QA] {prop}.Activate 없음");
                continue;
            }

            ParameterInfo[] ps = activate.GetParameters();
            var args = new object[ps.Length];
            for (int i = 0; i < ps.Length; i++)
            {
                Type pt = ps[i].ParameterType.IsByRef ? ps[i].ParameterType.GetElementType() : ps[i].ParameterType;
                args[i] = ps[i].IsOut || ps[i].ParameterType.IsByRef ? null
                    : pt.IsValueType || pt.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(pt) : null;
            }

            object ok = activate.Invoke(player, args);
            Debug.Log($"[QA] {prop}.Activate → {ok} (이전 상태 {state}, error={args[0]})");
        }
    }

    /// <summary>
    /// 가상 플레이어 2·3 에 MPPM 태그 <c>Client</c>(<see cref="DemoConnectionConfig.MppmClientTag"/>)를 붙인다 — 없으면 데모 흐름이 안 켜지고
    /// 옛 수동 흐름(타이틀)으로 돈다(lobby-demo-flow.md §6). 48.3 2차에서 태그가 비어 클론이 타이틀로 간 것을 확인했다.
    /// 에디터 로컬 MPPM 설정(Library/VP)이라 git 무관. 이미 켜진 클론은 다시 켜야 태그가 반영된다 — QA 창 ▸ 가상 플레이어 ▸ 끄기 후 켜기.
    /// </summary>
    public static void EnsureClientTags()
    {
        string tag = DemoConnectionConfig.MppmClientTag;
        Type mppm = FindType("Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode"); // internal — 리플렉션
        object projectTags = mppm?.GetProperty("PlayerTags", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
        if (projectTags == null)
        {
            Debug.LogError("[QA] MPPM PlayerTags 를 찾지 못했다.");
            return;
        }
        Type tagsType = projectTags.GetType();
        bool has = (bool)tagsType.GetMethod("Contains").Invoke(projectTags, new object[] { tag });
        if (!has)
        {
            var args = new object[] { tag, null };
            bool added = (bool)tagsType.GetMethod("Add").Invoke(projectTags, args);
            Debug.Log($"[QA] MPPM 프로젝트 태그 '{tag}' 추가 → {added} ({args[1]})");
        }

        foreach (string prop in new[] { "PlayerTwo", "PlayerThree" })
        {
            object player = mppm.GetProperty(prop, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            if (player == null)
                continue;
            var tags = player.GetType().GetProperty("Tags")?.GetValue(player) as string[];
            if (tags != null && Array.IndexOf(tags, tag) >= 0)
                continue;
            var args = new object[] { tag, null };
            bool ok = (bool)player.GetType().GetMethod("AddTag").Invoke(player, args);
            Debug.Log($"[QA] {prop} 에 MPPM 태그 '{tag}' 추가 → {ok} ({args[1]})");
        }
    }

    /// <summary>가상 플레이어 2·3 을 끈다(태그 반영·정리용). 끝나면 Play 를 먼저 멈출 것.</summary>
    public static void DeactivateVirtualPlayers()
    {
        Type mppm = FindType("Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode");
        foreach (string prop in new[] { "PlayerTwo", "PlayerThree" })
        {
            object player = mppm?.GetProperty(prop, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            MethodInfo deactivate = player?.GetType().GetMethod("Deactivate", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (deactivate == null)
                continue;
            var args = new object[deactivate.GetParameters().Length];
            object ok = deactivate.Invoke(player, args);
            Debug.Log($"[QA] {prop}.Deactivate → {ok} ({(args.Length > 0 ? args[0] : null)})");
        }
    }

    /// <summary>가상 플레이어 상태(PlayerState) 로그.</summary>
    public static void PlayerStates()
    {
        Type mppm = FindType("Unity.Multiplayer.PlayMode.Editor.MultiplayerPlaymode");
        var sb = new StringBuilder("[QA] 가상 플레이어:");
        foreach (string prop in new[] { "PlayerOne", "PlayerTwo", "PlayerThree", "PlayerFour" })
        {
            object player = mppm?.GetProperty(prop, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
            object state = player?.GetType().GetProperty("PlayerState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(player);
            sb.Append($" {prop}={state}");
        }
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 0.BootStrapScene 을 열고(저장 안 된 변경이 있으면 중단) 툴바 Play 와 같은 경로로 Play 를 시작한다.
    /// 활성 설정이 Play Mode Scenario 면 가상 플레이어도 함께 뜬다.
    /// </summary>
    public static void Play()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[QA] 이미 Play 중이다.");
            return;
        }

        var active = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        if (active.path != BootScene)
        {
            if (active.isDirty)
            {
                Debug.LogError($"[QA] 현재 씬 {active.path} 에 저장 안 된 변경이 있어 0.BootStrapScene 으로 바꾸지 않는다.");
                return;
            }
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene(BootScene);
        }

        Type managerType = FindType("Unity.PlayMode.Editor.PlayModeManager");
        MethodInfo toggle = managerType?.GetMethod("TogglePlayingShortcut", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (toggle == null)
        {
            Debug.LogError("[QA] PlayModeManager.TogglePlayingShortcut 를 찾지 못했다 — QA 창(Tools/QA) ▸ 디버그 ▸ MPPM API 덤프 로 확인할 것.");
            return;
        }

        Status();
        Debug.Log("[QA] Play Mode Scenario 시작(툴바 Play 와 같은 경로).");
        toggle.Invoke(null, null);
    }

    /// <summary>Play 중이면 툴바 Stop 과 같은 경로로 멈춘다(가상 플레이어도 함께 정지).</summary>
    public static void Stop()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("[QA] Play 중이 아니다.");
            return;
        }
        Type managerType = FindType("Unity.PlayMode.Editor.PlayModeManager");
        MethodInfo toggle = managerType?.GetMethod("TogglePlayingShortcut", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        if (toggle != null)
            toggle.Invoke(null, null);
        else
            EditorApplication.isPlaying = false;
        Debug.Log("[QA] Play Mode Scenario 정지.");
    }

    /// <summary>MPPM 관련 내부 API(타입·메서드·속성)를 Logs/qa-mppm-api.txt 로 덤프한다.</summary>
    public static void Dump()
    {
        var sb = new StringBuilder();
        foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
        {
            string an = a.GetName().Name;
            if (!an.Contains("PlayMode") && !an.Contains("Multiplayer"))
                continue;
            Type[] types;
            try { types = a.GetTypes(); } catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
            foreach (Type t in types)
            {
                bool interesting = t.Name.Contains("PlayModeManager") || t.Name.Contains("PlayModeButton") ||
                                   t.Name.Contains("Scenario") && t.Name.Contains("Run") || t.Name == "ScenarioConfig" ||
                                   t.Name.Contains("PlaymodeDropdown");
                if (!interesting)
                {
                    // 가상 플레이어 활성화 후보: Activate/Launch/Open 류 메서드를 가진 타입 이름만.
                    bool hasActivate = false;
                    try { hasActivate = t.GetMethods(All).Any(m => m.Name.Contains("Activate") || m.Name.Contains("Launch")); } catch { }
                    if (hasActivate)
                        sb.AppendLine($"-- {an} :: {t.FullName} :: " + string.Join(", ",
                            t.GetMethods(All).Where(m => m.Name.Contains("Activate") || m.Name.Contains("Launch") || m.Name.Contains("Player"))
                                .Select(m => $"{(m.IsStatic ? "s " : "")}{m.Name}({string.Join(",", m.GetParameters().Select(p => p.ParameterType.Name))})").Distinct()));
                    continue;
                }
                sb.AppendLine($"== {an} :: {t.FullName}");
                foreach (MethodInfo m in t.GetMethods(All))
                    sb.AppendLine($"   M {(m.IsStatic ? "static " : "")}{m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
                foreach (PropertyInfo p in t.GetProperties(All))
                    sb.AppendLine($"   P {p.PropertyType.Name} {p.Name}");
                foreach (FieldInfo f in t.GetFields(All))
                    sb.AppendLine($"   F {(f.IsStatic ? "static " : "")}{f.FieldType.Name} {f.Name}");
            }
        }
        string path = Path.GetFullPath("Logs/qa-mppm-api.txt");
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[QA] MPPM API 덤프: {path}");
    }
}
#endif
