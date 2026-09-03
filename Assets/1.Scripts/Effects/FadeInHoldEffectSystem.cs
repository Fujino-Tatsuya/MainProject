using UnityEngine;

/// <summary>
/// 코드 구동 변색 연출(<see cref="FadeInHoldEffect"/>) 파트 드라이버. Shuriken · FloorArea에 이은 세 번째다.
///
/// <b>왜 별도 드라이버인가.</b> 이 파트의 시간축은 프리팹이 아니라 호출자가 정한다
/// (<see cref="IEffectSystem.Play"/>의 <c>duration</c>이 유일한 통로다). 파티클은 자기
/// Start Lifetime이 진실이라 그 인자를 무시하지만, 코드 구동 파트는 그 반대다.
///
/// 캐시를 만들지 않는 이유는 <see cref="FloorAreaEffectSystem"/>과 같다 — 프리팹당 컴포넌트 하나이고,
/// 이 드라이버의 메서드는 대출·반납·히트스톱 시점에만 불린다(매 프레임 도는 것은 컴포넌트 쪽이다).
/// </summary>
public class FadeInHoldEffectSystem : IEffectSystem
{
    public bool CanDrive(GameObject instance) => Find(instance) != null;

    /// <summary>
    /// <paramref name="duration"/> 동안 목표 색까지 변하기 시작한다.
    /// 0이 오면(= 수명이 정해지지 않은 루프 재생) 프리팹에 저작된 시간을 쓴다.
    /// </summary>
    public void Play(GameObject instance, float duration)
    {
        FadeInHoldEffect fade = Find(instance);
        if (fade == null) return;

        // 이전 대출자가 히트스톱 중에 반납됐을 수 있다. 배율을 먼저 되돌리지 않으면 멈춘 채로 시작한다.
        fade.SetPlayRate(1f);
        fade.BeginFade(duration);
    }

    /// <summary>
    /// 변색을 멈춘다. <paramref name="immediate"/>가 false여도 <b>색은 그대로 둔다</b> —
    /// 파티클의 "발생만 멈추고 살아 있는 입자는 수명대로"에 해당하는 것이 여기서는
    /// "변한 만큼은 남는다"이다. 실제 소멸은 매니저의 outro 타이머가 반납으로 처리한다.
    /// </summary>
    public void Stop(GameObject instance, bool immediate)
    {
        FadeInHoldEffect fade = Find(instance);
        if (fade == null) return;

        fade.StopFade();
        if (immediate) instance.SetActive(false);
    }

    public void SetPlayRate(GameObject instance, float rate)
    {
        FadeInHoldEffect fade = Find(instance);
        if (fade == null) return;

        fade.SetPlayRate(rate);
    }

    public void ResetForPool(GameObject instance)
    {
        FadeInHoldEffect fade = Find(instance);
        if (fade == null) return;

        fade.ResetForPool();
    }

    private static FadeInHoldEffect Find(GameObject instance)
        => instance.GetComponentInChildren<FadeInHoldEffect>(true);
}
