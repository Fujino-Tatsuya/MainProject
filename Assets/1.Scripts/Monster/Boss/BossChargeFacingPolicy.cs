using UnityEngine;

/// <summary>
/// 차징 시 보스가 바라볼 방향 — <b>화면 아래쪽(남쪽)</b>을 월드 XZ 방향으로 바꾼다.
///
/// 문제 — 차징은 도착 당시 방향을 그대로 유지해서 매번 다른 쪽을 봤다(팀장 관찰: 화면 좌측을 본다).
/// 연출이 정면으로 서야 하는 구간이라 방향을 확정해야 한다.
///
/// 왜 카메라를 보는가 — "화면 남쪽"은 카메라가 정하는 방향이다. 월드 방향을 상수로 박으면
/// 카메라 거치가 바뀌는 순간 조용히 틀어진다(팀장 선택 2026-09-08).
///
/// 🔴 톱다운 경계 처리가 이 클래스의 존재 이유다. 피치가 90°에 가까우면 <c>forward</c> 의 XZ
///    투영이 0 으로 수축해 방향을 잃는다 — 그때는 <c>up</c> 을 쓴다. 완전 수직 카메라에서
///    화면 위쪽은 <c>up</c> 의 XZ 투영이고, 기울어진 카메라에서는 <c>forward</c> 의 투영과 같은 쪽을 본다.
///
/// ⚠️ 리슨서버에서는 서버(=호스트)의 카메라가 기준이다. 거치가 고정 톱다운이라 두 피어가 같지만,
///    누군가 카메라를 돌리면 그 사람 화면에서는 정남이 아니다.
/// </summary>
public static class BossChargeFacingPolicy
{
    // 이 아래로 짧아지면 투영이 방향을 잃은 것으로 본다(완전 수직 카메라).
    const float MinProjectedLength = 0.01f;

    /// <summary>
    /// 화면 아래쪽에 해당하는 월드 XZ 방향(정규화). 둘 다 못 쓰면 <paramref name="fallback"/>.
    /// </summary>
    /// <param name="cameraForward">카메라 forward.</param>
    /// <param name="cameraUp">카메라 up.</param>
    /// <param name="fallback">폴백 방향(기본 −Z = 고정 톱다운의 화면 아래).</param>
    public static Vector3 ScreenSouth(Vector3 cameraForward, Vector3 cameraUp, Vector3 fallback)
    {
        // 화면 위쪽 = 카메라에서 멀어지는 방향의 XZ 투영.
        Vector3 up = Flatten(cameraForward);
        if (up.sqrMagnitude < MinProjectedLength * MinProjectedLength)
            up = Flatten(cameraUp);   // 수직 카메라 — forward 가 XZ 에서 사라진다

        if (up.sqrMagnitude < MinProjectedLength * MinProjectedLength)
            return Normalized(fallback);

        return -up.normalized;
    }

    /// <summary>폴백 기본값 — 요 0° 고정 톱다운에서 화면 아래.</summary>
    public static Vector3 DefaultFallback => Vector3.back;

    static Vector3 Flatten(Vector3 v) => new Vector3(v.x, 0f, v.z);

    static Vector3 Normalized(Vector3 v)
    {
        Vector3 flat = Flatten(v);
        return flat.sqrMagnitude < MinProjectedLength * MinProjectedLength
            ? Vector3.back
            : flat.normalized;
    }
}
