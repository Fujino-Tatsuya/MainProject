using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 룸 판 도중 재합류(C14 R3c, 설계 메모 §3.4, 사용자 답 Q-A·Q-F) — 모서리 4지점과 "지금 보스 룸인가" 판정.
/// <list type="bullet">
/// <item>모서리 원본 = <c>bossroom.prefab</c> 의 <see cref="GroupName"/>/<c>Corner1~4</c>(BossRoomAuthoring 이 없을 때만 만든다 — 경석이 옮길 수 있다).</item>
/// <item>그룹이 없으면 <c>InvisibleBoundaries/Boundary_*</c> 안쪽 면에서 <see cref="Inset"/> 만큼 들어온 네 점을 계산한다(경고 1회).
/// 🔴 경계 이름은 **방 로컬 축** 기준이다 — 아레나가 씬에서 yaw -90 이라 월드 AABB 로 읽으면 X·Z 가 뒤바뀐다(TwentyThreeBoss.ResolveArena 와 같은 읽기).</item>
/// </list>
/// 순수 계산(<see cref="ComputeLocalCorners"/>·<see cref="TryReadArenaLocal"/>·<see cref="IsInBossRoom"/>·<see cref="IsInsideBoxXZ"/>)은 EditMode 테스트 대상이다.
/// </summary>
public static class BossRoomRejoin
{
    public const string GroupName = "RejoinCornerPoints";
    public const string BoundariesName = "InvisibleBoundaries";
    public const string BossAreaTag = "BossArea";
    public const int CornerCount = 4;
    /// <summary>경계 안쪽 면에서 모서리까지(m) — NavMeshMargin(1m)·보스 몸 반경(≈1.5m) 밖.</summary>
    public const float Inset = 2f;

    /// <summary>모서리 자식 이름 — index 0..3 → Corner1..4.</summary>
    public static string CornerName(int index) => $"Corner{index + 1}";

    static bool s_warnedFallback;
    static bool s_warnedMissing;

    /// <summary>
    /// 방 로컬 경계 안쪽 면 → 로컬 모서리 4개. 순서 = (minX,minZ)·(maxX,minZ)·(maxX,maxZ)·(minX,maxZ).
    /// 방이 인셋 두 배보다 좁은 축은 가운데로 모은다.
    /// </summary>
    public static void ComputeLocalCorners(float minX, float maxX, float minZ, float maxZ, float floorY, float inset, Vector3[] output)
    {
        float x0 = minX + inset, x1 = maxX - inset, z0 = minZ + inset, z1 = maxZ - inset;
        if (x0 > x1) x0 = x1 = (minX + maxX) * 0.5f;
        if (z0 > z1) z0 = z1 = (minZ + maxZ) * 0.5f;

        output[0] = new Vector3(x0, floorY, z0);
        output[1] = new Vector3(x1, floorY, z0);
        output[2] = new Vector3(x1, floorY, z1);
        output[3] = new Vector3(x0, floorY, z1);
    }

