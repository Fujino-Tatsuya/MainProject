using UnityEngine;

/// <summary>
/// 코드 구동 장판(<see cref="FloorAreaEffect"/>) 파트 드라이버. Shuriken에 이은 두 번째 구현체.
///
/// <b>왜 파티클이 아닌가.</b> No.23 JumpAttack의 예고 장판은 연출이 아니라 <b>판정 예고</b>다.
/// 도달 크기가 착지 데미지 반경과 같은 수여야 하고(<c>JumpController</c>의 <c>_floorRadius</c>),
/// 성장 시간은 서버가 점프 체공시간으로 매번 계산한다. 파티클의 Size over Lifetime은 그 둘 중
/// 어느 쪽도 정확히 보장하지 못한다 — startSize × 커브 × 렌더러 정렬을 거치면 월드 반경이 근사가 되고,
/// 시간축은 프리팹의 Start Lifetime에 묶인다. 예고의 존재 이유가 정확성인데 그걸 근사로 만드는 셈이다.
///
/// <b>캐시를 만들지 않는 이유.</b> Shuriken은 프리팹 하나에 파티클이 수십 개라 캐시가 필요했지만
/// 장판은 컴포넌트 하나다. 게다가 이 드라이버의 메서드는 대출·반납·히트스톱 시점에만 불린다
/// (매 프레임 도는 것은 <see cref="FloorAreaEffect.Update"/> 쪽이다).
/// </summary>
public class FloorAreaEffectSystem : IEffectSystem
{
    public bool CanDrive(GameObject instance) => Find(instance) != null;

    /// <summary>
    /// <paramref name="duration"/> 동안 <b>지금의 localScale까지</b> 자라기 시작한다.
    /// 목표 크기를 인자로 받지 않는 것은 풀이 대출 시점에
    /// <c>originalScale × 배율</c>로 이미 확정해 두었기 때문이다 — 같은 수를 두 경로로 받으면 어긋난다.
    /// </summary>
    public void Play(GameObject instance, float duration)
    {
        FloorAreaEffect area = Find(instance);
        if (area == null) return;

        // 이전 대출자가 히트스톱 중에 반납됐을 수 있다. 배율을 먼저 되돌리지 않으면 멈춘 채로 시작한다.
        area.SetPlayRate(1f);
        area.BeginPooledGrow(duration);
    }

    /// <summary>
    /// 성장을 멈춘다. <paramref name="immediate"/>가 false여도 <b>크기는 그대로 둔다</b> —
    /// 파티클의 "발생만 멈추고 살아 있는 입자는 수명대로"에 해당하는 것이 장판에서는
    /// "자란 만큼은 남는다"이기 때문이다. 실제 소멸은 매니저의 타이머가 반납으로 처리한다.
    /// </summary>
    public void Stop(GameObject instance, bool immediate)
    {
        FloorAreaEffect area = Find(instance);
        if (area == null) return;

        area.StopGrow();
        if (immediate) instance.SetActive(false);
    }

    public void SetPlayRate(GameObject instance, float rate)
    {
        FloorAreaEffect area = Find(instance);
        if (area == null) return;

        area.SetPlayRate(rate);
    }

    public void ResetForPool(GameObject instance)
    {
        FloorAreaEffect area = Find(instance);
        if (area == null) return;

        area.ResetForPool();
    }

    private static FloorAreaEffect Find(GameObject instance)
        => instance.GetComponentInChildren<FloorAreaEffect>(true);
}
