using System.Collections.Generic;

/// <summary>
/// <see cref="EffectSocketPlayer"/> 안전 회수(safetyTimeout)를 잠깐 끄고 나중에 원래 값으로 되돌린다(할 일 298).
/// 끝을 코드(타임라인)가 정하는 긴 재생용 — 보스 처치 연출 거너 Q 의 차징 루프·과충전·알림·레이저.
/// 같은 소켓을 여러 번 꺼도 원래 값은 처음 한 번만 적어 둔다. 파괴된 소켓은 되돌리기에서 건너뛴다.
/// 테스트 <c>BossVictoryGunnerLaserTests</c>.
/// </summary>
public sealed class EffectSocketSafetyOverride
{
    readonly Dictionary<EffectSocketPlayer, float> _home = new Dictionary<EffectSocketPlayer, float>();

    /// <summary>끈 소켓 수(되돌리기 전).</summary>
    public int Count => _home.Count;

    /// <summary>안전 회수 끄기(0). null 이면 무시.</summary>
    public void Suspend(EffectSocketPlayer player)
    {
        if (player == null) return;
        if (!_home.ContainsKey(player)) _home[player] = player.SafetyTimeout;
        player.SetSafetyTimeout(0f);
    }

    /// <summary>끈 소켓 전부 원래 값으로.</summary>
    public void RestoreAll()
    {
        foreach (KeyValuePair<EffectSocketPlayer, float> home in _home)
            if (home.Key != null) home.Key.SetSafetyTimeout(home.Value);
        _home.Clear();
    }
}