    /// <summary>모서리에서 방 중심을 바라보는 회전(수평). 같은 점이면 항등.</summary>
    public static Quaternion FaceTowards(Vector3 from, Vector3 to)
    {
        var dir = to - from;
        dir.y = 0f;
        return dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir.normalized, Vector3.up) : Quaternion.identity;
    }

    /// <summary>
    /// <paramref name="boundariesRoot"/> 로컬 축으로 경계 4개(Boundary_XMin/XMax/ZMin/ZMax)의 안쪽 면과 바닥 높이(경계 박스 밑면)를 읽는다.
    /// 넷 중 하나라도 없으면 false.
    /// </summary>
    public static bool TryReadArenaLocal(Transform boundariesRoot, out float minX, out float maxX, out float minZ, out float maxZ, out float floorY)
    {
        minX = maxX = minZ = maxZ = floorY = 0f;
        if (boundariesRoot == null)
            return false;

        int found = 0;
        float bottomSum = 0f;
        foreach (var box in boundariesRoot.GetComponentsInChildren<BoxCollider>(true))
        {
            var c = boundariesRoot.InverseTransformPoint(box.transform.TransformPoint(box.center));
            var h = boundariesRoot.InverseTransformVector(box.transform.TransformVector(box.size * 0.5f));
            float hx = Mathf.Abs(h.x), hy = Mathf.Abs(h.y), hz = Mathf.Abs(h.z);
            switch (box.name)
            {
                case "Boundary_XMin": minX = c.x + hx; found |= 1; break;
                case "Boundary_XMax": maxX = c.x - hx; found |= 2; break;
                case "Boundary_ZMin": minZ = c.z + hz; found |= 4; break;
                case "Boundary_ZMax": maxZ = c.z - hz; found |= 8; break;
                default: continue;
            }

            bottomSum += c.y - hy;
        }

        if (found != 15)
            return false;

        floorY = bottomSum / 4f;
        return true;
    }

    /// <summary>경계에서 계산한 월드 모서리 4개(폴백) — 방 로컬로 계산한 뒤 경계 루트 트랜스폼으로 옮긴다(회전된 룸 대응).</summary>
    public static bool TryComputeFallbackCorners(Transform boundariesRoot, List<Pose> output)
    {
        if (!TryReadArenaLocal(boundariesRoot, out var minX, out var maxX, out var minZ, out var maxZ, out var floorY))
            return false;

        var local = new Vector3[CornerCount];
        ComputeLocalCorners(minX, maxX, minZ, maxZ, floorY, Inset, local);
        var center = boundariesRoot.TransformPoint(new Vector3((minX + maxX) * 0.5f, floorY, (minZ + maxZ) * 0.5f));
        for (int i = 0; i < CornerCount; i++)
        {
            var world = boundariesRoot.TransformPoint(local[i]);
            output.Add(new Pose(world, FaceTowards(world, center)));
        }

        return true;
    }

    /// <summary>
    /// [서버] 씬의 모서리 4지점. <see cref="GroupName"/> 그룹의 Corner1~4 가 다 있으면 그것, 아니면 경계 계산 폴백(경고 1회).
    /// 둘 다 없으면 false(호출자는 첫 스폰 공식으로 떨어진다).
    /// </summary>
    public static bool TryCollectCorners(List<Pose> output, out bool fromPrefab)
    {
        output.Clear();
        fromPrefab = false;

        var group = GameObject.Find(GroupName);
        if (group != null)
        {
            for (int i = 0; i < CornerCount; i++)
            {
                var corner = group.transform.Find(CornerName(i));
                if (corner == null)
                    break;
                output.Add(new Pose(corner.position, corner.rotation));
            }

            if (output.Count == CornerCount)
            {
                fromPrefab = true;
                return true;
            }

            output.Clear();
        }

        var boundaries = GameObject.Find(BoundariesName);
        if (boundaries != null && TryComputeFallbackCorners(boundaries.transform, output))
        {
            if (!s_warnedFallback)
            {
                s_warnedFallback = true;
                Debug.LogWarning(
                    $"[Rejoin] 보스 룸 '{GroupName}/Corner1~4' 를 못 찾아 {BoundariesName} 안쪽 면에서 {Inset}m 인셋으로 계산한다 " +
                    $"(그룹 {(group != null ? "있음·자식 부족" : "없음")}). Tools/Map/Authoring/Add Boss Room Rejoin Corners 로 다시 만들 수 있다.");
            }

            return true;
        }

        if (!s_warnedMissing)
        {
            s_warnedMissing = true;
            Debug.LogWarning($"[Rejoin] 보스 룸 모서리도 {BoundariesName} 도 없다 — 보스 룸 재합류를 첫 스폰 공식으로 둔다.");
        }

        output.Clear();
        return false;
    }

    /// <summary>
    /// 보스 룸 판정(메모 §3.4 추천). Combat 이면 무조건, 아니면 Idle 이 아니고 다른 플레이어(영구 사망 제외)가 BossArea 안에 있을 때.
    /// Phase 만 보면 FailedSafe(연출 중단 — 이미 아레나 안)를, 위치만 보면 재도전 리셋 뒤 Idle 의 아레나 잔류를 놓친다.
    /// </summary>
    public static bool IsInBossRoom(BossEncounterPhase phase, bool anyOtherPlayerInBossArea)
    {
        if (phase == BossEncounterPhase.Combat)
            return true;

        return phase != BossEncounterPhase.Idle && anyOtherPlayerInBossArea;
    }

    /// <summary>점이 박스의 수평(XZ) 범위 안인가 — 박스 로컬 축으로 본다(회전된 룸 대응). 높이는 보지 않는다(점프·대시 중에도 안).</summary>
    public static bool IsInsideBoxXZ(BoxCollider box, Vector3 worldPoint)
    {
        var local = box.transform.InverseTransformPoint(worldPoint) - box.center;
        var half = box.size * 0.5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.z;
    }

    /// <summary>[서버] 위치 하나라도 씬의 <c>BossArea</c> 태그 박스 안에 있는가.</summary>
    public static bool AnyInsideBossArea(IReadOnlyList<Vector3> positions)
    {
        if (positions == null || positions.Count == 0)
            return false;

        foreach (var area in GameObject.FindGameObjectsWithTag(BossAreaTag))
        {
            if (!area.TryGetComponent(out BoxCollider box))
                continue;

            for (int i = 0; i < positions.Count; i++)
            {
                if (IsInsideBoxXZ(box, positions[i]))
                    return true;
            }
        }

        return false;
    }
}
