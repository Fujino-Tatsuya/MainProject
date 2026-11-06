using UnityEngine;

/// <summary>
/// 보스 처치 연출 반복 디버그 랩(할 일 244 — DebugBossVictory 씬)의 순수 규칙. 상태가 없어 EditMode 로 고정한다.
/// 단계 이름 · 막타/팀원 배치 · 점프(빨리 감기) 배속 · 연출 시계 밀기 양.
/// 시간은 <see cref="BossVictoryRules"/> 와 같은 <b>단계 시각 t</b>(경과 − 추가 연출 구간)를 쓴다.
/// </summary>
public static class BossVictoryLabRules
{
    /// <summary>점프할 때 빨리 감는 최대 배속(Time.timeScale).</summary>
    public const float SeekMaxRate = 8f;

    /// <summary>점프 목표에 닿았다고 보는 오차(초).</summary>
    public const float SeekTolerance = 0.0005f;

    /// <summary>단계 이름(화면 표시). <paramref name="resultReached"/> = 결과 화면 전환을 막은 뒤(한 바퀴 끝).</summary>
    public static string StageName(float t, bool resultReached)
    {
        if (BossVictoryRules.IsPrelude(t)) return "추가 연출 구간(prelude — 보스 몸 정지)";
        if (t < BossVictoryRules.FreezeStart) return "처치 순간";
        if (t < BossVictoryRules.DescendStart) return "슬로모션 시작";
        if (t < BossVictoryRules.DescendEnd) return "슬로모션 · 카메라 하강(→ 24°)";
        if (t < BossVictoryRules.ColorInEnd) return "슬로모션 · 주황 단색 들어옴";
        if (t < BossVictoryRules.FreezeEnd) return "슬로모션 · 정지 구도(24°)";
        if (t < BossVictoryRules.End) return "슬로모션 해제 · 카메라 상승(24° → 32°)";
        return resultReached ? "끝 — 결과 화면 전환 차단(다시 재생 대기)" : "연출 단계 끝 — 사망 완료·결과 대기";
    }

    /// <summary>
    /// 막타 위치 — 보스에서 <paramref name="screenSouth"/>(화면 아래, 수평) 쪽으로 <paramref name="distance"/> m 를
    /// 위에서 볼 때 시계 방향 <paramref name="angleDegrees"/>° 돌린 곳. 높이는 <paramref name="groundY"/>.
    /// </summary>
    public static Vector3 KillerPosition(Vector3 boss, Vector3 screenSouth, float angleDegrees, float distance, float groundY)
    {
        Vector3 dir = Flat(screenSouth);
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.back;
        dir = Quaternion.Euler(0f, angleDegrees, 0f) * dir.normalized;
        Vector3 p = boss + dir * Mathf.Max(0f, distance);
        p.y = groundY;
        return p;
    }

    /// <summary>막타가 보스를 보는 방향에 <paramref name="yawOffsetDegrees"/>° 를 더한 회전(수평).</summary>
    public static Quaternion KillerFacing(Vector3 killer, Vector3 boss, float yawOffsetDegrees)
    {
        Vector3 to = Flat(boss - killer);
        if (to.sqrMagnitude < 0.0001f) to = Vector3.forward;
        return Quaternion.Euler(0f, yawOffsetDegrees, 0f) * Quaternion.LookRotation(to.normalized, Vector3.up);
    }

    /// <summary>팀원 위치 — 막타 기준 로컬 오프셋(x = 오른쪽, z = 앞 = 보스 쪽). 높이는 막타와 같다.</summary>
    public static Vector3 TeammatePosition(Vector3 killer, Quaternion killerFacing, Vector3 localOffset)
    {
        Vector3 p = killer + killerFacing * new Vector3(localOffset.x, 0f, localOffset.z);
        p.y = killer.y;
        return p;
    }

    /// <summary>
    /// 점프 중 이번 프레임 배속 — 남은 시간을 다음 프레임(대략 <paramref name="unscaledDelta"/>)에 다 먹되 <see cref="SeekMaxRate"/> 를 넘지 않는다.
    /// 목표를 지났으면 0.
    /// </summary>
    public static float SeekRate(float target, float current, float unscaledDelta)
    {
        float remain = target - current;
        if (remain <= SeekTolerance) return 0f;
        return Mathf.Clamp(remain / Mathf.Max(0.001f, unscaledDelta), 0f, SeekMaxRate);
    }

    /// <summary>
    /// 연출 시계를 밀 양(초) — 서버 시각은 실제 시간(unscaled)으로 흐르고 애니·사망 대기는 게임 시간(deltaTime)으로 흐른다.
    /// 처치 시각을 그 차이만큼 뒤로 밀면 단계 시각이 게임 시간과 같이 흐른다(배속·일시정지·점프).
    /// </summary>
    public static double ClockShift(float unscaledDelta, float scaledDelta) => (double)unscaledDelta - scaledDelta;

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
