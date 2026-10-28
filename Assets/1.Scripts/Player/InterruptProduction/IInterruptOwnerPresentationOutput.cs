using UnityEngine;

/// <summary>
/// 오너 암전 강도 출력(할 일 132) — 0 = 없음, 1 = 칸에 정한 색·불투명도 그대로. 오너 화면에만 붙는다.
/// 실제 렌더(마스크 암전)는 단계 3 이 붙인다 — 그 전엔 <see cref="InterruptOwnerPresentationLogOutput"/>.
/// </summary>
public interface IInterruptOwnerDimOutput
{
    void SetOwnerDim(float intensity);
}

/// <summary>
/// 빛 모임 시작/정지 출력(할 일 132) — 강 시도(성공 예측) 입력 순간 시작, Hit 이벤트·예측 빗나감·실패에 정지.
/// 실제 VFX(캐릭터 소켓)는 단계 4 가 붙인다. <paramref name="skill"/> = 입력한 간파 스킬(소켓 찾기용, 테스트에선 null).
/// </summary>
public interface IInterruptLightGatherOutput
{
    void StartLightGather(PlayerInterruptSkillBase skill);
    void StopLightGather();
}

/// <summary>단계 3·4 전 임시 출력 — 바뀔 때만 에디터 로그(빌드에선 호출째 빠진다).</summary>
public sealed class InterruptOwnerPresentationLogOutput : IInterruptOwnerDimOutput, IInterruptLightGatherOutput
{
    private float _lastLoggedDim = -1f;

    public void SetOwnerDim(float intensity)
    {
        // 램프 중 매 프레임 찍지 않는다 — 0·1 도달만 남긴다.
        bool edge = Mathf.Approximately(intensity, 0f) || Mathf.Approximately(intensity, 1f);
        if (!edge || Mathf.Approximately(intensity, _lastLoggedDim))
            return;

        _lastLoggedDim = intensity;
        Edit.Log($"[InterruptOwner] 오너 암전 강도 {intensity:F2}");
    }

    public void StartLightGather(PlayerInterruptSkillBase skill) =>
        Edit.Log($"[InterruptOwner] 빛 모임 시작 — {(skill != null ? skill.name : "(스킬 없음)")}");

    public void StopLightGather() => Edit.Log("[InterruptOwner] 빛 모임 정지");
}
