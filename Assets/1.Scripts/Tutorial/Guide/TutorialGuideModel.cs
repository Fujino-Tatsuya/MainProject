using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 체크리스트 한 줄. <see cref="Label"/> 이 문구 테이블 id 면 그 문구(목표 args 로 치환)를, 아니면 글자 그대로 쓴다.
/// </summary>
[Serializable]
public struct TutorialChecklistItem
{
    public TutorialChecklistItem(string label, bool done)
    {
        Label = label;
        Done = done;
    }

    public string Label;
    public bool Done;
}

/// <summary>진행도(예 0/9). Total ≤ 0 이면 표시하지 않는다.</summary>
[Serializable]
public struct TutorialProgress
{
    public TutorialProgress(int current, int total)
    {
        Current = current;
        Total = total;
    }

    public int Current;
    public int Total;

    public bool HasValue => Total > 0;
    public override string ToString() => HasValue ? $"{Mathf.Clamp(Current, 0, Total)}/{Total}" : string.Empty;
}

/// <summary>동료 대기 줄(TUT_WAIT_PARTY) 상태.</summary>
[Serializable]
public struct TutorialWaitState
{
    public TutorialWaitState(int ready, int total, float secondsLeft)
    {
        Active = true;
        Ready = ready;
        Total = total;
        SecondsLeft = secondsLeft;
    }

    public bool Active;
    public int Ready;
    public int Total;
    /// <summary>자동 진행까지 남은 초. 음수 = 자동 진행 없음(G1 — 카운트다운 없이 동료 n/total 만, TUT_WAIT_PARTY_NOAUTO).</summary>
    public float SecondsLeft;

    public bool HasCountdown => SecondsLeft >= 0f;
}

public enum TutorialHighlightStyle
{
    Pulse,
    Arrow,
    PulseAndArrow,
}

/// <summary>
/// 강조 대상 하나. CombatHUD 칸(<see cref="HudTarget"/>) 또는 Transform(RectTransform = UI, 그 밖 = 월드 위치) 중 하나.
/// </summary>
public readonly struct TutorialHighlightRequest
{
    public TutorialHighlightRequest(TutorialHudTarget hudTarget, TutorialHighlightStyle style)
    {
        HudTarget = hudTarget;
        Target = null;
        Style = style;
    }

    public TutorialHighlightRequest(Transform target, TutorialHighlightStyle style)
    {
        HudTarget = TutorialHudTarget.None;
        Target = target;
        Style = style;
    }

    public TutorialHudTarget HudTarget { get; }
    public Transform Target { get; }
    public TutorialHighlightStyle Style { get; }

    public bool SameTarget(in TutorialHighlightRequest other) =>
        HudTarget != TutorialHudTarget.None
            ? HudTarget == other.HudTarget
            : other.HudTarget == TutorialHudTarget.None && Target == other.Target;
}

/// <summary>
/// 늦게 로드된 클라가 다시 그릴 상태(<see cref="TutorialGuideUI.RebuildFromState"/>).
/// 지나간 자막은 담지 않는다 — 목표·체크리스트·진행도·대기 줄·강조만.
/// </summary>
public struct TutorialGuideState
{
    public string ObjectiveId;
    public IReadOnlyDictionary<string, string> ObjectiveArgs;
    public TutorialChecklistItem[] Checklist;
    public TutorialProgress Progress;
    public TutorialWaitState Wait;
    public TutorialHighlightRequest[] Highlights;
}

/// <summary>
/// 튜토리얼 안내 UI 의 상태(순수 로직, EditMode 테스트 대상). 그리기는 <see cref="TutorialGuideUI"/> 가 한다.
/// </summary>
public sealed class TutorialGuideModel
{
    private static readonly TutorialChecklistItem[] NoChecklist = Array.Empty<TutorialChecklistItem>();

    private readonly List<TutorialHighlightRequest> highlights = new List<TutorialHighlightRequest>();
    private TutorialChecklistItem[] checklist = NoChecklist;

    public TutorialSubtitleQueue Subtitles { get; } = new TutorialSubtitleQueue();

    public string ObjectiveId { get; private set; }
    public IReadOnlyDictionary<string, string> ObjectiveArgs { get; private set; }
    public IReadOnlyList<TutorialChecklistItem> Checklist => checklist;
    public TutorialProgress Progress { get; private set; }
    public TutorialWaitState Wait { get; private set; }
    public IReadOnlyList<TutorialHighlightRequest> Highlights => highlights;

    public bool HasObjective => !string.IsNullOrEmpty(ObjectiveId);

    /// <summary>반환 true = 목표 id 가 바뀌었다(단계 전환 — 효과음 훅).</summary>
    public bool SetObjective(
        string id, IReadOnlyDictionary<string, string> args, TutorialChecklistItem[] items, TutorialProgress progress)
    {
        bool stepChanged = !string.Equals(ObjectiveId, id, StringComparison.Ordinal);
        ObjectiveId = id;
        ObjectiveArgs = args;
        checklist = items != null && items.Length > 0 ? (TutorialChecklistItem[])items.Clone() : NoChecklist;
        Progress = progress;
        return stepChanged;
    }

    public void SetWait(int ready, int total, float secondsLeft) => Wait = new TutorialWaitState(ready, total, secondsLeft);

    public void ClearWait() => Wait = default;

    /// <summary>
    /// 대기 카운트다운을 로컬에서 줄인다(0 아래로 안 간다). 서버 값은 SetWait 재호출로 다시 맞춘다.
    /// 음수(= 자동 진행 없음, <see cref="TutorialWaitState.HasCountdown"/> false)는 그대로 둔다.
    /// </summary>
    public void TickWait(float deltaSeconds)
    {
        if (!Wait.Active || !Wait.HasCountdown || deltaSeconds <= 0f)
            return;

        TutorialWaitState wait = Wait;
        wait.SecondsLeft = Mathf.Max(0f, wait.SecondsLeft - deltaSeconds);
        Wait = wait;
    }

    /// <summary>같은 대상이 이미 있으면 스타일만 바꾼다.</summary>
    public void AddHighlight(TutorialHighlightRequest request)
    {
        RemoveHighlight(request);
        highlights.Add(request);
    }

    public bool RemoveHighlight(TutorialHighlightRequest request)
    {
        for (int i = 0; i < highlights.Count; i++)
        {
            if (!highlights[i].SameTarget(request))
                continue;

            highlights.RemoveAt(i);
            return true;
        }

        return false;
    }

    public void ClearHighlights() => highlights.Clear();

    /// <summary>현재 상태를 통째로 바꾼다. 자막은 복원하지 않고 비운다.</summary>
    public void Rebuild(TutorialGuideState state)
    {
        Subtitles.Clear();
        SetObjective(state.ObjectiveId, state.ObjectiveArgs, state.Checklist, state.Progress);
        Wait = state.Wait;

        highlights.Clear();
        if (state.Highlights != null)
        {
            foreach (TutorialHighlightRequest request in state.Highlights)
                AddHighlight(request);
        }
    }

    public TutorialGuideState Capture() => new TutorialGuideState
    {
        ObjectiveId = ObjectiveId,
        ObjectiveArgs = ObjectiveArgs,
        Checklist = (TutorialChecklistItem[])checklist.Clone(),
        Progress = Progress,
        Wait = Wait,
        Highlights = highlights.ToArray(),
    };

    public void Clear()
    {
        Subtitles.Clear();
        ObjectiveId = null;
        ObjectiveArgs = null;
        checklist = NoChecklist;
        Progress = default;
        Wait = default;
        highlights.Clear();
    }
}
