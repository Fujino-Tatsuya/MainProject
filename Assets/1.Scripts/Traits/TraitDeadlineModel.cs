using System;

/// <summary>
/// 특성 선택 창의 교착 방지 게이지(할 일 15 후속) — Unity·NGO 비의존, EditMode 테스트 대상.
/// 입력은 바깥(복도 흐름)이 넣는 스냅샷이다: 표시 여부·자동 진행까지 남은 초·교착 방지 전체 초·선택 완료 수·전체 인원.
/// 스냅샷 사이에는 <see cref="Tick"/>(실제 시간)으로 남은 초를 로컬로 줄이고, 다음 <see cref="Sync"/> 가 서버 값으로 다시 맞춘다.
/// 이 모듈은 튜토리얼 디렉터·게이트를 모른다 — 어떤 게이트를 기다리는지 판단은 넣는 쪽 몫.
/// </summary>
public sealed class TraitDeadlineModel
{
    /// <summary>이 초 이하로 남으면 강조색(게이지·텍스트).</summary>
    public const float UrgentSeconds = 10f;

    public const string SecondsFormat = "{0}초 뒤 자동 선택";
    public const string DoneFormat = "선택 완료 {0}/{1}";

    public bool Visible { get; private set; }

    /// <summary>자동 진행까지 남은 초(0 이상).</summary>
    public float Remaining { get; private set; }

    /// <summary>교착 방지 전체 초(게이지 분모).</summary>
    public float Total { get; private set; }

    public int Done { get; private set; }
    public int Count { get; private set; }

    /// <summary>게이지 비율 0~1 = 남은 / 전체. 전체가 0 이하면 0.</summary>
    public float Fill => Total > 0f ? Clamp01(Remaining / Total) : 0f;

    public bool IsUrgent => Visible && Remaining <= UrgentSeconds;

    /// <summary>표시용 남은 초 — 올림(0.2초 남음 = "1초").</summary>
    public int DisplaySeconds => (int)Math.Ceiling(Remaining);

    /// <summary>서버 값으로 다시 맞춘다. 숨김이면 나머지 값은 0 으로 지운다.</summary>
    public void Sync(bool visible, float remaining, float total, int done, int count)
    {
        Visible = visible;
        if (!visible)
        {
            Remaining = Total = 0f;
            Done = Count = 0;
            return;
        }

        Remaining = Math.Max(0f, remaining);
        Total = Math.Max(0f, total);
        Done = Math.Max(0, done);
        Count = Math.Max(0, count);
    }

    /// <summary>다음 동기화까지 로컬로 줄인다(0 에서 멈춤 — 만료 처리는 서버가 한다).</summary>
    public void Tick(float deltaSeconds)
    {
        if (!Visible || deltaSeconds <= 0f)
            return;
        Remaining = Math.Max(0f, Remaining - deltaSeconds);
    }

    public string FormatSeconds() => string.Format(SecondsFormat, DisplaySeconds);

    public string FormatDone() => string.Format(DoneFormat, Done, Count);

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
}
