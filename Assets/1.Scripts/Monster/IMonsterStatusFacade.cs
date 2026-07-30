// 상태이상 적용 파사드(seam).
//
// 플레이어 스킬 / 성장 시스템(담당: 은희)이 몬스터에게 CC·버프를 걸 때 사용할 공개 진입점이다.
// 지금(슬라이스 1)은 MonsterStatusEffect가 이 인터페이스를 구현하고 ApplyStatus로 최소 라우팅만 한다.
// 은희가 나중에 실제 수치/지속시간/스택 규칙과 호출부(어떤 스킬이 어떤 상태를 몇 초)를 채운다.
//
// 사용 예(은희 작업 예정):
//   var facade = monster.GetComponent<IMonsterStatusFacade>();
//   facade?.ApplyStatus(StatusEffectType.Stunned, 1.5f);
public interface IMonsterStatusFacade
{
    // 서버 권한. duration <= 0 이면 무한 지속(수동 해제 전까지).
    void ApplyStatus(StatusEffectType type, float duration);

    // 특정 상태 플래그 해제.
    void RemoveStatus(StatusEffectType type);

    // 모든 상태 초기화(복귀/사망/리셋 시).
    void ClearAll();

    // 슈퍼아머 여부(넉백/경직 무시 판단).
    bool HasSuperArmor { get; }
}
