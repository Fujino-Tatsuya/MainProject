/// <summary>선입력 버퍼가 기억하는 입력 종류(할 일 44).</summary>
public enum PlayerBufferedInput
{
    None,
    Attack,
    Dash,
    SkillMain,
    SkillSub,
    SkillInterrupt, // 우클릭 — 인터럽트 상태 경로도 이 종류를 쓴다
    SkillUltimate
}

/// <summary>
/// 선입력 버퍼(할 일 44) — 지금 바로 실행 못 하는 입력을 잠깐 들고 있다가 실행 가능해진 첫 순간에 넘긴다.
/// 마지막 1개만 기억한다(새 입력이 이전 것을 덮는다). 유지 시간이 지나면 없는 것으로 본다.
/// 시간은 호출자가 넘긴다(MonoBehaviour 아님 — EditMode 테스트 대상). 유지 시간 0 이하 = 버퍼 끔.
/// </summary>
public sealed class PlayerInputBuffer
{
    private PlayerBufferedInput kind = PlayerBufferedInput.None;
    private float recordedAt;

    public PlayerInputBuffer(float holdSeconds)
    {
        HoldSeconds = holdSeconds;
    }

    public float HoldSeconds { get; set; }

    /// <summary>새로 눌린 입력을 기록한다. 이전 입력은 버린다.</summary>
    public void Record(PlayerBufferedInput input, float now)
    {
        if (HoldSeconds <= 0f || input == PlayerBufferedInput.None)
            return;

        kind = input;
        recordedAt = now;
    }

    /// <summary>지금 살아 있는 입력. 만료됐으면 None.</summary>
    public PlayerBufferedInput Peek(float now)
    {
        if (kind == PlayerBufferedInput.None)
            return PlayerBufferedInput.None;

        if (HoldSeconds <= 0f || now - recordedAt > HoldSeconds)
        {
            kind = PlayerBufferedInput.None;
            return PlayerBufferedInput.None;
        }

        return kind;
    }

    public bool Has(PlayerBufferedInput input, float now) =>
        input != PlayerBufferedInput.None && Peek(now) == input;

    /// <summary>그 종류가 들어 있으면 지운다. 다른 종류는 그대로 둔다.</summary>
    public void Consume(PlayerBufferedInput input)
    {
        if (input != PlayerBufferedInput.None && kind == input)
            kind = PlayerBufferedInput.None;
    }

    public void Clear()
    {
        kind = PlayerBufferedInput.None;
    }

    public static PlayerBufferedInput FromSkillSlot(PlayerSkillSlot slot) => slot switch
    {
        PlayerSkillSlot.Main => PlayerBufferedInput.SkillMain,
        PlayerSkillSlot.Sub => PlayerBufferedInput.SkillSub,
        PlayerSkillSlot.Interrupt => PlayerBufferedInput.SkillInterrupt,
        PlayerSkillSlot.Ultimate => PlayerBufferedInput.SkillUltimate,
        _ => PlayerBufferedInput.None
    };
}
