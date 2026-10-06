using UnityEngine;

/// <summary>
/// 어쌔신 우클릭 간파(character_assassin.md §11, PLAN-assassin A10) — 공용 간파 베이스 그대로, 캐릭터 훅 없음.
/// 일반·변신 공통(대체 세트 없음). 애니 = Attack_Up_01("Interrupt" 상태), 앵커 = Assassin_Armature/InterruptAttack.
/// 다른 동작 중 우클릭 무시·예약 없음은 공통 FSM 이 처리한다. 변신 종료 대기 중이면 간파(Skill 상태)가 끝난 뒤
/// <see cref="AssassinState"/> 가 변신을 끝낸다(§10.3 "현재 간파 공격 동작 완료").
/// 간파 성공 방향 조건은 보스 쪽 판정이다 — 변신 중 백어택과 별개.
/// </summary>
public sealed class AssassinInterruptSkill : PlayerInterruptSkillBase
{
    // 애니메이션은 PlayerSkillController 가 animatorStateName 으로 재생한다. 간파 고유 연출은 아직 없다(민경).
    public override void OnClientPlay(Vector3 direction) { }
}
