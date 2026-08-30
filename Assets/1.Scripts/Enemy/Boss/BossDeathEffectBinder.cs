using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 보스(TwentyThree)의 사망 연출 트리거. 보스 루트에 둔다.
///
/// <b>왜 별도 컴포넌트인가.</b> 보스는 <c>Enemy : Unit</c>이라 <c>MonsterBase</c>·<c>BossBase</c>의
/// <c>EnterDead()</c> 경로를 타지 않는다 — 그쪽에 있는 <see cref="IDeathEffect"/> 훅이 보스에는
/// 아예 없다. <c>Enemy</c>는 일반 몬스터도 쓰는 공용 클래스라 여기에 보스 연출을 박지 않고,
/// 보스 프리팹에만 붙는 어댑터로 분리한다.
///
/// <b>보스는 디스폰되지 않는다.</b> <c>BossEncounterDirector.HandleBossDefeated</c>는 클리어를
/// 기록하고 <c>defeatResultDelaySeconds</c>(기본 3초) 뒤 결과 씬으로 넘길 뿐이다. 그래서
/// <see cref="IDeathEffect.Play"/>에 완료 콜백을 넘기지 않는다 — 디졸브 길이가 그 3초보다
/// 짧기만 하면 된다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(DissolveDeath))]
public class BossDeathEffectBinder : NetworkBehaviour
{
    [Tooltip("사망을 알려줄 Unit. 비우면 이 오브젝트(또는 부모)에서 찾는다")]
    [SerializeField] Unit unit;

    [Tooltip("재생할 사망 연출. 비우면 같은 오브젝트에서 찾는다")]
    [SerializeField] DissolveDeath deathEffect;

    bool _subscribed;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Unit.Died는 서버에서만 발행된다(TakeDamage 경로). 구독도 서버에서만 한다 —
        // 연출을 전 피어에 퍼뜨리는 것은 DissolveDeath의 RPC가 맡는다.
        if (!IsServer) return;

        if (unit == null) unit = GetComponentInParent<Unit>();
        if (deathEffect == null) deathEffect = GetComponent<DissolveDeath>();

        if (unit == null)
        {
            Edit.LogWarning("[No.23] BossDeathEffectBinder가 Unit을 찾지 못했습니다 — 사망 연출이 재생되지 않습니다.", this);
            return;
        }

        if (deathEffect == null)
        {
            Edit.LogWarning("[No.23] BossDeathEffectBinder가 DissolveDeath를 찾지 못했습니다.", this);
            return;
        }

        unit.Died += OnBossDied;
        _subscribed = true;
    }

    public override void OnNetworkDespawn()
    {
        if (_subscribed && unit != null)
        {
            unit.Died -= OnBossDied;
            _subscribed = false;
        }

        base.OnNetworkDespawn();
    }

    // Unit.Died는 _deathNotified로 래치되어 한 번만 발행되지만, 구독 해제 타이밍이
    // 어긋날 수 있어 DissolveDeath 쪽에도 재진입 방어(_played)가 있다.
    void OnBossDied()
    {
        // 디스폰이 없으므로 완료 콜백이 필요 없다.
        deathEffect.Play(null);
    }
}
