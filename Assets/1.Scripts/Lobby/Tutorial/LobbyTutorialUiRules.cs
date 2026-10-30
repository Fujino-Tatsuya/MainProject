using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>목표 패널 하단 진행 줄 종류(할 일 158). None = 구분선·진행 줄을 끈다.</summary>
public enum LobbyTutorialProgressKind
{
    None = 0,
    TrainingDone = 1, // 훈련 완료 n/N — 6항목을 마친 파티원 수
    Gather = 2,       // 집결 n/N — 활성 구역 안 인원
    Countdown = 3,    // 시작까지 n — 전투 시작 3·2·1
}

/// <summary>하단 진행 줄 입력. Countdown 은 <see cref="N"/> 만 쓴다.</summary>
[Serializable]
public struct LobbyTutorialProgress
{
    public LobbyTutorialProgressKind Kind;
    public int N;
    public int Total;

    public LobbyTutorialProgress(LobbyTutorialProgressKind kind, int n, int total)
    {
        Kind = kind;
        N = n;
        Total = total;
    }

    public static LobbyTutorialProgress None => default;
}

/// <summary>체크 항목 한 줄(자기 기록). <see cref="LabelKey"/> = Strings 키.</summary>
[Serializable]
public struct LobbyTutorialCheckItem
{
    public string LabelKey;
    public bool Done;

    public LobbyTutorialCheckItem(string labelKey, bool done)
    {
        LabelKey = labelKey;
        Done = done;
    }
}

/// <summary>목표 패널 한 장면 — 챕터·지시문·설명·체크·진행. 키는 모두 Strings 키(<see cref="LobbyTutorialUiStrings.Resolve"/>).</summary>
[Serializable]
public sealed class LobbyObjectiveView
{
    public string ChapterKey;
    public string ObjectiveKey;
    public string[] BodyKeys = Array.Empty<string>();
    public LobbyTutorialCheckItem[] Checklist = Array.Empty<LobbyTutorialCheckItem>();
    public LobbyTutorialProgress Progress;
}

/// <summary>대사 패널 뒤 전면 삽화(명세 23·24).</summary>
public enum LobbyTutorialBackground
{
    None = 0,
    Factory = 1,   // Scene_01 — 로봇 생산 시설
    Rebellion = 2, // Scene_02 — 기계 반란
}

/// <summary>
/// 로비 튜토리얼 UI 순수 규칙(할 일 158) — 패널 폭·진행 줄·체크 줄·태그 치환. MonoBehaviour·Loc 없음(EditMode 테스트).
/// 상세: Docs/tech/lobby-tutorial-ui.md
/// </summary>
public static class LobbyTutorialUiRules
{
    public const string TagTokenPrefix = "{tag:";

    /// <summary>설명 줄이 하나라도 있으면 넓은 폭(긴 훈련 설명 최대 680), 아니면 기본 폭(560). 짧은 목표에 폭을 늘리지 않는다.</summary>
    public static float PanelWidth(LobbyObjectiveView view, float basicWidth, float wideWidth) =>
        HasAny(view?.BodyKeys) ? wideWidth : basicWidth;

    /// <summary>진행 줄(과 구분선)을 보이는가. 분모가 0 인 집결·완료 수, 0 이하 카운트다운은 숨긴다.</summary>
    public static bool ShowProgress(LobbyTutorialProgress progress)
    {
        switch (progress.Kind)
        {
            case LobbyTutorialProgressKind.TrainingDone:
            case LobbyTutorialProgressKind.Gather:
                return progress.Total > 0;
            case LobbyTutorialProgressKind.Countdown:
                return progress.N > 0;
            default:
                return false;
        }
    }

    /// <summary>숫자만 금색으로 감싼 <c>{n}</c>·<c>{total}</c> 치환 인자.</summary>
    public static Dictionary<string, string> ProgressArgs(LobbyTutorialProgress progress, Color numberColor)
    {
        string open = "<color=#" + ColorUtility.ToHtmlStringRGB(numberColor) + ">";
        return new Dictionary<string, string>
        {
            ["n"] = open + Mathf.Max(0, progress.N) + "</color>",
            ["total"] = open + Mathf.Max(0, progress.Total) + "</color>",
        };
    }

