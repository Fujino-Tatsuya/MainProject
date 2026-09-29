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
