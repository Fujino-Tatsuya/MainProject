using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public enum DataSource
{
    /// <summary>xlsx 값 — 빌드와 같은 조건.</summary>
    Table = 0,

    /// <summary>디스크의 SO·프리팹 값 그대로 — 개발 중 인스펙터 실험.</summary>
    Inspector = 1,
}

/// <summary>
/// 플레이 시작 옵션 "데이터 출처"(PLAN-data-table.md D1.5).
/// <para>
/// <b>적용 지점은 여기 한 곳</b> — <c>ExitingEditMode</c>. 기본 Play·Dev Boot·Dev_Boot 씬 직접 Play·MPPM 클론이
/// 모두 이 이벤트를 지나므로 어떤 버튼으로 들어가도 같은 규칙이다.
/// 테이블 값은 메모리에서만 덮어쓰고(디스크 변경 0), 스냅샷은 SessionState 에 둬서 Play 진입 도메인 리로드를 넘긴다.
/// Play 가 끝나면(<c>EnteredEditMode</c>) 스냅샷으로 되돌린다.
/// </para>
/// <para>
/// 테이블에 오류가 있으면 Play 진입을 막는다. xlsx 가 하나도 없으면(아직 SVN 에 없음 등) 막지 않고 SO 값으로 돈다 —
/// 표시가 "TABLE (xlsx 없음)" 으로 바뀐다.
/// </para>
/// </summary>
[InitializeOnLoad]
public static class DataSourcePlayMode
{
    private const string SnapshotSessionKey = "MainProject.DataTable.PlaySnapshot";
    private const string BadgeSessionKey = "MainProject.DataTable.PlayBadge";
    private const string LogPrefix = "[DataTable] ";

    internal static readonly Color TableColor = new Color32(0xFF, 0xDA, 0xC1, 0xFF);     // 피치
    internal static readonly Color InspectorColor = new Color32(0xE2, 0xF0, 0xCB, 0xFF); // 연두

    private static readonly string PrefKey = DevBootTarget.ScopedKey("DataSource");
    private static HashSet<int> snapshotTargets;
    private static readonly HashSet<int> warnedTargets = new HashSet<int>();

    static DataSourcePlayMode()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        Undo.postprocessModifications -= OnModified;
        Undo.postprocessModifications += OnModified;

        // Play 밖에서 스냅샷이 남아 있으면(종료 이벤트를 놓친 경우) 지금 되돌린다.
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            RestorePending();
        }
    }

    public static DataSource Current
    {
        get => EditorPrefs.GetInt(PrefKey, (int)DataSource.Table) == (int)DataSource.Inspector
            ? DataSource.Inspector
            : DataSource.Table;
        set => EditorPrefs.SetInt(PrefKey, (int)value);
    }

    public static string Label(DataSource source) => source == DataSource.Table ? "테이블" : "인스펙터";

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        switch (state)
        {
            case PlayModeStateChange.ExitingEditMode:
                RestorePending(); // 이전 Play 의 찌꺼기가 있으면 먼저 정리
                PrepareTablePlay();
                break;
            case PlayModeStateChange.EnteredPlayMode:
                ShowBadge();
                break;
            case PlayModeStateChange.EnteredEditMode:
                RestorePending();
                break;
        }
    }

    private static void PrepareTablePlay()
    {
        if (Current != DataSource.Table)
        {
            SessionState.SetString(BadgeSessionKey, "DATA: INSPECTOR");
            return;
        }

        DataTableSource.Result result = DataTableSource.Load();
        if (result.FileCount == 0)
        {
            Debug.LogWarning($"{LogPrefix}데이터 출처 = 테이블인데 xlsx 가 없다({DataTableSource.Folder}) — SO 값으로 돈다. SVN 업데이트 확인.");
            SessionState.SetString(BadgeSessionKey, "DATA: TABLE (xlsx 없음)");
            return;
        }

        if (!DataTableMenu.ReportIssues(result, "테이블 Play"))
        {
            // 진입 취소. 반쯤 적용된 상태가 생기지 않도록 아무것도 쓰기 전에 멈춘다.
            EditorApplication.isPlaying = false;
            return;
        }

        DataTableSnapshot snapshot = DataTableApplier.ApplyInMemory(result.Writes);
        SessionState.SetString(SnapshotSessionKey, JsonUtility.ToJson(snapshot));
        SessionState.SetString(BadgeSessionKey, "DATA: TABLE");
        snapshotTargets = null;
        warnedTargets.Clear();
        Debug.Log($"{LogPrefix}테이블 Play — xlsx {result.FileCount}개, 필드 {result.Writes.Count}개를 메모리에 적용(디스크 변경 없음).");
    }

    private static void ShowBadge()
    {
        string text = SessionState.GetString(BadgeSessionKey, string.Empty);
        if (text.Length > 0)
        {
            DataSourceBadge.Show(text, text.StartsWith("DATA: TABLE") ? TableColor : InspectorColor);
        }
    }

    private static void RestorePending()
    {
        string json = SessionState.GetString(SnapshotSessionKey, string.Empty);
        if (json.Length == 0)
        {
            return;
        }

        SessionState.EraseString(SnapshotSessionKey);
        snapshotTargets = null;
        DataTableSnapshot snapshot = JsonUtility.FromJson<DataTableSnapshot>(json);
        int missing = snapshot == null ? -1 : DataTableApplier.Restore(snapshot);
        if (missing != 0)
        {
            Debug.LogError($"{LogPrefix}테이블 Play 복구 중 SO {(missing < 0 ? "스냅샷을 읽지 못함" : missing + "개를 찾지 못함")} — " +
                           "디스크에는 쓰지 않았으므로 에디터를 다시 열면 원래 값이다. 그 전에 Save Project 하지 말 것.");
        }
    }

    /// <summary>
    /// 테이블 Play 중 테이블이 관리하는 SO 를 인스펙터에서 고치면 경고한다 — Play 가 끝나면 스냅샷으로 되돌아가 사라지고,
    /// 그 사이 Save Project 를 하면 테이블 값까지 디스크에 저장된다(PLAN R8).
    /// </summary>
    private static UndoPropertyModification[] OnModified(UndoPropertyModification[] modifications)
    {
        if (!EditorApplication.isPlaying)
        {
            return modifications;
        }

        if (snapshotTargets == null)
        {
            string json = SessionState.GetString(SnapshotSessionKey, string.Empty);
            DataTableSnapshot snapshot = json.Length == 0 ? null : JsonUtility.FromJson<DataTableSnapshot>(json);
            snapshotTargets = new HashSet<int>(snapshot?.items.Select(i => i.instanceId) ?? Enumerable.Empty<int>());
        }

        foreach (UndoPropertyModification modification in modifications)
        {
            Object target = modification.currentValue?.target;
            if (target != null && snapshotTargets.Contains(target.GetInstanceID()) && warnedTargets.Add(target.GetInstanceID()))
            {
                Debug.LogWarning($"{LogPrefix}테이블 Play 중 '{target.name}' 을 고쳤다 — 이 SO 는 테이블이 관리한다. " +
                                 "Play 가 끝나면 원래 값으로 돌아간다. 실험은 데이터 출처 = 인스펙터에서 할 것.", target);
            }
        }

        return modifications;
    }
}
