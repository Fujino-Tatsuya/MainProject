using System.Collections.Generic;
using BaseNetCode;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 보호막 연출 창구. <b>보호막 연출의 주인은 보호막을 받은 쪽</b>이라는 것이 이 컴포넌트의 전제다 —
/// 누가 걸어 줬든(자기 E, 거너 Q, 앞으로 생길 무엇이든) 배리어를 띄우고 걷는 것은 여기다.
/// 단죄의 방패 적중 충격파도 같이 맡는다.
///
/// <b>왜 시전자가 안 하나.</b> 세 가지가 전부 어긋난다.
/// <list type="number">
/// <item><b>보호막은 시간보다 먼저 끝난다.</b> <see cref="ShieldEndReason"/> 가
///       Depleted(소진)·Expired(만료)·Cleared(사망·추락) 셋이다. 지속시간을 넘겨받아 타이머로
///       돌리면 이미 깨진 뒤에도 배리어가 남고, 깨지는 연출을 띄울 근거도 없다.</item>
/// <item><b>거너 Q 보호막은 <c>Stack</c> 규칙이다.</b> 같은 아군이 두 번 맞으면 인스턴스가 2개가
///       되고 각자 다른 시각에 끝난다 — 타이머 하나로는 못 센다.
///       (팔라딘 E 는 <c>Replace</c> 라 1개뿐이어서 이 문제가 안 보였다.)</item>
/// <item><b>수명 주인이 다르다.</b> 거너 Q 스킬은 FireRecovery 만에 끝나는데 보호막은 5초 간다.
///       시전자가 죽거나 나가면 꺼 줄 주체가 사라진다.</item>
/// </list>
///
/// <b>그래서 복제 상태를 그대로 읽는다.</b> <c>Unit</c> 이 보호막 목록을
/// <c>NetworkList&lt;ShieldInstance&gt;</c> 로 전 피어에 복제하고 있다
/// (<see cref="Unit.ShieldInstanceCount"/> · <see cref="Unit.GetShieldInstance"/>).
/// 각 피어가 자기 눈앞의 상태를 보고 켜고 끄므로 <b>시작·종료에 RPC 가 필요 없고</b>,
/// 늦게 들어온 클라도 맞고, 시전자가 넘겨야 할 값이 하나도 없다.
///
/// <b>딱 하나 RPC 가 필요한 곳</b>은 "깨졌는가(Depleted)" 다. 복제 목록으로는 보호막이
/// <b>사라졌다</b>까지만 알 수 있고 <b>왜</b>는 서버만 안다. 그래서 파괴 연출만 전 피어로 퍼뜨린다.
/// 피격 파문도 같은 이유다 — 공격 판정이 서버 전용 스코프라 방향을 클라가 알 길이 없다.
///
/// <b>색은 출처로 가른다</b>(2026-10-06, 민경 지정).
/// <c>HolyShield</c>(자기 E) = 금색 / 그 외 전부 = 흰색. 둘 다 걸려 있으면 <b>금색이 이긴다</b> —
/// 자기 핵심 스킬을 남의 연출이 덮으면 안 된다. 배리어는 어느 쪽이든 <b>언제나 하나</b>다.
///
/// ⚠️ 소켓의 safetyTimeout 은 <b>보호막 지속시간보다 길어야 한다</b> — 짧으면 배리어가 도중에
/// 강제 회수된다. 팔라딘 10초 / 거너 5초 기준으로 15초를 쓴다.
/// </summary>
[DisallowMultipleComponent]
public class PlayerShieldVfx : BaseNetworkBehaviour
{
    /// <summary>지금 띄워야 할 배리어. 셋 중 하나뿐이다 — 두 겹으로 띄우지 않는다.</summary>
    private enum Barrier
    {
        None,
        Holy,   // 자기 E(수호자의 의지) — 금색
        Ally,   // 남이 걸어 준 보호막 — 흰색
    }

