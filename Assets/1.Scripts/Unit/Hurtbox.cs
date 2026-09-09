using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class Hurtbox : MonoBehaviour
{
    [Tooltip("이 히트박스의 주인 Unit. 플레이어·몬스터·보스는 여기를 채운다")]
    [SerializeField] private Unit ownerUnit;

    [Tooltip("Unit이 아닌 피격 대상(파괴 가능한 상자 등)을 여기 연결한다.\n" +
             "IAttackReceiver를 구현한 컴포넌트만 받는다 — 아니면 OnValidate가 비운다.\n" +
             "ownerUnit이 비어 있을 때만 쓰인다")]
    [SerializeField] private MonoBehaviour attackReceiverSource;

    private IAttackReceiver attackReceiver;

    public Unit OwnerUnit => ownerUnit;

    private void Awake()
    {
        ResolveOwner();
    }

    private void OnValidate()
    {
        // 유니티는 인터페이스 필드를 직렬화하지 못한다. 그래서 MonoBehaviour로 받고
        // 여기서 타입을 검사한다 — 잘못 끌어다 놓으면 인스펙터에서 바로 되돌려
        // "왜 안 맞지"를 런타임까지 끌고 가지 않게 한다.
        if (attackReceiverSource != null && !(attackReceiverSource is IAttackReceiver))
        {
            Debug.LogError(
                $"[Hurtbox] '{attackReceiverSource.GetType().Name}'은(는) IAttackReceiver를 구현하지 않습니다. " +
                "연결을 해제합니다.", this);
            attackReceiverSource = null;
        }

        ResolveOwner();
    }

    public bool TryGetOwner(out Unit unit)
    {
        ResolveReferences();
        unit = ownerUnit;
        return unit != null;
    }

    public bool TryGetReceiver(out IAttackReceiver receiver)
    {
        ResolveReferences();
        receiver = attackReceiver;
        return receiver != null;
    }

    public bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext)
    {
        if (!TryGetReceiver(out IAttackReceiver receiver))
        {
            Debug.LogError($"[Unit] Hurtbox '{name}' has no attack receiver.", this);
            return false;
        }

        return receiver.ReceiveAttack(attackInfo, hitContext);
    }

    private void ResolveOwner()
    {
        ResolveReferences();
    }

    // 명시 배선 → 자동 탐색 순서. 인스펙터에 적힌 값이 항상 이긴다 —
    // 자동 탐색은 중첩 계층에서 엉뚱한 것을 집을 수 있어 최후 수단이다.
    private void ResolveReferences()
    {
        // ① 명시: Unit 주인 (기존 동작. 플레이어·몬스터·보스가 전부 이 경로다)
        if (ownerUnit != null)
        {
            attackReceiver = ownerUnit;
            return;
        }

        // ② 명시: Unit이 아닌 수신자 (파괴 가능한 상자 등)
        if (attackReceiverSource is IAttackReceiver assigned)
        {
            attackReceiver = assigned;
            return;
        }

        // ③ 자동: 부모에서 Unit 탐색
        ownerUnit = GetComponentInParent<Unit>();
        if (ownerUnit != null)
        {
            attackReceiver = ownerUnit;
            return;
        }

        // ④ 자동: 부모에서 아무 IAttackReceiver나 탐색
        attackReceiver = GetComponentInParent<IAttackReceiver>();
    }
}
