/// <summary>
/// 캐릭터 패시브의 HUD 용 읽기 창구. base(Player·PassiveHUD)는 구체 패시브 타입을 모르고 이것만 본다.
/// 패시브 컴포넌트는 Player 와 같은 GameObject 에 붙는다(<c>GetComponent&lt;IPlayerPassive&gt;()</c>).
/// </summary>
public interface IPlayerPassive
{
    /// <summary>쿨타임 전체 길이(초). HUD fill 분모.</summary>
    float CooldownTime { get; }

    /// <summary>남은 쿨타임(초). 발동 가능 상태면 0. 오너·서버에서만 유효.</summary>
    float RemainingCooldown { get; }

    /// <summary>발동 가능 상태(버프 보유 등). HUD 강조.</summary>
    bool IsReady { get; }
}

/// <summary>
/// 서버가 공격 판정의 첫 Unit 대상에게 기본 피해를 적용하기 직전에 묻는 추가 피해 제공자.
/// 구현체는 반환과 동시에 소모·쿨타임·회복·연출 같은 발동 상태를 확정한다.
/// </summary>
public interface IPlayerOnHitBonus
{
    /// <summary>[서버] 이번 첫 대상에게 합산할 추가 피해. 발동하지 않으면 0.</summary>
    int ServerConsumeOnHitBonus(Unit target);
}
