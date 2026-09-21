using System.Collections.Generic;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 방패 계열 연출 중 <b>서버에서만 알 수 있는 시점</b>을 전 피어로 퍼뜨리는 창구.
/// 보호막(수호자의 의지)의 등장·소멸·피격 파문과, 단죄의 방패 적중 충격파를 맡는다.
/// 소켓을 들고 있고 스킬은 시점만 알려 준다.
///
/// <b>왜 스킬이 직접 안 하고 여기로 오나.</b> <see cref="FirstMeleeSubSkill"/>의 부모인
/// <c>PlayerSkillBase</c>는 <b>MonoBehaviour</b>라 RPC를 달 수 없다. 그런데 보호막이 끝나는
/// 지점(만료 코루틴)은 <b>서버 전용</b>이라, 거기서 바로 Stop을 부르면 이 레포가 이미 여러 번 겪은
/// "호스트에서만 이펙트가 보인다" 버그가 그대로 재현된다. 그래서 NetworkObject와 같은
/// 오브젝트에 얹힌 이 NetworkBehaviour가 종료만 전 피어로 퍼뜨린다.
///
/// <b>시작에는 RPC가 없다.</b> <c>PlayerSkillController</c>의 재생 경로가 이미 전 피어에서 돌기 때문이다
/// (서버는 직접 호출, 클라는 <c>PlaySkillClientRpc</c>). 그 경로에서 불리는 <see cref="PlayLocal"/>은
/// 각 피어가 자기 몫을 켜면 끝이라 트래픽이 0이다.
///
/// <b>수명이 스킬보다 길다.</b> 스킬 자체는 0.5초(maxActiveDuration)만에 끝나지만 보호막은 5초 간다.
/// 그래서 배리어를 <c>OnEnd</c>에 묶으면 안 되고, 소켓의 safetyTimeout도
/// <b>보호막 지속시간보다 길어야</b> 한다 — 짧으면 배리어가 도중에 강제 회수된다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerShieldVfx : BaseNetworkBehaviour
{
    [Header("연출")]
    [Tooltip("보호막이 떠 있는 동안 재생할 루프. 프리팹의 'HolyShield' EffectSocketPlayer 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer barrierLoop;

    [Tooltip("보호막이 피해로 깨질 때 한 번 재생할 파괴 연출. 프리팹의 'HolyShield_Break' 를 물린다.\n" +
             "비워두면 깨지는 순간에도 조용히 걷히기만 한다")]
    [SerializeField] private EffectSocketPlayer breakBurst;

    [Tooltip("공격 지점이 배리어 중심에서 이만큼도 떨어져 있지 않으면 파문을 건너뛴다(미터).\n" +
             "장판 한가운데서 맞으면 방향이 서지 않는다 — 엉뚱한 쪽에 띄우는 것보다 안 띄우는 게 낫다")]
    [SerializeField, Min(0f)] private float minHitDistance = 0.15f;

    [Tooltip("단죄의 방패(우클릭)가 적중했을 때 한 번 터뜨릴 충격파. 프리팹의 'ShieldWave' 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer interruptWave;

    // 풀 인스턴스를 담아 둘 수 없으므로(반납되면 다른 연출에 재대출된다) 매번 다시 받는다.
    // 리스트만 재사용해 할당을 없앤다.
    private readonly List<GameObject> _instances = new List<GameObject>();

    /// <summary>
    /// [전 피어] 배리어를 띄운다. 스킬 재생 경로(OnClientPlay)에서 불리므로 RPC가 필요 없다.
    /// 재시전이면 <c>Play</c>가 먼저 회수하고 다시 시작하므로 두 겹으로 겹치지 않는다.
    /// </summary>
    public void PlayLocal() => barrierLoop?.Play();

    /// <summary>
    /// [서버] 보호막이 끝났다. 전 피어에 알려 배리어를 걷는다.
    /// </summary>
    /// <param name="broken">피해로 소진돼 깨진 것이면 true, 시간 만료·사망이면 false.</param>
    public void ServerEnd(bool broken)
    {
        // 오프라인(싱글 테스트·VFXScene)에서는 스폰되지 않아 RPC를 부르면 예외가 난다.
        if (!IsNetworkActive)
        {
            EndLocal(broken);
            return;
        }

        if (!IsServer) return;

        EndShieldVfxRpc(broken);
    }

    /// <summary>
    /// [서버] 보호막이 공격을 받아냈다. 맞은 방향에 파문을 띄우라고 전 피어에 알린다.
    ///
    /// <b>여기만 RPC 가 불가피하다.</b> 공격 판정은 전부 서버 전용 스코프 안에서 돌아
    /// (<c>MonsterMeleeAttack</c>·<c>AreaZone</c>·<c>BossBomb</c> 모두 <c>if (!IsServer) return;</c>),
    /// <see cref="AttackHitContext"/>가 리모트 클라에는 아예 도착하지 않는다. 클라가 아는 것은
    /// "쉴드가 줄었다"는 복제값뿐이라 <b>방향을 알 방법이 없다.</b>
    /// </summary>
    /// <param name="sourceWorldPosition">공격이 들어온 지점(보통 공격자 위치). 거리는 버리고 방향만 쓴다.</param>
    public void ServerHit(Vector3 sourceWorldPosition)
    {
        if (!IsNetworkActive)
        {
            HitLocal(sourceWorldPosition);
            return;
        }

        if (!IsServer) return;

        HitShieldVfxRpc(sourceWorldPosition);
    }

    // 🔴 루프 '정지'라 Reliable 이다. 유실되면 배리어가 영영 떠 있고 풀 인스턴스도 안 돌아온다.
    //    (원샷 연출이었다면 Unreliable 이 맞다 — 이 레포의 다른 연출 RPC들과 갈리는 지점)
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Reliable)]
    private void EndShieldVfxRpc(bool broken) => EndLocal(broken);

    // 이쪽은 원샷이라 Unreliable 이다. 한 발 유실되면 파문 하나가 안 뜰 뿐 상태가 어긋나지 않는다 —
    // 난타 중에 신뢰 전송으로 줄 세울 이유가 없다.
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void HitShieldVfxRpc(Vector3 sourceWorldPosition) => HitLocal(sourceWorldPosition);

    private void HitLocal(Vector3 sourceWorldPosition)
    {
        if (barrierLoop == null) return;
        if (barrierLoop.GetInstances(_instances) == 0) return;

        for (int i = 0; i < _instances.Count; i++)
        {
            GameObject instance = _instances[i];
            if (instance == null) continue;

            // 걷히는 중이거나 이미 꺼진 배리어에는 띄우지 않는다.
            HolyShieldEffect barrier = instance.GetComponentInChildren<HolyShieldEffect>(true);
            if (barrier != null && !barrier.AcceptsHits) continue;

            ShieldRippleEffect ripple = instance.GetComponentInChildren<ShieldRippleEffect>(true);
            if (ripple == null) continue;

            // 파트 인스턴스의 위치가 곧 배리어 중심이다(EffectManager 가 매 프레임 소켓 위치로 옮긴다).
            Vector3 center = instance.transform.position;
            Vector3 direction = sourceWorldPosition - center;

            // 🔴 장판 한가운데서 맞은 경우 — AreaZone 은 sourcePosition 으로 장판 자신의 위치를 넘긴다.
            //    플레이어가 중앙에 서 있으면 방향이 서지 않는다. 엉뚱한 쪽에 띄우느니 건너뛴다.
            if (direction.sqrMagnitude < minHitDistance * minHitDistance) continue;

            ripple.AddHit(center + direction.normalized);
        }
    }

    /// <summary>
    /// [서버] 단죄의 방패가 적중했다. 충격파를 전 피어에 한 번 터뜨린다.
    ///
    /// <b>왜 스킬이 직접 못 하나.</b> 적중 판정(<c>FirstMeleeInterruptSkill.ResolveHit</c>)은
    /// <b>서버에서만</b> 돈다 — <c>OnTick</c>은 <c>TickServer</c>가, 애니메이션 이벤트는
    /// <c>PlayerSkillController.HandleAnimationEvent</c>의 <c>if (IsNetworkActive &amp;&amp; !IsServer) return;</c>가
    /// 각각 막는다. 거기서 바로 재생하면 <b>호스트에서만 보인다</b>.
    /// 그리고 스킬의 부모 <c>PlayerSkillBase</c>는 MonoBehaviour라 RPC를 달 수 없다.
    /// </summary>
    public void ServerInterruptWave()
    {
        if (!IsNetworkActive)
        {
            interruptWave?.PlayOnce();
            return;
        }

        if (!IsServer) return;

        InterruptWaveRpc();
    }

    // 원샷이라 Unreliable 이다. 한 발 유실되면 충격파 한 번이 안 뜰 뿐 상태가 어긋나지 않는다.
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void InterruptWaveRpc() => interruptWave?.PlayOnce();

    private void EndLocal(bool broken)
    {
        barrierLoop?.Stop();

        if (broken) breakBurst?.PlayOnce();
    }
}
