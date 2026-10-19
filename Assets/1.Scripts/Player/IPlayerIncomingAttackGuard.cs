/// <summary>
/// 진행 중인 플레이어 스킬이 피격을 가로챌 수 있게 하는 서버 전용 훅(팔라딘 E 방어·반격).
/// <see cref="Player"/> 가 <see cref="PlayerSkillController.ActiveSkill"/> 이 이 인터페이스일 때만 묻는다.
/// 시간 경계(방어 창·반격 종료 시각)는 구현체가 Time.time 으로 직접 적용한다.
/// </summary>
public interface IPlayerIncomingAttackGuard
{
    /// <summary>
    /// [서버] Player.ReceiveAttack 맨 위(피해 상한·base 앞)에서 불린다.
    /// true = 이 피격을 흡수했다(피해·피격 반응 없음, ReceiveAttack 은 true 반환) / false = 원래대로 진행.
    /// 사망·무적 중에는 Player 가 묻지 않는다.
    /// </summary>
    bool ServerTryAbsorbAttack(AttackInfo attackInfo, AttackHitContext hitContext);

    /// <summary>[서버] true 면 Player.OnKnockback 이 넉백을 통째로 버린다.</summary>
    bool ServerBlocksKnockback { get; }
}
