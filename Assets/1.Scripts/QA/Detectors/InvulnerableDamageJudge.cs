/// <summary>
/// P6 무적 중 피해 판정 — 씬/MonoBehaviour 없이 샘플 값만으로 판정한다(할 일 184).
/// 서버는 대시 무적을 ServerRpc 도착 순간에 켠다. 그래서 그 직전 피격으로 줄어든 HP 와 무적 켜짐이
/// 같은 NGO 틱에 묶여 클라에 동시에 도착한다. 무적이 막 켜진 샘플의 HP 감소는 무적 이전 피해이므로,
/// 이전 샘플부터 이미 무적이었을 때만 위반으로 본다.
/// </summary>
public static class InvulnerableDamageJudge
{
    /// <param name="hasPreviousHealth">이전 샘플 HP 가 유효한지.</param>
    /// <param name="previousInvulnerable">이전 샘플의 서버 무적.</param>
    /// <param name="invulnerable">지금 샘플의 서버 무적.</param>
    /// <param name="inFallGrace">낙사 피해(무적 우회) 유예 창 안인지.</param>
    public static bool IsViolation(
        bool hasPreviousHealth,
        int previousHealth,
        int health,
        bool previousInvulnerable,
        bool invulnerable,
        bool inFallGrace)
    {
        return hasPreviousHealth &&
               previousInvulnerable &&
               invulnerable &&
               health < previousHealth &&
               !inFallGrace;
    }
}
