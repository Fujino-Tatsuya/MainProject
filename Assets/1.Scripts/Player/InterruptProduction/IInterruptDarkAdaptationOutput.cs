using UnityEngine;

/// <summary>
/// 간파 성공 암순응(A2) 수동 실행자 창구(할 일 118) — 실제 구현은 <see cref="InterruptDarkAdaptation"/>.
/// 보는 사람(본인/다른 사람)과 칸 판단은 디렉터 채널이 하고, 여기는 받은 대로 튼다.
/// </summary>
public interface IInterruptDarkAdaptationOutput
{
    /// <summary>본인 화면이면 강한 강도(self), 아니면 약한 강도(others)로 튼다.</summary>
    void Play(bool viewerIsCaster, ulong casterClientId, Vector3 targetPosition);

    /// <summary>즉시 0(강제 리셋·세션 종료).</summary>
    void Stop();
}
