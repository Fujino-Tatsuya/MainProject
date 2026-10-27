using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// 간파 성공 이펙트 칸 — 간파 성공이고 발동자가 이 플레이어면
/// <see cref="casterFx"/> 를 시전자 소켓 위치에서, <see cref="targetFx"/> 를 신호의 대상 위치에서 원샷으로 재생한다.
/// <c>Player.prefab</c>(base) 루트에 붙고 칸은 비워 둔다 — 캐릭터 Variant 가 자기 소켓을 만들어 칸을 덮어쓴다.
/// 할 일 87 C4·C5, 문서 Docs/tech/interrupt-presentation.md.
///
/// <list type="bullet">
/// <item>수동 실행자다(할 일 118) — <see cref="InterruptProductionDirector"/> 가 슬로우 Started(Success) 를 받아
///       발동자 플레이어의 이 컴포넌트에 <see cref="InterruptProductionCue.SuccessFxSlot"/> 을 부른다(공용 채널, 칸 A4).</item>
/// <item>새 RPC 없음 — 슬로우 이벤트의 목적 칸·대상 위치 칸이 전 피어 공용 성공 신호다. 각 피어가 자기 화면에 각자 재생한다.</item>
/// <item>칸이 비어 있으면 그 칸은 아무것도 안 한다(경고도 없음).</item>
/// <item>캐릭터별 기존 성공 연출(가붕이 방패 충격파·어쌔신 성공 소켓·거너)과는 별개다.</item>
/// </list>
/// </summary>
[DisallowMultipleComponent]
public sealed class InterruptSuccessFx : MonoBehaviour, IInterruptCueSink
{
    /// <summary>이번 성공에서 재생할 칸.</summary>
    [Flags]
    public enum Slots
    {
        None = 0,
        Caster = 1,
        Target = 2,
    }

    [Tooltip("간파 성공 시 시전자 소켓 위치에서 재생할 원샷 이펙트. base 는 비워 두고 캐릭터 Variant 가 소켓을 연결한다")]
    [FormerlySerializedAs("successEffect")]
    [SerializeField] private EffectSocketPlayer casterFx;

    [Tooltip("간파 성공 시 카운터 창을 소비한 대상 위치(못 찾으면 시전자)에서 재생할 원샷 이펙트(충격파). " +
             "소켓은 루트가 아닌 빈 자식에 둔다 — 재생 순간 소켓을 대상 위치로 잠깐 옮겼다 되돌린다")]
    [SerializeField] private EffectSocketPlayer targetFx;

    private NetworkObject _networkObject;

    private void Awake()
    {
        _networkObject = GetComponent<NetworkObject>();
    }

    /// <summary>[각 피어] 디렉터가 부른다(칸 A4 는 이미 걸렀다). 발동자가 이 플레이어일 때만 연결된 칸을 원샷.</summary>
    public void PlayCue(InterruptProductionCue cue, in InterruptProductionEvent e)
    {
        if (cue != InterruptProductionCue.SuccessFxSlot || _networkObject == null)
            return;

        bool shouldPlay = InterruptProductionRules.ShouldPlaySuccessFx(SlowMotionPurpose.Success, e.CasterClientId,
            _networkObject.IsSpawned, _networkObject.OwnerClientId);
        Slots slots = InterruptProductionRules.SelectSuccessFxSlots(shouldPlay, casterFx != null, targetFx != null);

        if ((slots & Slots.Caster) != 0)
            casterFx.PlayOnce();
        if ((slots & Slots.Target) != 0)
            PlayOnceAt(targetFx, e.TargetPosition);
    }

    // EffectSocketPlayer 는 위치 지정 원샷이 없다 — 소켓을 대상 위치로 옮겨 PlayOnce 하고 같은 호출 안에서 되돌린다.
    // 원샷은 재생 순간 위치에 찍히고 소켓을 따라가지 않으므로 되돌려도 이펙트는 대상 위치에 남는다.
    private static void PlayOnceAt(EffectSocketPlayer fx, Vector3 position)
    {
        Transform socket = fx.Socket;
        Vector3 restore = socket.position;
        socket.position = position;
        fx.PlayOnce();
        socket.position = restore;
    }
}
