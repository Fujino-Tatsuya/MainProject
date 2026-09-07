using UnityEngine;

/// <summary>
/// 파편 버스트(<see cref="FragmentBurstEffect"/>) 파트 드라이버.
/// Shuriken · FloorArea · FadeInHold에 이은 네 번째다.
///
/// <b>왜 별도 드라이버인가.</b> 이 파트는 파티클이 아니라 <b>리지드바디 덩어리</b>다.
/// 재생 = 힘을 주는 것, 반납 = 제자리로 돌려놓는 것이라 파티클 드라이버의 어휘가 하나도 맞지 않는다.
///
/// 캐시를 만들지 않는 이유는 <see cref="FloorAreaEffectSystem"/>과 같다 — 프리팹당 컴포넌트 하나이고,
/// 이 드라이버의 메서드는 대출·반납·히트스톱 시점에만 불린다.
/// </summary>
public class FragmentBurstEffectSystem : IEffectSystem
{
    public bool CanDrive(GameObject instance) => Find(instance) != null;

    /// <summary>
    /// 터뜨린다. <paramref name="duration"/>은 쓰지 않는다 —
    /// 파편 수명은 매니저의 반납 타이머가 정하고, 여기서는 물리에 맡긴다.
    /// </summary>
    public void Play(GameObject instance, float duration)
    {
        FragmentBurstEffect burst = Find(instance);
        if (burst == null) return;

        // 이전 대출자가 히트스톱 중에 반납됐을 수 있다. 배율을 먼저 되돌리지 않으면 멈춘 채로 시작한다.
        burst.SetPlayRate(1f);
        burst.Burst();
    }

    /// <summary>
    /// 정지. <paramref name="immediate"/>가 false면 <b>파편을 그대로 날려보낸다</b> —
    /// 공중에서 멈춰 세우는 편이 더 이상하다. 실제 소멸은 매니저의 반납이 처리한다.
    /// </summary>
    public void Stop(GameObject instance, bool immediate)
    {
        FragmentBurstEffect burst = Find(instance);
        if (burst == null) return;

        burst.StopEmitting();
        if (immediate) instance.SetActive(false);
    }

    public void SetPlayRate(GameObject instance, float rate)
    {
        FragmentBurstEffect burst = Find(instance);
        if (burst == null) return;

        burst.SetPlayRate(rate);
    }

    public void ResetForPool(GameObject instance)
    {
        FragmentBurstEffect burst = Find(instance);
        if (burst == null) return;

        burst.ResetForPool();
    }

    private static FragmentBurstEffect Find(GameObject instance)
        => instance.GetComponentInChildren<FragmentBurstEffect>(true);
}
