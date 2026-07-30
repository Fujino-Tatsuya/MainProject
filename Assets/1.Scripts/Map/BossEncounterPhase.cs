/// <summary>
/// 보스 등장 연출의 서버 권한 단계. 복제되어 HUD·로컬 피드백이 같은 값을 본다.
/// (승인 계획 <c>Docs/superpowers/plans/2026-07-24-boss-encounter-intro.md</c> Task 3)
/// </summary>
public enum BossEncounterPhase
{
    /// <summary>연출 전. 보스 미스폰.</summary>
    Idle,

    /// <summary>텔레포트 도착 ACK 대기(BossTeleportManager 소유 구간).</summary>
    AwaitingArrival,

    /// <summary>참가자 잠금·보스 스폰 준비.</summary>
    Preparing,

    /// <summary>보스가 상공에서 착지점으로 하강 중.</summary>
    Descending,

    /// <summary>착지 순간·직후 정지 구간.</summary>
    Impact,

    /// <summary>페이지 대사 표시 구간(대사 HUD는 후속 작업).</summary>
    Dialogue,

    /// <summary>전투 시작. 잠금 해제 + BT 개방 완료.</summary>
    Combat,

    /// <summary>연출을 안전하게 중단한 상태. 참가자는 조작 가능해야 한다.</summary>
    FailedSafe
}