    [Header("자기 보호막 (수호자의 의지 · 금색)")]
    [Tooltip("보호막이 떠 있는 동안 재생할 루프. 프리팹의 'HolyShield' EffectSocketPlayer 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer barrierLoop;

    [Tooltip("보호막이 피해로 깨질 때 한 번 재생할 파괴 연출. 프리팹의 'HolyShield_Break' 를 물린다.\n" +
             "비워두면 깨지는 순간에도 조용히 걷히기만 한다")]
    [SerializeField] private EffectSocketPlayer breakBurst;

    [Header("남이 걸어 준 보호막 (거너 Q 등 · 흰색)")]
    [Tooltip("FX_AllyShield_Entry 를 문 EffectSocketPlayer. 프리팹의 'AllyShield' 를 물린다.\n" +
             "비워두면 남이 준 보호막에는 배리어가 안 뜬다")]
    [SerializeField] private EffectSocketPlayer allyBarrierLoop;

    [Tooltip("FX_AllyShield_Break_Entry. 프리팹의 'AllyShield_Break' 를 물린다")]
    [SerializeField] private EffectSocketPlayer allyBreakBurst;

    [Header("공통")]
    [Tooltip("공격 지점이 배리어 중심에서 이만큼도 떨어져 있지 않으면 파문을 건너뛴다(미터).\n" +
             "장판 한가운데서 맞으면 방향이 서지 않는다 — 엉뚱한 쪽에 띄우는 것보다 안 띄우는 게 낫다")]
    [SerializeField, Min(0f)] private float minHitDistance = 0.15f;

    [Tooltip("단죄의 방패(우클릭)가 적중했을 때 한 번 터뜨릴 충격파. 프리팹의 'ShieldWave' 를 물린다.\n" +
             "비워두면 연출만 빠진다")]
    [SerializeField] private EffectSocketPlayer interruptWave;

    // 풀 인스턴스를 담아 둘 수 없으므로(반납되면 다른 연출에 재대출된다) 매번 다시 받는다.
    // 리스트만 재사용해 할당을 없앤다.
    private readonly List<GameObject> _instances = new List<GameObject>();

    private Unit _unit;
    private Barrier _shown;
    private bool _subscribed;

    private void Awake()
    {
        // PlayerShieldVfx 는 Unit(Player)과 같은 GameObject 에 얹힌다 — NetworkObject 가 거기 있다.
        _unit = GetComponent<Unit>();
        if (_unit == null)
        {
            Edit.LogWarning($"[PlayerShieldVfx] '{name}' 에 Unit 이 없다 — 보호막 배리어가 뜨지 않는다.", this);
            return;
        }

        // 🔴 스폰 전에도 건다. 이벤트는 순수 C# 이고 서버 판정은 핸들러 안에서 한다.
        _unit.ServerShieldEnded += OnServerShieldEnded;
        _subscribed = true;
    }

    private void OnDestroy()
    {
        if (_subscribed && _unit != null)
            _unit.ServerShieldEnded -= OnServerShieldEnded;
    }

    // 🔴 소켓은 OnDisable 에서 스스로 회수한다. 여기서 _shown 을 안 지우면
    //    다시 켜졌을 때 "이미 띄워 뒀다"고 판단해 영영 안 나온다.
    private void OnDisable() => _shown = Barrier.None;

    /// <summary>
    /// 복제된 보호막 목록을 보고 배리어를 맞춘다.
    ///
    /// 목록이 비어 있는 것이 대부분이라 평소 비용은 <c>Count</c> 읽기 한 번이다. 할당은 없다.
    /// </summary>
    private void LateUpdate()
    {
        // 오프라인(VFXScene·싱글 테스트)에서는 복제 목록이 채워지지 않는다
        // (Unit.UpdateNetworkShield 가 !IsSpawned 에서 빠져나간다). 그쪽은 PlayLocal 이 맡는다.
        if (!IsNetworkActive || _unit == null)
            return;

        Barrier want = Resolve();
        if (want == _shown)
            return;

        Show(want);
    }

    private Barrier Resolve()
    {
        int count = _unit.ShieldInstanceCount;
        if (count == 0)
            return Barrier.None;

        // 자기 E 가 하나라도 섞여 있으면 금색이 이긴다.
        Barrier want = Barrier.Ally;
        for (int i = 0; i < count; i++)
        {
            if (_unit.GetShieldInstance(i).type != ShieldType.HolyShield) continue;

            want = Barrier.Holy;
            break;
        }

        // 🔴 그 색 소켓이 비어 있으면 다른 쪽으로 떨어뜨린다. 거너 프리팹에는 금색 소켓이 없다 —
        //    팔라딘 E 는 자기 자신만 씌우므로 지금은 닿지 않는 길이지만, 닿았을 때
        //    "보호막은 있는데 아무것도 안 뜬다"보다는 색이 틀린 쪽이 낫다.
        if (want == Barrier.Holy && barrierLoop == null && allyBarrierLoop != null) return Barrier.Ally;
        if (want == Barrier.Ally && allyBarrierLoop == null && barrierLoop != null) return Barrier.Holy;

        return want;
    }

    private void Show(Barrier want)
    {
        // 색이 바뀌는 경우(흰색 중에 자기 E 를 씀) 옛 배리어는 아웃트로로 걷히고 새것이 올라온다.
        if (_shown == Barrier.Holy) barrierLoop?.Stop();
        else if (_shown == Barrier.Ally) allyBarrierLoop?.Stop();

        if (want == Barrier.Holy) barrierLoop?.Play();
        else if (want == Barrier.Ally) allyBarrierLoop?.Play();

        _shown = want;
    }

    /// <summary>
    /// [오프라인 전용] 배리어를 띄운다.
    ///
    /// 🔴 <b>네트워크가 붙어 있으면 아무 일도 하지 않는다.</b> 그때는 복제된 보호막 목록이
    /// 유일한 근거이고, 여기서 또 켜면 목록이 아직 도착하지 않은 피어에서
    /// "켰다가 한 프레임 뒤 껐다가 다시 켜는" 깜빡임이 난다.
    /// 스킬 쪽(<see cref="FirstMeleeSubSkill.OnClientPlay"/>)이 계속 불러도 안전하다.
    /// </summary>
    public void PlayLocal()
    {
        if (IsNetworkActive) return;

        barrierLoop?.Play();
        _shown = Barrier.Holy;
    }

    /// <summary>
    /// [오프라인 전용] 배리어를 걷는다.
    ///
    /// 🔴 <b>네트워크가 붙어 있으면 아무 일도 하지 않는다.</b> 걷는 것은 복제 목록이,
    /// 파괴 연출은 <see cref="OnServerShieldEnded"/> 가 맡는다.
    /// </summary>
    /// <param name="broken">피해로 소진돼 깨진 것이면 true, 시간 만료·사망이면 false.</param>
    public void ServerEnd(bool broken)
    {
        if (IsNetworkActive) return;

        barrierLoop?.Stop();
        _shown = Barrier.None;
        if (broken) breakBurst?.PlayOnce();
    }

    /// <summary>
    /// [서버] 보호막 하나가 끝났다. <b>마지막 하나가 피해로 깨진 경우에만</b> 파괴 연출을 퍼뜨린다.
    ///
    /// 남은 보호막이 있으면 배리어는 그대로 떠 있으므로, 거기에 파괴 연출을 겹치면
    /// "깨졌는데 아직 있다"로 읽혀 거짓말이 된다.
    ///
    /// 🔴 <c>Unit</c> 은 <c>UpdateNetworkShield()</c> 를 <b>부른 뒤에</b> 이 이벤트를 쏜다
    /// (ApplyMitigatedHealthDamage · ShieldExpiryLoop · ClearShields 전부 같은 순서).
    /// 그래서 여기서 읽는 <c>ShieldInstanceCount</c> 는 이미 이번 종료가 반영된 값이다.
    /// </summary>
    private void OnServerShieldEnded(ShieldInstance shield, ShieldEndReason reason)
    {
        if (!IsServer || reason != ShieldEndReason.Depleted) return;
        if (_unit == null || _unit.ShieldInstanceCount > 0) return;

        BreakBurstRpc(shield.type == ShieldType.HolyShield);
    }

    // 원샷이라 Unreliable 이다. 한 발 유실되면 파괴 연출 한 번이 안 뜰 뿐 상태가 어긋나지 않는다 —
    // 배리어를 걷는 일은 복제 목록이 따로 하고 있다.
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void BreakBurstRpc(bool holy)
    {
        // 배리어와 같은 폴백 규칙 — 그 색이 없으면 있는 쪽으로.
        EffectSocketPlayer burst = holy ? breakBurst : allyBreakBurst;
        if (burst == null) burst = holy ? allyBreakBurst : breakBurst;

        burst?.PlayOnce();
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

    // 이쪽도 원샷이라 Unreliable 이다. 난타 중에 신뢰 전송으로 줄 세울 이유가 없다.
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void HitShieldVfxRpc(Vector3 sourceWorldPosition) => HitLocal(sourceWorldPosition);

    // 어느 색이 떠 있는지는 여기서 따지지 않는다 — 안 뜬 쪽은 인스턴스가 0이라 조용히 넘어간다.
    private void HitLocal(Vector3 sourceWorldPosition)
    {
        Ripple(barrierLoop, sourceWorldPosition);
        Ripple(allyBarrierLoop, sourceWorldPosition);
    }

    private void Ripple(EffectSocketPlayer player, Vector3 sourceWorldPosition)
    {
        if (player == null) return;
        if (player.GetInstances(_instances) == 0) return;

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
}
