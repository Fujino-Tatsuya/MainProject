using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 투사체의 비행 궤적(루프)과 소멸 폭발(원샷)을 재생한다. 투사체 프리팹 루트에 둔다.
///
/// <b>RPC가 하나도 없다.</b> 투사체는 서버가 <c>Spawn(true)</c>하는 NetworkObject라
/// <b>모든 피어에 같은 인스턴스가 생기고</b> <see cref="OnNetworkSpawn"/>·<see cref="OnNetworkDespawn"/>이
/// 각 피어에서 자기 것으로 호출된다. 연출은 그 자리에서 로컬로 재생하면 된다 —
/// 좌표를 실어 보낼 이유도, 서버가 알려줄 이유도 없다.
///
/// <b>왜 엔트리를 <c>MonsterRangedAttack</c>이 넘기지 않는가.</b> <c>Fire()</c>와
/// <c>MonsterProjectile.Launch()</c>는 <c>IsServer</c> 게이트 뒤에 있어 <b>서버에서만</b> 실행된다.
/// 거기서 엔트리를 넘기면 호스트 인스턴스에만 값이 들어가고 클라이언트는 null이 된다.
/// <see cref="EffectEntry"/>는 ScriptableObject라 RPC로 보낼 수도 없다.
/// 몬스터별로 이펙트를 바꾸는 것은 <c>MonsterDataSO.projectilePrefab</c>이 이미 몬스터마다
/// 다르므로, <b>이펙트를 투사체 프리팹이 들고 있게</b> 하면 공짜로 따라온다.
/// (투사체 프리팹 변형을 추가하면 <c>DefaultNetworkPrefabs.asset</c> 등록을 잊지 말 것.)
/// </summary>
[DisallowMultipleComponent]
public class MonsterProjectileEffects : NetworkBehaviour
{
    [Tooltip("비행 중 투사체를 따라다니는 궤적. 루프로 재생되고 소멸 시 회수된다.\n" +
             "비워두면 아무 일도 하지 않는다")]
    [SerializeField] EffectEntry trail;

    [Tooltip("투사체가 사라질 때 1회 재생. 직격·스플래시 착탄·수명 만료 <b>전부</b>에서 재생된다 — " +
             "특정 경로에서만 터뜨리려면 그 경로에서 RPC를 쏘는 방식으로 좁혀야 한다")]
    [SerializeField] EffectEntry explode;

    [Tooltip("폭발 위치 오프셋(월드 단위). 투사체 중심이 아니라 표면에서 터뜨리고 싶을 때")]
    [SerializeField] Vector3 explodeOffset;

    [Tooltip("프리팹에 저작된 크기에 곱해지는 배율")]
    [SerializeField, Min(0.01f)] float scale = 1f;

    EffectHandle _trailHandle = EffectHandle.None;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (trail == null || EffectManager.Instance == null) return;

        // follow로 넘기면 SetParent를 쓰지 않으므로 투사체의 scale이 곱해지지 않고,
        // 투사체가 사라져도 풀 인스턴스가 딸려 죽지 않는다.
        _trailHandle = EffectManager.Instance.PlayLooping(trail, transform, Vector3.zero, scale);
    }

    public override void OnNetworkDespawn()
    {
        // 순서가 중요하다 — 궤적을 먼저 끊고 폭발을 얹어야 한 프레임 겹치지 않는다.
        ReleaseTrail();
        PlayExplode();

        base.OnNetworkDespawn();
    }

    void ReleaseTrail()
    {
        if (!_trailHandle.IsSet) return;

        if (EffectManager.Instance != null) EffectManager.Instance.Release(_trailHandle);
        _trailHandle = EffectHandle.None;
    }

    void PlayExplode()
    {
        if (explode == null || EffectManager.Instance == null) return;

        // ⚠️ 회전은 identity다. 투사체의 rotation은 LookRotation(속도)이라
        // 포물선 탄이 떨어질 때 거의 수직 아래를 향한다 — 그걸 쓰면 폭발이 옆으로 눕는다.
        EffectManager.Instance.Play(explode, transform.position + explodeOffset, Quaternion.identity, scale);
    }

    // 디스폰 없이 파괴되는 경로(씬 언로드 등)에서도 루프 핸들을 회수한다.
    // 놓치면 풀 인스턴스가 영원히 돌아오지 않는다.
    public override void OnDestroy()
    {
        ReleaseTrail();
        base.OnDestroy();
    }
}
