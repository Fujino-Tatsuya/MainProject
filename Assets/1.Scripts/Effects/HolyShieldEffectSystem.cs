using UnityEngine;

/// <summary>
/// 코드 구동 배리어(<see cref="HolyShieldEffect"/>) 파트 드라이버.
/// Shuriken · FloorArea · FadeInHold · FragmentBurst 에 이은 다섯 번째다.
///
/// <b>왜 별도 드라이버인가.</b> 이 파트에는 ParticleSystem이 없다 — 드라이버가 없으면
/// 매니저는 "몰 드라이버가 없다"고 경고하고 위치만 잡은 채 아무것도 재생하지 않는다.
///
/// ⚠️ <b>같은 프리팹에 ParticleSystem을 넣지 말 것.</b> 그러면 ShurikenEffectSystem과 둘이
/// 손을 들어 <c>ResolveDriver</c>가 LogError를 낸다(프리팹 하나에 단일 기술 규칙).
/// 방어막의 입자 연출은 <c>FX_HolyShield_Motes</c>로 갈라 두고 엔트리의 parts에서 합친다.
/// </summary>
public class HolyShieldEffectSystem : IEffectSystem
{
    public bool CanDrive(GameObject instance) => Find(instance) != null;

    /// <summary>
    /// 배리어를 띄우기 시작한다. <paramref name="duration"/>이 0이면(= 수명을 세지 않는 루프 재생)
    /// 프리팹에 저작된 등장 시간을 쓴다.
    /// </summary>
    public void Play(GameObject instance, float duration)
    {
        HolyShieldEffect shield = Find(instance);
        if (shield == null) return;

        // 이전 대출자가 히트스톱 중에 반납됐을 수 있다. 배율을 먼저 되돌리지 않으면 멈춘 채로 시작한다.
        shield.SetPlayRate(1f);
        shield.BeginIntro(duration);
    }

    /// <summary>
    /// <paramref name="immediate"/>가 false면 <b>걷히는 연출을 시작만</b> 한다 —
    /// 파티클의 "발생만 멈추고 살아 있는 입자는 수명대로"에 해당한다.
    /// 실제 반납은 엔트리의 outroDuration을 세는 매니저 타이머가 한다.
    /// </summary>
    public void Stop(GameObject instance, bool immediate)
    {
        HolyShieldEffect shield = Find(instance);
        if (shield == null) return;

        if (immediate)
        {
            shield.ResetForPool();
            instance.SetActive(false);
            return;
        }

        shield.BeginOutro(0f);
    }

    public void SetPlayRate(GameObject instance, float rate) => Find(instance)?.SetPlayRate(rate);

    public void ResetForPool(GameObject instance) => Find(instance)?.ResetForPool();

    private static HolyShieldEffect Find(GameObject instance)
        => instance.GetComponentInChildren<HolyShieldEffect>(true);
}
