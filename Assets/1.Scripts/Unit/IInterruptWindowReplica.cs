using BeaverLobby.Player.Dash;

/// <summary>
/// 간파 대상의 카운터 창 **복제값**(할 일 132) — 오너 클라가 간파 입력 순간 성공을 예측(연출 강·약)할 때 읽는다.
/// 판정은 여전히 서버(<see cref="IInterruptSlowMotionTarget.ServerIsInterruptible"/>)가 하고, 이 값은 읽기만 한다.
///
/// <see cref="IInterruptSlowMotionTarget"/> 와 같은 GameObject 에 붙인다. 구현체는 복제값이 바뀔 때
/// <see cref="InterruptWindowReplicaTracker.Record"/> 를 불러 클라 쪽 열림 시각 기록을 남긴다(열린 뒤 경과·추정 길이용).
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
