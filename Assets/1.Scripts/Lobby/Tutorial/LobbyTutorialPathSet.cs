using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>로비 튜토리얼 바닥 경로 ID(할 일 158 계획 §1.5). 157 계약이 다른 이름을 정하면 그쪽에 맞춘다.</summary>
public static class LobbyTutorialPathIds
{
    public const string Locker = "Locker";
    public const string Central = "Central";
    public const string TrainingInside = "TrainingInside";
    public const string Combat = "Combat";
    public const string Counter = "Counter";
    public const string Exit = "Exit";
    public const string BriefCentral = "BriefCentral";
    public const string Sortie = "Sortie";

    public static readonly IReadOnlyList<string> All = new[] { Locker, Central, TrainingInside, Combat, Counter, Exit, BriefCentral, Sortie };
}

/// <summary>경로 켜고 끄기 판정(순수). 빈 ID = 아무 경로도 안 켬. 대소문자 구분.</summary>
public static class LobbyTutorialPathRules
{
    public static bool ShouldShow(string entryId, string currentId) =>
        !string.IsNullOrEmpty(currentId) && string.Equals(entryId, currentId, StringComparison.Ordinal);
}

/// <summary>
/// 로비 튜토리얼 단계별 바닥 경로 묶음(할 일 158 C5) — 경로 ID ↔ 할 일 111 의 <see cref="LobbyFloorArrowPath"/> 목록.
/// <see cref="SetPath"/> 가 그 ID 의 경로 오브젝트만 켜고 나머지는 끈다. 화살표 모양·셰이더·색은 111 그대로(바꾸지 않음).
/// <list type="bullet">
/// <item>전 피어 로컬 표시. 어떤 ID 를 켤지는 157 상태를 읽은 바인더가 <see cref="LobbyTutorialUI.SetPath"/> 로 넘긴다.</item>
/// <item>옛 3스텝 안내(<see cref="LobbyGuideSteps"/>)와 동시에 켜지지 않게, 경로를 켜는 동안 <see cref="legacyGuideRoots"/> 를 끈다(계획 Q1).
/// 경로를 전부 끄면(빈 ID) 되돌린다. 튜토리얼이 끝난 뒤 옛 안내를 아예 끌지는 157 흐름이 정한다.</item>
/// <item>켜질 때 전부 끈다. 회차 초기화·완료 정리는 <see cref="SetPath"/>(null).</item>
/// </list>
/// 상세: Docs/tech/lobby-tutorial-ui.md
/// </summary>
[DisallowMultipleComponent]
public sealed class LobbyTutorialPathSet : MonoBehaviour
{
    private const string Tag = "[LobbyTutorialPathSet]";

    [Serializable]
    public struct Entry
    {
        [Tooltip("경로 ID — LobbyTutorialPathIds 또는 157 계약 이름.")]
        public string id;

        [Tooltip("이 단계에서 켤 경로들(보통 하나).")]
        public List<LobbyFloorArrowPath> paths;
    }

    public static LobbyTutorialPathSet Active { get; private set; }

    [SerializeField] private List<Entry> entries = new List<Entry>();

    [Tooltip("튜토리얼 경로가 켜진 동안 끌 옛 안내 오브젝트(씬의 LobbyGuideSteps 루트). 비워도 된다.")]
    [SerializeField] private List<GameObject> legacyGuideRoots = new List<GameObject>();

    private readonly List<GameObject> _suppressed = new List<GameObject>();
    private string _current;

    public string Current => _current;
    public IReadOnlyList<Entry> Entries => entries;

    private void OnEnable()
    {
        if (Active != null && Active != this)
            Debug.LogWarning($"{Tag} LobbyTutorialPathSet 이 둘 이상 켜졌다 — 나중 것({name})을 Active 로 쓴다.", this);
        Active = this;
        SetPath(null);
    }

    // 씬 내림 중 다른 오브젝트 SetActive 는 오류가 나므로 여기선 상태만 지운다(경로는 보통 이 오브젝트의 자식이라 같이 꺼진다).
    private void OnDisable()
    {
        _current = null;
        _suppressed.Clear();
        if (Active == this)
            Active = null;
    }

    /// <summary>이 ID 의 경로만 켠다. null·빈 값 = 전부 끔. 모르는 ID 면 경고 후 전부 끔.</summary>
    public void SetPath(string pathId)
    {
        _current = string.IsNullOrEmpty(pathId) ? null : pathId;
        bool known = _current == null;

        // 같은 경로가 여러 ID 에 들어 있을 수 있어 켤 것을 먼저 모은다.
        var show = new HashSet<LobbyFloorArrowPath>();
        foreach (Entry entry in entries)
        {
            if (!LobbyTutorialPathRules.ShouldShow(entry.id, _current) || entry.paths == null)
                continue;

            known = true;
            foreach (LobbyFloorArrowPath path in entry.paths)
            {
                if (path != null)
                    show.Add(path);
            }
        }

        foreach (Entry entry in entries)
        {
            if (entry.paths == null)
                continue;

            foreach (LobbyFloorArrowPath path in entry.paths)
            {
                if (path == null)
                    continue;

                bool on = show.Contains(path);
                if (path.gameObject.activeSelf != on)
                    path.gameObject.SetActive(on);
            }
        }

        if (!known)
            Debug.LogWarning($"{Tag} 모르는 경로 ID '{pathId}' — 전부 끈다.", this);

        SetLegacySuppressed(show.Count > 0);
    }

    private void SetLegacySuppressed(bool suppressed)
    {
        if (suppressed)
        {
            foreach (GameObject root in legacyGuideRoots)
            {
                if (root != null && root != gameObject && root.activeSelf)
                {
                    root.SetActive(false);
                    _suppressed.Add(root);
                }
            }

            return;
        }

        foreach (GameObject root in _suppressed)
        {
            if (root != null)
                root.SetActive(true);
        }

        _suppressed.Clear();
    }
}
