/// <summary>
/// 큰 순간이동 직후 벽 겹침 억제 판정기 — 씬/MonoBehaviour 없이 이동 거리·시각만으로 판정한다(할 일 187).
/// 서버가 플레이어를 멀리 옮기면(QA 하네스 QABoss103Match.PlacePartyOnPad 의 보스 발판 재배치 ≈116m 등)
/// 클라에 그 위치가 반영되는 1~2틱 동안 캡슐이 벽에 잠깐 겹친다 — 순간이동 도중의 과도 상태라 버그가 아니다.
/// 순간이동(<c>teleportDistance</c> 초과 이동)을 본 시각부터 <see cref="GraceSeconds"/> 동안 P8 을 억제한다.
/// 유예 뒤에도 겹치면 실제 관통이므로 그대로 보고한다. P8 벽 관통(GameplayInvariantDetector)만 쓴다.
/// </summary>
public sealed class TeleportWallGraceJudge
{
    public const double GraceSeconds = 0.5;

    private readonly float _teleportDistance;
    private bool _hasTeleport;
    private double _lastTeleportTime;

    public TeleportWallGraceJudge(float teleportDistance)
    {
        _teleportDistance = teleportDistance;
    }

    public void Reset()
    {
        _hasTeleport = false;
    }

    /// <summary>이번 이동 거리가 순간이동 기준을 넘으면 그 시각을 기록한다.</summary>
    public void NotifyMove(double time, float distance)
    {
        if (distance > _teleportDistance)
        {
            _hasTeleport = true;
            _lastTeleportTime = time;
        }
    }

    /// <summary>이번 틱 벽 겹침을 봐주는지(마지막 순간이동에서 <see cref="GraceSeconds"/> 이내).</summary>
    public bool IsSuppressed(double time)
    {
        return _hasTeleport && time - _lastTeleportTime <= GraceSeconds;
    }
}
