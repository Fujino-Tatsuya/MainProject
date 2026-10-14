using UnityEngine;

/// <summary>
/// [서버] 플레이어 넉백의 벽 충돌(할 일 32 — 이벤트만). <see cref="Player.OnKnockback"/> 가 서버에서 넉백을 받아들일 때 <see cref="Begin"/>,
/// 서버 Update 가 넉백 상태 동안 <see cref="Tick"/>. 판정은 공용 <see cref="UnitWallImpacts.ServerProbe"/>(넉백 한 번 = 1회).
/// 이동은 오너 권한이라 서버는 복제된 위치를 보며 판정한다 — 모터가 Wall 에서 멈추므로 몸 가장자리 ↔ 벽 면 0.4m 안이면 충돌.
/// 충격 속도 = 서버 모델 속도(세기 − 감속 × 경과) — 복제 위치 차분은 네트워크 틱에 따라 튀어서 쓰지 않는다.
/// 민 쪽은 <c>Unit.Knockback(방향, 세기)</c> 에 없어서 null.
/// </summary>
internal sealed class PlayerWallImpactTracker
{
    readonly WallImpactEpisode _episode = new WallImpactEpisode();
    Vector3 _direction;
    float _strength;
    float _startTime;

    public void Begin(Vector3 direction, float strength)
    {
        _direction = direction;
        _strength = Mathf.Max(0f, strength);
        _startTime = Time.time;
        _episode.Begin();
    }

    public void Tick(Player player, bool knockbackActive, float deceleration)
    {
        if (!_episode.IsActive) return;
        if (!knockbackActive)
        {
            _episode.End();
            return;
        }

        float speed = Mathf.Max(0f, _strength - Mathf.Max(0f, deceleration) * (Time.time - _startTime));
        UnitWallImpacts.ServerProbe(player, _episode, _direction, 0f, 0f, speed, null, out _);
    }
}