    /// <summary>체크 한 줄. 완료 = 기존 글자색에서 어둡게 + 취소선(승인 결정 — 금색 체크 안 씀).</summary>
    public static string FormatCheckRow(string label, bool done, string pendingMark, string doneMark, Color doneColor)
    {
        if (!done)
            return pendingMark + " " + label;

        return "<color=#" + ColorUtility.ToHtmlStringRGBA(doneColor) + "><s>" + doneMark + " " + label + "</s></color>";
    }

    /// <summary>글자색을 어둡게(알파 유지). <paramref name="factor"/> 0 = 검정, 1 = 그대로.</summary>
    public static Color Darken(Color color, float factor)
    {
        factor = Mathf.Clamp01(factor);
        return new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
    }

    /// <summary>
    /// 문구 안 <c>{tag:Knockback}</c> 같은 자리표시자를 <paramref name="render"/> 결과로 바꾼다.
    /// render 가 null 을 주면(모르는 태그) 자리표시자를 그대로 둔다. 닫는 괄호가 없으면 거기서 멈춘다.
    /// </summary>
    public static string ReplaceTags(string text, Func<string, string> render)
    {
        if (string.IsNullOrEmpty(text) || render == null || text.IndexOf(TagTokenPrefix, StringComparison.Ordinal) < 0)
            return text ?? string.Empty;

        var builder = new StringBuilder(text.Length + 32);
        int index = 0;
        while (index < text.Length)
        {
            int start = text.IndexOf(TagTokenPrefix, index, StringComparison.Ordinal);
            if (start < 0)
                break;

            int close = text.IndexOf('}', start + TagTokenPrefix.Length);
            if (close < 0)
                break;

            builder.Append(text, index, start - index);
            string id = text.Substring(start + TagTokenPrefix.Length, close - start - TagTokenPrefix.Length).Trim();
            string rendered = render(id);
            builder.Append(rendered ?? text.Substring(start, close - start + 1));
            index = close + 1;
        }

        builder.Append(text, index, text.Length - index);
        return builder.ToString();
    }

    /// <summary>태그 하나를 TMP 리치텍스트로 — 글자색 + 반투명 배경 mark.</summary>
    public static string RenderTag(string label, Color32 text, Color32 background) =>
        "<mark=#" + ColorUtility.ToHtmlStringRGBA(background) + "><color=#" + ColorUtility.ToHtmlStringRGBA(text) + ">" + label + "</color></mark>";

    private static bool HasAny(string[] keys)
    {
        if (keys == null)
            return false;

        foreach (string key in keys)
        {
            if (!string.IsNullOrEmpty(key))
                return true;
        }

        return false;
    }
}

/// <summary>
/// 대사 중 목표 패널 숨김(명세 3.3) — 대사가 떠 있는 동안 + 대사가 끝난 뒤 <see cref="GraceSeconds"/> 동안 숨긴다.
/// 유예 안에 다음 대사가 오면 계속 숨김이라 연속 대사 사이에 이전 목표가 깜빡 나타나지 않는다. 시간은 호출자가 넣는다(unscaled).
/// </summary>
public sealed class LobbyDialogueObjectiveGate
{
    private bool _dialogueActive;
    private double _endedAt = double.NegativeInfinity;

    public LobbyDialogueObjectiveGate(float graceSeconds)
    {
        GraceSeconds = Mathf.Max(0f, graceSeconds);
    }

    public float GraceSeconds { get; set; }
    public bool DialogueActive => _dialogueActive;

    public void BeginDialogue() => _dialogueActive = true;

    public void EndDialogue(double now)
    {
        if (!_dialogueActive)
            return;

        _dialogueActive = false;
        _endedAt = now;
    }

    /// <summary>유예 없이 바로 푼다(씬 정리·회차 초기화).</summary>
    public void Reset()
    {
        _dialogueActive = false;
        _endedAt = double.NegativeInfinity;
    }

    public bool IsObjectiveHidden(double now) => _dialogueActive || now - _endedAt < GraceSeconds;
}
