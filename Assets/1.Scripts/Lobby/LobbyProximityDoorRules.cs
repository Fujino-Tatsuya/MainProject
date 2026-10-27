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

    /// <summary>
    /// 이번 프레임에 쓸 진행도(할 일 99). 다음 자세(<paramref name="candidate"/>)의 문짝이 대상 플레이어 몸과 겹치면
    /// 지금 진행도 그대로 멈춘다 — 문짝이 캡슐 위로 돌면 모터 스윕이 시작 겹침으로 전 방향을 막아 영구히 끼인다.
    /// 여는 중·닫는 중·닫히다 다시 열리는 중 모두 같다. 비키면 다음 프레임부터 다시 움직인다.
    /// </summary>
    public static float ResolveProgress(float progress, float candidate, bool nextPoseBlocked) =>
        nextPoseBlocked ? progress : candidate;

    /// <summary>
    /// 문 판정 대상인가 — 열림 판정과 문짝 겹침 판정이 같이 쓴다. 스폰된 <see cref="PlayerLifeState.Alive"/> 만.
    /// 죽은 플레이어는 문을 열지도, 막지도 않는다.
    /// </summary>
    public static bool IsDoorTarget(bool isSpawned, PlayerLifeState state) =>
        isSpawned && state == PlayerLifeState.Alive;

    /// <summary>
    /// 이번에 열 방향(+1 = 문짝 로컬 +Z 쪽으로 넘어감 = 로컬 Y +각도, -1 = 반대).
    /// <paramref name="playerSide"/> = 닫힌 문 기준으로 연 플레이어가 로컬 +Z 쪽이면 양수 — 그 반대쪽으로 열어
    /// 다가온 플레이어를 문짝이 덮지 않는다. 문면 위(0)는 +Z 쪽으로 친다.
    /// </summary>
    public static int SwingSign(float playerSide) => playerSide >= 0f ? -1 : 1;

    /// <summary>
    /// 로컬 Y 로 <paramref name="angle"/> 만큼 돌렸을 때의 문짝 로컬 자세. 회전 중심 = 문짝 로컬 <paramref name="pivot"/>
    /// (힌지 모서리의, 넘어가는 쪽 면) — 문짝 두께가 문틀로 파고들지 않는다. angle 0 이면 닫힌 자세 그대로.
    /// </summary>
    public static void LeafPose(Vector3 closedPosition, Quaternion closedRotation, float angle, Vector3 pivot,
        out Vector3 position, out Quaternion rotation)
    {
        var turn = Quaternion.Euler(0f, angle, 0f);
        rotation = closedRotation * turn;
        position = closedPosition + closedRotation * (pivot - turn * pivot);
    }

    /// <summary>
    /// 문짝 상자(<paramref name="leafBox"/> — 문짝 회전 기준, 월드 단위)가 월드 자세 (<paramref name="leafPosition"/>,
    /// <paramref name="leafRotation"/>)일 때 직립(Y 축) 몸 캡슐과 겹치는가(할 일 99). 문짝은 Y 축으로만 돈다고 보고
    /// 문짝 기준 XZ 에서 원(반경) 대 사각형, Y 는 캡슐 높이 구간으로 본다. 닿기만 한 경계는 겹침이 아니다.
    /// </summary>
    public static bool LeafBoxOverlapsUprightCapsule(Bounds leafBox, Vector3 leafPosition, Quaternion leafRotation,
        Vector3 capsuleCenter, float capsuleHeight, float capsuleRadius)
    {
        if (capsuleRadius <= 0f)
            return false;

        var local = Quaternion.Inverse(leafRotation) * (capsuleCenter - leafPosition);
        var halfHeight = Mathf.Max(capsuleHeight * 0.5f, capsuleRadius);
        if (local.y + halfHeight <= leafBox.min.y || local.y - halfHeight >= leafBox.max.y)
            return false;

        var dx = Mathf.Max(leafBox.min.x - local.x, 0f, local.x - leafBox.max.x);
        var dz = Mathf.Max(leafBox.min.z - local.z, 0f, local.z - leafBox.max.z);
        return dx * dx + dz * dz < capsuleRadius * capsuleRadius;
    }

    /// <summary>두 점 사이의 평면(XZ) 거리 제곱.</summary>
    public static float PlanarSqrDistance(Vector3 a, Vector3 b)
    {
        var dx = a.x - b.x;
        var dz = a.z - b.z;
        return dx * dx + dz * dz;
    }
}
