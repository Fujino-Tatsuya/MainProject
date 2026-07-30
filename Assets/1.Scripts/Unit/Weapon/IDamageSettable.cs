/// <summary>
/// 외부에서 damage 값을 주입받을 수 있는 컴포넌트를 나타내는 인터페이스.
/// (특정 보스에 종속되지 않는 범용 계약)
/// </summary>
public interface IDamageSettable
{
    /// <summary>현재 damage 값을 설정한다. (음수는 0으로 보정)</summary>
    void SetDamage(int value);
}
