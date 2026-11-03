using BeaverLobby.Player.Dash;

/// <summary>
/// 간파 대상의 카운터 창 **복제값**(할 일 132) — 피어 화면 대상 재탐색(할 일 143-3)·보정 랩 미리보기가 읽는다.
/// 예측·판정은 이 값이 아니라 <see cref="InterruptManager"/> 창 시간표를 본다(할 일 191).
///
/// <see cref="IInterruptSlowMotionTarget"/> 와 같은 GameObject 에 붙인다. 구현체는 복제값이 바뀔 때
/// <see cref="InterruptWindowReplicaTracker.Record"/> 를 불러 로컬 선착순 장부를 초기화한다.
/// 예측 대상 캐시(<see cref="InterruptTargetRegistry"/>)에는 스폰 때 등록하고 디스폰·사망 때 뺀다.
/// </summary>
public interface IInterruptWindowReplica
{
    /// <summary>[모든 피어] 복제된 카운터 창 열림. 서버 판정 조건(취약·제압·준비 남은 시간 등)은 다 담지 않는 근사다.</summary>
    bool ReplicatedWindowOpen { get; }

    /// <summary>
    /// [모든 피어] 간파 판정의 정면 허용 반각(도, 대상 forward 기준 ±) — 판정과 같은 데이터 값을 읽는다(정정1, 새 칸 없음).
    /// 정면 판정이 없는 대상(중간보스·허수아비) = <see cref="InterruptTargetGeometry.NoFrontRule"/>(180).
    /// </summary>
    float InterruptFrontAngle => InterruptTargetGeometry.NoFrontRule;
}
