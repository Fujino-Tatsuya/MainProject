/// <summary>
/// 보스 처치 연출 — 처치자 순서 단계 판정 순수 함수(255 후속 4, 테스트 <c>BossVictoryKillerSequenceTests</c>).
/// 타임라인 노드 4개가 각 단계 시각을 따로 정한다: '처치자 평타' 클립 끝(하던 동작 끊기) · '백대쉬 시작' · '스킬 연출 시작' · '느려짐 시작'(0 = 끔, 스킬 End 이벤트).
/// 런타임은 지금 단계보다 <see cref="Target"/> 이 뒤면 그 단계로 넘어간다(앞으로만 — 되감기는 ⏮ 로 다시 보기).
/// </summary>
public static class BossVictoryKillerSequence
{
    /// <summary>처치자 단계(순서대로 — 큰 값이 뒤).</summary>
    public enum Step
    {
        None,           // 연출 없음
        CurrentAction,  // 처치 순간 하던 동작을 ×배율로 이어 감
        Wait,           // 하던 동작이 끝났다(클립 끝·평타 클립 끝) — 백대쉬 전 대기 자세
        BackDash,       // 백대쉬(끝나면 '백대쉬 후 대기' 뒤 Idle — 스킬 연출 시작까지 그대로)
        Finisher,       // 처치 연출 스킬(표시 전용)
    }

    /// <summary>각 단계 시각(초, 처치 판정 뒤 글리치 시계).</summary>
    public struct Times
    {
        public bool HasActionClip;   // '처치자 평타' 클립이 있다 — 없으면 하던 동작은 자기 클립 끝까지
        public float ActionEnd;      // 평타 클립 끝 = 하던 동작을 끊는 시각
        public float BackDashStart;
        public float FinisherStart;
    }

    /// <summary>시각 <paramref name="t"/> 에 타임라인이 정하는 단계. 늦은 단계가 이긴다(스킬 시작이 백대쉬 시작보다 앞이면 백대쉬는 건너뜀).</summary>
    public static Step Target(in Times times, float t)
    {
        if (t >= times.FinisherStart) return Step.Finisher;
        if (t >= times.BackDashStart) return Step.BackDash;
        if (times.HasActionClip && t >= times.ActionEnd) return Step.Wait;
        return Step.CurrentAction;
    }

    /// <summary>'느려짐 시작' 마커로 처치자·보스가 End 후 배율이 되는가 — 마커가 켜져 있고(&gt; 0) 시각이 지났다. 꺼짐이면 스킬 End 이벤트가 정한다.</summary>
    public static bool SlowByMarker(float slowStart, float t) => slowStart > 0f && t >= slowStart;

    /// <summary>
    /// 스킬과 백대쉬가 같이 가는 프로필(<c>finisherWithBackDash</c>, 267 후속)에서 지금 백대쉬를 (이동만으로) 시작해야 하나 —
    /// 이미 스킬 단계이고, 이번 회차에 백대쉬를 아직 안 했고, 백대쉬 시작 시각이 지났다. 끈 프로필은 늘 false(옛 흐름: 스킬이 백대쉬를 끊거나 건너뜀).
    /// </summary>
    public static bool StartDashDuringFinisher(bool finisherWithBackDash, Step step, bool dashStarted, float backDashStart, float t) =>
        finisherWithBackDash && step == Step.Finisher && !dashStarted && t >= backDashStart;

    /// <summary>스킬 연출 시작 기본 = 백대쉬 시작 + 백대쉬 시간 + 백대쉬 후 대기(255 후속 3 까지의 체감 — Idle 복귀 직후 스킬).</summary>
    public static float DefaultFinisherStart(float backDashStart, float backDashDuration, float backDashIdleDelay) =>
        backDashStart + backDashDuration + backDashIdleDelay;
}
