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
/// 튜토리얼 안내 문구 테이블(ID → 문구·지속시간·효과음). 에셋: <see cref="AssetPath"/>.
/// 화면 문구는 Strings 시트 일반 키 <c>Tutorial.Guide.&lt;id&gt;</c> 가 우선이고, 이 SO 문구는 시트에 행이 없을 때의
/// 폴백 + Export Template 의 ko 원문이다(할 일 86 T6a — <see cref="TutorialGuideTextSource"/>, Editor <c>TutorialGuideStringKeys</c>).
/// 지속시간·효과음은 이 SO 가 원본이다. 일반 키라 <c>{Q}</c>·<c>{mm:ss}</c> 표기를 바꿀 필요가 없다.
/// 자세한 건 Docs/tech/tutorial-guide-ui.md.
/// </summary>
[CreateAssetMenu(menuName = "Tutorial/Guide Text Table")]
public sealed class TutorialGuideTextTable : ScriptableObject, ITutorialGuideTextSource
{
    public const string AssetPath = "Assets/9.ScriptableObject/Tutorial/Guide/TutorialGuideTexts.asset";

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

    /// <summary>
    /// 저작 메뉴 전용 — 문구 개정(v8 등)을 적용한다. 행이 있으면 문구·지속시간만 바꾸고(효과음 유지), 없으면 덧붙인다.
    /// 이미 같은 값이면 false.
    /// </summary>
    public bool SetRowText(string id, string text, float durationSeconds)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        for (int i = 0; i < rows.Count; i++)
        {
            TutorialGuideTextRow row = rows[i];
            if (row == null || row.Id != id)
                continue;
            if (row.Text == (text ?? string.Empty) && Mathf.Approximately(row.DurationSeconds, durationSeconds))
                return false;

            rows[i] = new TutorialGuideTextRow(id, text, durationSeconds, row.Sfx);
            lookup = null;
            return true;
        }

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
