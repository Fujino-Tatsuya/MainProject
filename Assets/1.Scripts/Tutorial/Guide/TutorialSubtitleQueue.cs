using System.Collections.Generic;
using UnityEngine;

/// <summary>화면에 띄울 자막 한 줄 — 치환까지 끝난 문구와 지속시간.</summary>
public readonly struct TutorialSubtitle
{
    public TutorialSubtitle(string id, string text, float durationSeconds, AudioClip sfx = null)
    {
        Id = id ?? string.Empty;
        Text = text ?? string.Empty;
        DurationSeconds = durationSeconds > 0f ? durationSeconds : TutorialSubtitleQueue.DefaultDurationSeconds;
        Sfx = sfx;
    }

    public string Id { get; }
    public string Text { get; }
    public float DurationSeconds { get; }
    public AudioClip Sfx { get; }
}

/// <summary>
/// 자막 큐(순수 로직). 한 번에 한 줄만 보이고, 겹치면 들어온 순서대로 이어서 띄운다.
/// 시간은 호출자가 <see cref="Advance"/> 로 민다 — 뷰는 슬로우모션에 안 늘어나게 unscaled 시간을 넘긴다.
/// </summary>
public sealed class TutorialSubtitleQueue
{
    public const float DefaultDurationSeconds = 6f;

    private readonly Queue<TutorialSubtitle> pending = new Queue<TutorialSubtitle>();
    private TutorialSubtitle current;

    public bool HasCurrent { get; private set; }
    public TutorialSubtitle Current => current;
    /// <summary>현재 자막이 뜬 뒤 지난 시간(초).</summary>
    public float Elapsed { get; private set; }
    public float Remaining => HasCurrent ? Mathf.Max(0f, current.DurationSeconds - Elapsed) : 0f;
    public int PendingCount => pending.Count;

    /// <summary>비어 있으면 바로 현재 자막이 된다(반환 true = 현재 자막이 바뀜).</summary>
    public bool Enqueue(TutorialSubtitle subtitle)
    {
        if (HasCurrent)
        {
            pending.Enqueue(subtitle);
            return false;
        }

        SetCurrent(subtitle, 0f);
        return true;
    }

    /// <summary>시간을 민다. 만료된 자막은 다음 것으로 넘기고 남은 시간을 이어 쓴다(반환 true = 현재 자막이 바뀜).</summary>
    public bool Advance(float deltaSeconds)
    {
        if (!HasCurrent || deltaSeconds <= 0f)
            return false;

        Elapsed += deltaSeconds;
        bool changed = false;
        while (HasCurrent && Elapsed >= current.DurationSeconds)
        {
            float carry = Elapsed - current.DurationSeconds;
            changed = true;
            if (pending.Count > 0)
            {
                SetCurrent(pending.Dequeue(), carry);
            }
            else
            {
                HasCurrent = false;
                current = default;
                Elapsed = 0f;
            }
        }

        return changed;
    }

    public void Clear()
    {
        pending.Clear();
        HasCurrent = false;
        current = default;
        Elapsed = 0f;
    }

    private void SetCurrent(TutorialSubtitle subtitle, float elapsed)
    {
        current = subtitle;
        HasCurrent = true;
        Elapsed = elapsed;
    }
}
