using UnityEngine;

// 몬스터 공격 애니메이션 이벤트 릴레이.
//
// 애니메이션 이벤트는 Animator와 "같은 GameObject"에 붙은 컴포넌트의 메서드만 호출할 수 있다.
// 몬스터는 Animator가 모델 자식(예: R_CombatBot)에 있고 두뇌(MonsterBase)는 루트에 있으므로,
// 이 릴레이를 Animator 오브젝트에 얹어 이벤트를 부모의 MonsterBase로 전달한다.
// (MonsterBase.OnNetworkSpawn에서 자동 부착 — 모든 피어. 실효 처리는 서버에서만.)
//
// 클립에 삽입할 이벤트 함수명:
//   - OnAttackHit : 타격 프레임(히트 판정). 없으면 MonsterBase가 attackWindup 타이머로 폴백.
//   - OnAttackEnd : 공격 종료 프레임(상태 이탈). 없으면 attackDuration 타이머로 폴백.
[DisallowMultipleComponent]
public class MonsterAnimationEventRelay : MonoBehaviour
{
    MonsterBase _monster;

    void Awake()
    {
        _monster = GetComponentInParent<MonsterBase>();
    }

    // 애니메이션 이벤트: 타격 프레임.
    public void OnAttackHit()
    {
        if (_monster != null)
            _monster.NotifyAttackHit();
    }

    // 애니메이션 이벤트: 다단계 공격의 "커밋"(다음 단계 진입) 프레임.
    // 예: MortarBot 조준루프(AttackLoop) 말미 → attackFinishTrigger("Attack") 발동 → Strike 전이.
    public void OnAttackCommit()
    {
        if (_monster != null)
            _monster.NotifyAttackCommit();
    }

    // 애니메이션 이벤트: 공격 종료 프레임.
    public void OnAttackEnd()
    {
        if (_monster != null)
            _monster.NotifyAttackEnd();
    }
}
