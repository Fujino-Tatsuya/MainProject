using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 보호막(수호자의 의지) 연출의 켜고 끄는 창구. 소켓 두 개를 들고 있고 스킬이 시점만 알려 준다.
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

    // 🔴 루프 '정지'라 Reliable 이다. 유실되면 배리어가 영영 떠 있고 풀 인스턴스도 안 돌아온다.
    //    (원샷 연출이었다면 Unreliable 이 맞다 — 이 레포의 다른 연출 RPC들과 갈리는 지점)
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Reliable)]
    private void EndShieldVfxRpc(bool broken) => EndLocal(broken);

    private void EndLocal(bool broken)
    {
        barrierLoop?.Stop();

        if (broken) breakBurst?.PlayOnce();
    }
}
