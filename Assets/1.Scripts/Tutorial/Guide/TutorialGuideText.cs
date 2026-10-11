using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>문구 변형을 고를 로컬 플레이어 캐릭터. None = 모름(기본 id 만 쓴다).</summary>
public enum TutorialCharacter
{
    None,
    Paladin,
    Gunner,
    Assassin,
}

/// <summary>문구 조회 결과. Empty 는 행은 있지만 문구가 비었다(메인 S7~S10 에서 채울 자리).</summary>
public enum TutorialGuideTextStatus
{
    Found,
    Empty,
    Missing,
}

/// <summary>
/// 튜토리얼 문구 규칙(순수 함수, EditMode 테스트 대상) — 캐릭터 변형 선택 · 자리표시자 치환 · 대기 시간 포맷.
/// </summary>
public static class TutorialGuideText
{
    // {Q} {mm:ss} {스킬명} 처럼 중괄호 안에 중괄호가 없는 토큰. 콜론·한글 키를 허용한다.
    private static readonly Regex Placeholder = new Regex(@"\{([^{}]+)\}", RegexOptions.Compiled);

    public static string Suffix(TutorialCharacter character) => character switch
    {
        TutorialCharacter.Paladin => "_PALADIN",
        TutorialCharacter.Gunner => "_GUNNER",
        TutorialCharacter.Assassin => "_ASSASSIN",
        _ => string.Empty,
    };

    /// <summary>
    /// <c>id + _캐릭터</c> 행이 있고 문구가 비어 있지 않으면 그 id, 아니면 기본 id.
    /// 빈 변형 행은 "아직 안 채운 자리" 로 보고 기본 문구로 내려간다.
    /// </summary>
    public static string SelectVariantId(ITutorialGuideTextSource source, string baseId, TutorialCharacter character)
    {
        if (source == null || string.IsNullOrEmpty(baseId) || character == TutorialCharacter.None)
            return baseId;

        string variantId = baseId + Suffix(character);
        return source.TryGetRow(variantId, out TutorialGuideTextRow row) && !string.IsNullOrWhiteSpace(row.Text)
            ? variantId
            : baseId;
    }

    /// <summary>변형을 고른 뒤 행을 찾는다. Found 일 때만 문구를 띄운다.</summary>
    public static TutorialGuideTextStatus Resolve(
        ITutorialGuideTextSource source, string baseId, TutorialCharacter character, out TutorialGuideTextRow row)
    {
        row = null;
        if (source == null)
            return TutorialGuideTextStatus.Missing;

        string id = SelectVariantId(source, baseId, character);
        if (!source.TryGetRow(id, out row))
            return TutorialGuideTextStatus.Missing;

        return string.IsNullOrWhiteSpace(row.Text) ? TutorialGuideTextStatus.Empty : TutorialGuideTextStatus.Found;
    }

    /// <summary>
    /// <c>{키}</c> 를 args 값으로 바꾼다. args 에 없는 자리표시자는 그대로 남기고 경고 없이 통과한다.
    /// </summary>
    public static string Fill(string template, IReadOnlyDictionary<string, string> args)
    {
        if (string.IsNullOrEmpty(template) || args == null || args.Count == 0)
            return template ?? string.Empty;

        return Placeholder.Replace(template, match =>
            args.TryGetValue(match.Groups[1].Value, out string value) && value != null ? value : match.Value);
    }

    /// <summary>
    /// 남은 초 → <c>mm:ss</c>. 음수·NaN 은 0, 소수는 올림(1.2초 남음 = 00:02 — 0 이 보이면 이미 끝난 것).
    /// 분은 두 자리 아래로 줄이지 않고 그대로 늘어난다(100분 = 100:00).
    /// </summary>
    public static string FormatWait(float secondsLeft)
    {
        int total = float.IsNaN(secondsLeft) || secondsLeft <= 0f ? 0 : Mathf.CeilToInt(secondsLeft);
        return $"{total / 60:00}:{total % 60:00}";
    }
}
