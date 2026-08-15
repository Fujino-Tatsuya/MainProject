using Unity.Behavior;
using Unity.Netcode;
using UnityEngine;
using static EffectCatalog;

public class Enemy : Unit
{
    [Header("초기화 값")]
    [SerializeField] int attackDamage;
    [SerializeField] float moveSpeed;
    [SerializeField] float chaseSpeed;
    [SerializeField] float attackSpeed;
    [SerializeField] int maxHp;
    [SerializeField] int defense;

    BlackboardVariable<float> WalkSpeed;
    BlackboardVariable<float> ChaseSpeed;

    [Header("시간제어 컴포넌트")]
    [SerializeField] MonsterTimeController _monsterTimeController;

    // hitPointMode는 여기 없다 — 전 유닛 공통이라 EffectManager로 올렸다(런타임 교체도 거기서).
    [Header("피격 이펙트 제어")]
    [SerializeField] Collider hitVFXCollider;
    [SerializeField] HitVFXType hitVFXType;

    public override void OnNetworkSpawn()
    {
        // ⚠️ base 호출이 빠져 있었다. Unit.OnNetworkSpawn이 HP/쉴드 복제 구독과 HitFlash(피격 빨간
        // 틴트) 자동 부착을 담당하므로, 이게 없으면 **보스만** 피격 표시가 안 나온다.
        // 전 피어에서 실행돼야 하는 로컬 연출이라 IsServer 게이트보다 먼저 호출한다.
        base.OnNetworkSpawn();

        if (!IsServer) return;
        Initialize(attackDamage, moveSpeed, attackSpeed, maxHp, defense);

        BehaviorGraphAgent bt = GetComponent<BehaviorGraphAgent>();
        if (bt == null)
            Edit.LogAssertion("[Enemy] BehaviorGraphAgent를 얻어오는 것을 실패했습니다.");

        ApplyOptionalSpeed(bt, "WalkSpeed", moveSpeed, out WalkSpeed);
        ApplyOptionalSpeed(bt, "ChaseSpeed", chaseSpeed, out ChaseSpeed);


    }

    /// <summary>
    /// 이동 속도 블랙보드 변수를 <b>있으면</b> 채운다.
    ///
    /// <c>WalkSpeed</c>·<c>ChaseSpeed</c>는 그래프마다 있을 수도, 없을 수도 있는 <b>선택 변수</b>다
    /// (No.23은 <c>WalkSpeed</c>만 쓰고, <c>Enemy/CommonMeleeRobot</c>은 둘 다 쓴다). 예전에는 부재를
    /// 경고로 띄웠는데, 그게 "이름을 맞춰야 할 버그"처럼 보여서 위험했다 — <c>TwentyThree.prefab</c>의
    /// <c>chaseSpeed</c>는 0이라, 경고만 보고 그래프에 변수를 만들어 이름을 맞추면 <b>추격 속도 0으로
    /// 보스가 제자리에 굳는다</b>. 그래서 부재는 조용히 넘기고, 반대로 <b>그래프가 쓰는데 값이 0인</b>
    /// 경우만 경고한다 — 그쪽이 실제로 "적이 안 움직인다"로 터지는 조건이다.
    /// </summary>
    void ApplyOptionalSpeed(BehaviorGraphAgent bt, string variableName, float value,
                            out BlackboardVariable<float> cached)
    {
        if (!bt.BlackboardReference.GetVariable<float>(variableName, out cached))
            return;   // 이 그래프는 그 속도 개념을 쓰지 않는다 — 정상이다.

        if (value <= 0f)
        {
            Edit.LogWarning(
                $"[Enemy] {name}의 {variableName}로 넣을 값이 {value}입니다 — BT가 이 값을 그대로 이동 " +
                "속도로 쓰므로 해당 상태에서 제자리에 멈춥니다. 프리팹의 속도 값을 채우세요.", this);
        }

        cached.Value = value;
    }

    public override bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        bool resolved = base.ReceiveAttack(attackInfo, hitContext);

        // 피격 이펙트는 판정이 아니라 연출이다 — 서버는 위치만 알리고 재생은 각 피어가 로컬로 한다.
        // ReceiveAttack은 서버에서만 불리므로(BaseAttack.TryResolveHit의 IsServer 게이트) 여기서
        // 직접 Play하면 호스트에서만 보인다.
        if (IsServer)
            PlayHitVFXRpc(hitContext.sourcePosition);

        return resolved;
    }

    // 서버가 보내는 것은 공격자 위치 하나뿐이다. 계산이 끝난 타격점(Pose)을 보내지 않는 이유:
    //
    // 클라이언트의 몹은 NetworkTransform 보간 때문에 서버보다 뒤에 그려진다(TickRate 30 + 보간
    // 버퍼 → 100ms 안팎, 4m/s면 0.3~0.4m = 몸통 반쯤). 서버가 계산한 월드 절대 좌표를 그대로
    // 재생하면 그 차이만큼 이펙트가 몸에서 떨어져 허공에 뜬다. 수신측이 자기 콜라이더로 다시
    // 계산하면 결과는 언제나 그 몹 표면 위다.
    //
    // 반대로 origin(공격자 위치)이 조금 틀리는 것은 무해하다 — origin은 "표면의 어느 쪽을
    // 고를지"만 정하지 이펙트를 몸에서 떼어내지 못한다. 그래서 origin만 서버 값을 쓴다.
    //
    // ⚠️ 호스트는 곧 서버라 이 어긋남이 0이다. 호스트 화면으로는 잘못된 구현도 정상으로 보인다 —
    // 검증은 반드시 MPPM 클라이언트 창에서, 몹이 이동 중일 때 한다.
    //
    // Unreliable: 순수 연출이라 유실돼도 상태가 발산하지 않는다(이펙트 하나가 빠질 뿐).
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    void PlayHitVFXRpc(Vector3 sourcePosition)
    {
        HitVFXPlayback.Play(this, hitVFXCollider, hitVFXType, sourcePosition);
    }
}
