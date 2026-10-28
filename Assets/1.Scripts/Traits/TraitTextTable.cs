using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 특성 선택 UI 문구 테이블(할 일 15 T3) — 문구 ID 별 문자열을 에셋 한 곳에 모은다.
/// 에셋이 없거나 그 ID 행이 없으면 <see cref="TraitTexts"/> 상수 기본값을 쓴다.
///
/// 데이터 테이블: development 병합 뒤 Main 에서 클래스에 [DataTableSheet] · <see cref="TraitTextEntry"/> 의 두 필드에
/// [DataTableText] 를 달고 병합 Export 한다. 지금 달지 않는 이유 —
/// ① 새 시트라 Export 전에는 xlsx 에 없다(규칙상 Export 는 병합 뒤 Main 에서만).
/// ② 테이블 문자열은 SkillTooltipFormatter 자리표시자 검증을 거쳐 <c>{location}</c> 이 "없는 필드" 오류가 된다 —
///    그때 <see cref="TraitTexts.LocationToken"/> 표기를 검증과 겹치지 않게 바꿔야 한다.
/// </summary>
[CreateAssetMenu(fileName = "TraitTexts", menuName = "Tutorial/Trait Texts")]
public sealed class TraitTextTable : ScriptableObject
{
    [Tooltip("문구 ID 별 문자열. 행 추가·순서는 인스펙터에서, 문자열은 나중에 데이터 테이블에서 관리한다.")]
    [SerializeField] private TraitTextEntry[] entries = CreateDefaultEntries();

    public IReadOnlyList<TraitTextEntry> Entries => entries ?? Array.Empty<TraitTextEntry>();

    /// <summary>모든 문구 ID 를 상수 기본값으로 채운 행 — 새 에셋의 초기값.</summary>
    public static TraitTextEntry[] CreateDefaultEntries()
    {
        var result = new TraitTextEntry[TraitTexts.AllIds.Length];
        for (int i = 0; i < result.Length; i++)
            result[i] = new TraitTextEntry(TraitTexts.AllIds[i], TraitTexts.Get(TraitTexts.AllIds[i]));
        return result;
    }

    /// <summary>그 ID 의 문구. 행이 없으면 false(빈 문자열 행은 빈 문구로 true).</summary>
    public bool TryGet(string id, out string text)
    {
        if (entries != null)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i] != null && entries[i].Id == id)
                {
                    text = entries[i].Text ?? string.Empty;
                    return true;
                }
            }
        }

        text = null;
        return false;
    }

    /// <summary>테이블(없으면 null) → 상수 기본값 순으로 찾는다. 모르는 ID 는 빈 문자열.</summary>
    public static string Resolve(TraitTextTable table, string id)
    {
        return table != null && table.TryGet(id, out string text) ? text : TraitTexts.Get(id);
    }
}

/// <summary>문구 한 줄 — ID 는 코드가 찾는 키(<see cref="TraitTexts"/> 의 *Id 상수), 문자열은 기획 문구.</summary>
[Serializable]
public sealed class TraitTextEntry
{
    // 병합 후 [DataTableText] 적용 — 이유는 TraitTextTable 주석.
    [Tooltip("코드가 찾는 문구 ID(TUT_C1_TRAIT_TITLE 등)")]
    [SerializeField] private string id = string.Empty;

    // 병합 후 [DataTableText] 적용.
    [Tooltip("표시 문구. TMP 리치 텍스트 사용 가능. TUT_C1_TRAIT_PICKED 의 {location} 은 다시 보기 위치 문구로 바뀐다.")]
    [SerializeField, TextArea(2, 5)] private string text = string.Empty;

    public TraitTextEntry()
    {
    }

    public TraitTextEntry(string id, string text)
    {
        this.id = id;
        this.text = text;
    }

    public string Id => id;
    public string Text => text;
}
