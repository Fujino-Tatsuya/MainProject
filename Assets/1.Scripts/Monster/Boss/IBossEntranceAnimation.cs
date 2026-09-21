/// <summary>
/// 보스방 입장 연출의 <b>애니메이션 seam</b>.
///
/// <see cref="BossEncounterDirector"/> 는 보스를 상공에서 착지점까지 <b>transform 으로 직접 내린다</b>.
/// 그런데 애니메이터는 보스 스크립트가 소유하므로, Director 가 "지금 하강 중 / 지금 착지 /
/// 이제 전투" 라는 <b>시점만</b> 알려 주고 무엇을 재생할지는 보스가 정한다.
///
/// 🔴 <b>왜 인터페이스인가</b> — Director 는 <c>MonsterBase</c> 만 들고 있다.
///    여기에 보스 입장 전용 가상 메서드를 넣으면 <b>모든 몬스터가</b> 그걸 갖게 된다.
///    <see cref="IBossChargeSequence"/> 와 같은 관용구로 보스 쪽에만 둔다.
///
/// ⚠️ 구현이 없어도 연출은 <b>그대로 진행되어야 한다</b>(Director 는 경고만 낸다).
///    웰즈나 다른 보스가 들어와도 입장이 깨지지 않게 하는 것이 이 seam 의 목적이다.
///
/// 🔴 <b>전부 서버에서만 호출된다.</b> 실제 재생은 각 구현이 ClientRpc 로 내린다 —
///    여기서 직접 애니메이터를 만지면 <b>호스트에서만 보인다</b>(이 레포가 여러 번 겪은 버그).
/// </summary>
public interface IBossEntranceAnimation
{
    /// <summary>하강 시작 — 체공 포즈로 바꾼다. 안 하면 Idle 로 뻣뻣하게 내려온다.</summary>
    void PlayEntranceDescentServer();

    /// <summary>착지 순간 — 착지 클립을 튼다.</summary>
    /// <remarks>
    /// 🔴 <b>데미지는 안 나간다</b>(2026-09-21 확인). 착지 클립에 <c>OnAttackHit</c> 애니 이벤트가
    /// 박혀 있지만, <c>NotifyAttackHit</c> 이 <c>State != MonsterState.Attack</c> 이면 즉시 반환하고
    /// 연출 중에는 <c>SetServerLogicSuspended(true)</c> 가 보스를 <c>Idle</c> 로 잡아 둔다.
    /// ⚠️ 연출 중 FSM 을 깨우도록 바꾸면 <b>여기서 입장 AoE 가 터진다.</b>
    /// </remarks>
    void PlayEntranceLandingServer();

    /// <summary>연출 종료 — 로코모션으로 되돌린다. FSM 을 깨우기 <b>직전</b>에 부른다.</summary>
    /// <remarks>
    /// 🔴 <b>이게 없으면 착지 포즈가 전투 중에 남는다.</b> 애니를 바꾸는 경로는
    /// <c>OnStateChanged → PlayStateAnimation</c> 하나뿐인데, 전투가 시작돼도 보스가
    /// <c>Idle</c> 에 머물면 <b>상태 변화가 없어 그 경로를 안 탄다.</b>
    /// 착지 클립(1.92초)이 <c>impactHoldSeconds</c>(0.9초)보다 길어 실제로 남을 수 있다.
    /// (Codex 교차검증 2026-09-21 지적 — 원래 계획은 "알아서 잘린다"고 전제했다.)
    ///
    /// ⚠️ 순서 — <c>SetServerLogicSuspended(false)</c> <b>보다 앞</b>이다.
    ///    뒤에 두면 FSM 이 고른 첫 애니메이션을 이게 덮어쓴다.
    /// </remarks>
    void EndEntranceAnimationServer();
}
