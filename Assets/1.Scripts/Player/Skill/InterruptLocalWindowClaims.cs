using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// [각 피어] 간파 로컬 선착순 장부(할 일 143-2) — 오너 화면 연출을 1차로 막는다. 순수 로직(EditMode 테스트 대상).
/// 최종 판정은 서버 장부(<see cref="InterruptWindowClaims"/>)다 — 이 장부는 오너 시도 예측(암전·빛 모임·섬광)만 약으로 내린다.
///
/// <list type="bullet">
/// <item>창 = 대상의 카운터 창 복제값이 한 번 열린 것. <see cref="WindowOpened"/> 가 그 대상 기록을 지운다(새 창 = 초기화).
///       서버 장부와 같은 전이(같은 복제값의 열림)를 쓴다 — <see cref="InterruptWindowReplicaTracker.Record"/> 가 넘긴다.</item>
/// <item>닫힘(<see cref="Observe"/>) = 이 창에서 다른 플레이어의 강 시도(<see cref="SlowMotionPurpose.Attempt"/>) 또는 성공을 받았다.
///       약 시도(<see cref="SlowMotionPurpose.AttemptWeak"/>)는 닫지 않는다 — 그 사람이 실패하면 내가 첫 성공일 수 있다. 내 것도 닫지 않는다.</item>
/// <item>창이 닫힌 전이는 기록을 바꾸지 않는다 — 닫힌 창은 복제값만으로 이미 약 예측이다.</item>
/// </list>
/// 키 = 대상 NetworkObjectId(0 = 대상 없음, 무시) · 시전자·보는 사람 = clientId. 신호 = 슬로우 Started(디렉터가 넘긴다, 새 RPC 없음).
/// 상대 신호가 닿기 전 동시 입력은 못 막는다 — 서버 판정 뒤 암전 걷힘이 처리한다. 문서: Docs/tech/interrupt-presentation.md §0.10.
/// </summary>
public sealed class InterruptLocalWindowClaims
{
    private readonly HashSet<ulong> _closed = new HashSet<ulong>();

    /// <summary>[각 피어] 런타임 장부 하나 — 디렉터(신호)·창 복제 기록(초기화)·오너 시도 예측(읽기)이 같이 쓴다.</summary>
    public static InterruptLocalWindowClaims Local { get; } = new InterruptLocalWindowClaims();

    // 도메인 리로드를 끈 Play 진입에서도 이전 세션 기록이 남지 않게 비운다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Local.Clear();

    /// <summary>새 창이 열렸다 — 그 대상의 닫힘 기록을 버린다.</summary>
    public void WindowOpened(ulong target) => _closed.Remove(target);

    /// <summary>
    /// 슬로우 시작 하나를 본다 — <paramref name="caster"/> 가 <paramref name="viewer"/>(이 피어) 가 아니고 강 시도·성공이면
    /// 그 대상의 이 창을 닫는다. 닫았으면(이미 닫혀 있었어도) true.
    /// </summary>
    public bool Observe(ulong target, ulong caster, SlowMotionPurpose purpose, ulong viewer)
    {
        if (target == 0 || caster == viewer || purpose == SlowMotionPurpose.AttemptWeak)
            return false;

        _closed.Add(target);
        return true;
    }

    /// <summary>이 창을 다른 플레이어가 먼저 잡았다고 봤는가 — 오너 시도 예측이 창 닫힘으로 친다.</summary>
    public bool IsClosed(ulong target) => target != 0 && _closed.Contains(target);

    /// <summary>대상이 디스폰된다 — 기록을 지운다(id 재사용 대비).</summary>
    public void Forget(ulong target) => _closed.Remove(target);

    public void Clear() => _closed.Clear();
}
