/// <summary>
/// 외부에서 넉백 세기(knockback strength)를 주입받을 수 있는 컴포넌트를 나타내는 인터페이스.
/// (특정 보스에 종속되지 않는 범용 계약)
/// </summary>
public interface IKnockbackSettable
{
    /// <summary>넉백 세기 값을 설정한다. (음수는 0으로 보정)</summary>
    void SetKnockbackStrength(float value);
}
