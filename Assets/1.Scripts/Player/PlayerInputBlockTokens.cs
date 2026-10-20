using System.Collections.Generic;

/// <summary>
/// 플레이어 입력 차단 토큰 집합(순수, 할 일 48 C9 — PLAN-48 §4.4).
/// 요청자(UI 모달 정책·로비 장치 등)가 각자 고유 토큰으로 막고 풀며, 토큰이 하나라도 남아 있으면 막힌 상태다.
/// 같은 토큰을 두 번 걸어도 한 번으로 친다 — 푸는 쪽이 짝을 맞추지 않아도 누수가 없다.
/// <see cref="PlayerInputReader"/> 가 생명주기 정책(SetInputEnabled·SetCombatInputEnabled)과 AND 로 합친다.
/// </summary>
public sealed class PlayerInputBlockTokens
{
    private readonly HashSet<object> tokens = new HashSet<object>();

    public bool IsBlocked => tokens.Count > 0;

    public int Count => tokens.Count;

    /// <summary>토큰을 건다. 막힘 여부가 바뀌었으면(열림 → 막힘) true. null 은 무시한다.</summary>
    public bool Acquire(object token)
    {
        if (token == null)
            return false;

        bool wasBlocked = IsBlocked;
        return tokens.Add(token) && !wasBlocked;
    }

    /// <summary>토큰을 푼다. 막힘 여부가 바뀌었으면(막힘 → 열림) true. 걸지 않은 토큰·null 은 무시한다.</summary>
    public bool Release(object token)
    {
        if (token == null || !tokens.Remove(token))
            return false;

        return !IsBlocked;
    }

    public bool Contains(object token) => token != null && tokens.Contains(token);
}
