using UnityEngine;
using Unity.Netcode;

/// <summary>
/// 공격 컴포넌트의 피격 연출을 <b>전 피어에 퍼뜨리는 중계자</b>. 보스 루트(NetworkObject가 있는 곳)에 둔다.
///
/// <b>왜 중계자가 필요한가.</b> <see cref="KnockbackAttack"/>은 <c>BaseAttack : MonoBehaviour</c>라
/// <c>NetworkBehaviour</c>가 아니어서 RPC를 선언할 수 없다. 게다가 <c>ApplyKnockbackAttack</c>은
/// 첫 줄이 <c>if (!IsServer) return;</c>이라 <b>클라이언트에서는 실행 자체가 되지 않는다</b> —
/// 거기서 이펙트를 재생하면 호스트에서만 보인다(이 레포가 피격 이펙트에서 이미 낸 버그다).
///
/// <b>무엇을 보내는가.</b> 이펙트 위치가 아니라 <b>피격자 위치</b>다. 위치는 각 피어가 자기 로컬
/// 공격 콜라이더 표면에서 만든다(<see cref="KnockbackAttack.PlayHitEffectLocal"/>).
/// 보낸 좌표는 "구 표면 위 어느 지점인지"만 고르는 방향 힌트라, 보간 지연으로 조금 어긋나도
/// 점이 표면을 따라 미끄러질 뿐 눈에 띄지 않는다.
///
/// <see cref="EffectEntry"/>는 ScriptableObject라 RPC로 보낼 수 없다. 대신 각 피어의 프리팹이
/// 이미 같은 엔트리를 들고 있으므로 <b>어느 공격인지(인덱스)</b>만 실어 보낸다.
/// </summary>
[DisallowMultipleComponent]
public class AttackEffectRelay : NetworkBehaviour
{
    [Tooltip("이 중계자가 담당하는 공격들. 인덱스가 곧 RPC 식별자이므로 순서를 바꾸면 배선이 어긋난다.\n" +
             "보스에는 Rage·DashAttack 등 KnockbackAttack이 여러 개라 구분이 필요하다")]
    [SerializeField] KnockbackAttack[] sources;

    /// <summary>
    /// [서버] 피격 연출을 전 피어에 요청한다. 목록에 없는 공격이면 조용히 무시한다.
    /// </summary>
    /// <param name="source">연출을 요청한 공격 컴포넌트</param>
    /// <param name="targetPosition">피격자 위치. 표면 위 지점을 고르는 <b>방향 힌트</b>다</param>
    public void Broadcast(KnockbackAttack source, Vector3 targetPosition)
    {
        if (!IsServer || source == null || sources == null) return;

        int index = System.Array.IndexOf(sources, source);
        if (index < 0 || index > byte.MaxValue)
        {
            Edit.LogWarning($"[AttackEffectRelay] '{source.name}'이 sources 목록에 없다. " +
                            "인스펙터에서 등록할 것.", this);
            return;
        }

        PlayHitEffectRpc((byte)index, targetPosition);
    }

    // 순수 연출이라 unreliable. 유실돼도 게임 상태가 갈라지지 않는다 —
    // 같은 이유로 JumpController의 착지 VFX도 이 방식을 쓴다.
    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    void PlayHitEffectRpc(byte index, Vector3 targetPosition)
    {
        if (sources == null || index >= sources.Length) return;

        sources[index]?.PlayHitEffectLocal(targetPosition);
    }
}
