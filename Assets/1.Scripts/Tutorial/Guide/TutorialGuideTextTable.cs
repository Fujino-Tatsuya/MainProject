using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>튜토리얼 문구 한 행 — id · 문구 · 지속시간(초, 자막용) · 선택 효과음.</summary>
[Serializable]
public sealed class TutorialGuideTextRow
{
    [SerializeField] private string id;
    [TextArea(1, 4), SerializeField] private string text;
    [Tooltip("자막 표시 시간(초). 0 이하면 기본값(TutorialSubtitleQueue.DefaultDurationSeconds).")]
    [SerializeField] private float durationSeconds;
    [Tooltip("이 문구가 자막으로 뜰 때 한 번 재생. 비워 두면 재생 안 함.")]
    [SerializeField] private AudioClip sfx;

    public TutorialGuideTextRow(string id, string text, float durationSeconds = 0f, AudioClip sfx = null)
    {
        this.id = id;
        this.text = text;
        this.durationSeconds = durationSeconds;
        this.sfx = sfx;
    }

    public string Id => id ?? string.Empty;
    public string Text => text ?? string.Empty;
    public float DurationSeconds => durationSeconds;
    public AudioClip Sfx => sfx;
}

/// <summary><see cref="TutorialGuideText"/> 가 읽는 문구 출처. 테스트는 사전 기반 가짜를 쓴다.</summary>
public interface ITutorialGuideTextSource
{
    bool TryGetRow(string id, out TutorialGuideTextRow row);
}

/// <summary>
/// 튜토리얼 안내 문구 테이블(ID → 문구). 에셋: <c>Assets/9.ScriptableObject/Tutorial/Guide/TutorialGuideTexts.asset</c>.
/// ⚠️ 데이터 테이블([DataTableText]) 연결은 일부러 하지 않았다(할 일 15 S6). xlsx Export 는 병합 뒤 메인이 하고,
///    기존 스킬 툴팁 검사가 낯선 <c>{…}</c> 자리표시자를 오류로 보므로 옮길 때 표기 변경이 필요하다
///    (특성 레인 선례). 자세한 건 Docs/tech/tutorial-guide-ui.md.
/// </summary>
[CreateAssetMenu(menuName = "Tutorial/Guide Text Table")]
public sealed class TutorialGuideTextTable : ScriptableObject, ITutorialGuideTextSource
{
    [SerializeField] private List<TutorialGuideTextRow> rows = new List<TutorialGuideTextRow>();

    [NonSerialized] private Dictionary<string, TutorialGuideTextRow> lookup;

    public IReadOnlyList<TutorialGuideTextRow> Rows => rows;

    public bool TryGetRow(string id, out TutorialGuideTextRow row)
    {
        row = null;
        if (string.IsNullOrEmpty(id))
            return false;

        if (lookup == null)
            BuildLookup();

        return lookup.TryGetValue(id, out row);
    }

    /// <summary>저작 메뉴 전용 — 없는 id 만 빈 행으로 덧붙인다. 이미 있는 문구는 건드리지 않는다.</summary>
    public bool AddRowIfMissing(string id, string text, float durationSeconds)
    {
        if (TryGetRow(id, out _))
            return false;

        rows.Add(new TutorialGuideTextRow(id, text, durationSeconds));
        lookup = null;
        return true;
    }

    private void OnValidate()
    {
        lookup = null;
    }

    private void BuildLookup()
    {
        lookup = new Dictionary<string, TutorialGuideTextRow>(StringComparer.Ordinal);
        foreach (TutorialGuideTextRow row in rows)
        {
            if (row == null || string.IsNullOrEmpty(row.Id))
                continue;

            if (lookup.ContainsKey(row.Id))
            {
                Debug.LogWarning($"[TutorialGuide] 문구 id 중복 — 첫 행만 쓴다: {row.Id}", this);
                continue;
            }

            lookup.Add(row.Id, row);
        }
    }
}
