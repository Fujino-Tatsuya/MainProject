/// <summary>
/// 간파 성공 쿨 감소를 대상 단위로 끄는 선택 인터페이스(서버 전용, 할 일 86 T5a — 명세 v0.2 §6·§9.2 "훈련 간파 성공 쿨 반환 없음").
/// 구현하지 않은 대상(23호·중간보스·허수아비)은 지금처럼 감소를 준다 — 실전 규칙은 그대로다.
///
/// 🔴 전역 <c>PlayerGameRuleData.InterruptSuccessCooldownReduction</c> 을 0 으로 덮지 않는 이유: 플레이어 전원이 공유하는 데이터라
///    훈련에서 바꾸면 실전으로 샌다. 대신 "이 대상의 성공은 감소를 주지 않음"을 대상이 직접 밝힌다.
///
/// 연결(T5b): <c>PlayerInterruptSkillBase</c> 가 성공을 장부에 올리는 두 곳(슬로우 모션 전후 비교·<c>ServerCounterSucceeded</c> 수신) —
/// 대상 Unit 을 아는 지점 — 에서 이 값을 <c>successCooldown.NotifySuccess(grantsCooldown)</c> 로 넘긴다.
/// false 면 성공은 남고(<c>ServerInterruptSucceeded</c> 통계·튜토리얼 이벤트 유지) 감소만 빠진다 — 성공 기록 자체를 건너뛰면 그 이벤트까지 사라진다.
/// <c>ApplySuccessCooldownReduction</c> 은 대상을 모르는(시전 1회 = 감소 1회) 합산 지점이라 거기선 판정하지 않는다.
///
/// Unit 과 같은 GameObject 에 붙인다(<see cref="IInterruptSlowMotionTarget"/> 와 같은 규칙).
/// </summary>
public interface IInterruptSuccessCooldownTarget
{
    /// <summary>[서버] 이 대상에게 간파가 성공했을 때 시전자의 간파 스킬 쿨 감소를 주는가. 훈련 상대 = false.</summary>
    bool ServerGrantsInterruptSuccessCooldown { get; }
}
