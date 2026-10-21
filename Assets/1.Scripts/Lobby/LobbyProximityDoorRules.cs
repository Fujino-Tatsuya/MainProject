using UnityEngine;

/// <summary>
/// 로비 근접 문의 열림/닫힘 판정(순수, EditMode 테스트 대상) — 할 일 78.
/// 열림 반경 &lt; 닫힘 반경 히스테리시스: 닫혀 있으면 열림 반경 안(경계 포함)에 누가 들어와야 열리고,
/// 열려 있으면 전원이 닫힘 반경 밖으로 나가야 닫힌다. 두 반경 사이에서는 지금 상태를 유지해 경계에서 떨지 않는다.
/// 거리는 <see cref="NearestInteractable"/> 와 같이 <b>평면(XZ)</b> 거리다.
/// </summary>
public static class LobbyProximityDoorRules
{
    /// <summary>
    /// 다음 프레임의 열림 여부. <paramref name="nearestSqrDistance"/> = 가장 가까운 대상까지의 평면 거리 제곱
    /// (대상이 없으면 <see cref="float.PositiveInfinity"/>). 닫힘 반경이 열림 반경보다 작으면 열림 반경으로 올려 쓴다.
    /// </summary>
    public static bool NextOpen(bool isOpen, float nearestSqrDistance, float openRadius, float closeRadius)
    {
        var open = Mathf.Max(0f, openRadius);
        var close = Mathf.Max(open, closeRadius);

        return isOpen
            ? nearestSqrDistance <= close * close
            : nearestSqrDistance <= open * open;
    }

    /// <summary>
    /// 열림 진행도(0 = 닫힘, 1 = 다 열림)를 목표 쪽으로 한 프레임 움직인다.
    /// 여는 중이면 <paramref name="openSeconds"/>, 닫는 중이면 <paramref name="closeSeconds"/> 에 0↔1 을 지난다. 0초 이하는 즉시.
    /// </summary>
    public static float StepProgress(float progress, bool targetOpen, float openSeconds, float closeSeconds, float deltaTime)
    {
        var target = targetOpen ? 1f : 0f;
        var seconds = targetOpen ? openSeconds : closeSeconds;
        if (seconds <= 0f)
            return target;

        return Mathf.MoveTowards(Mathf.Clamp01(progress), target, Mathf.Max(0f, deltaTime) / seconds);
    }

    /// <summary>두 점 사이의 평면(XZ) 거리 제곱.</summary>
    public static float PlanarSqrDistance(Vector3 a, Vector3 b)
    {
        var dx = a.x - b.x;
        var dz = a.z - b.z;
        return dx * dx + dz * dz;
    }
}
