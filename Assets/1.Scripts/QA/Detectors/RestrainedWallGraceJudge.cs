/// <summary>
/// 구속(23호 잡기) 중 벽 겹침 억제 판정기 — 씬/MonoBehaviour 없이 틱 입력만으로 판정한다(할 일 185).
/// 구속(<see cref="PlayerActionState.Restrained"/>) 중에는 PlayerMotor.ResolvePoseStaticOverlap 이 일부러
/// 플레이어를 벽 밖으로 꺼내지 않는다(136-2 결정) — 잡혀 끌려가는 몸이 벽에 겹치는 건 정상이다.
/// 풀린 뒤에도 디페네트레이션이 몇 틱 걸리므로 <see cref="GraceSeconds"/> 동안 더 억제한다.
/// P8 벽 관통(GameplayInvariantDetector)과 QA103 벽 재배치(QABoss103CombatBot.TickWall)가 같이 쓴다.
/// </summary>
public sealed class RestrainedWallGraceJudge
{
    public const double GraceSeconds = 0.5;

    private bool _hasRestrained;
    private double _lastRestrainedTime;

    public void Reset()
    {
        _hasRestrained = false;
    }

    /// <summary>이번 틱 벽 겹침을 봐주는지(구속 중이거나 마지막 구속 틱에서 <see cref="GraceSeconds"/> 이내).</summary>
    public bool Step(double time, PlayerActionState state)
    {
        if (state == PlayerActionState.Restrained)
        {
            _hasRestrained = true;
            _lastRestrainedTime = time;
            return true;
        }

        return _hasRestrained && time - _lastRestrainedTime <= GraceSeconds;
    }
}
