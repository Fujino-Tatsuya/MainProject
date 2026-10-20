using System.Collections.Generic;
using UnityEngine;

/// <summary>재합류 스폰 지점 선택 입력 — 서버가 스폰 순간에 채운다(NetworkLoadingFlowController).</summary>
public readonly struct RejoinSpawnContext
{
    public RejoinSpawnContext(
        bool inBossRoom,
        bool hasBaseSpawnPoint,
        Vector3 basePosition,
        Quaternion baseRotation,
        Vector3 spawnOffset,
        IReadOnlyList<Vector3> otherPlayerPositions,
        IReadOnlyList<Pose> bossRoomCorners = null,
        IReadOnlyList<Vector3> bossPositions = null)
    {
        InBossRoom = inBossRoom;
        HasBaseSpawnPoint = hasBaseSpawnPoint;
        BasePosition = basePosition;
        BaseRotation = baseRotation;
        SpawnOffset = spawnOffset;
        OtherPlayerPositions = otherPlayerPositions;
        BossRoomCorners = bossRoomCorners;
        BossPositions = bossPositions;
    }

    /// <summary>보스 룸 안에서 재합류(<see cref="BossRoomRejoin.IsInBossRoom"/>) — 모서리가 있으면 모서리, 없으면 첫 스폰 공식.</summary>
    public bool InBossRoom { get; }
    /// <summary>맵의 첫 스폰 지점(<c>MapGenerator.GetRoleSlot(ZoneRole.PlayerSpawn)</c>)을 찾았는가. false 면 처음 스폰과 같은 폴백 좌표.</summary>
    public bool HasBaseSpawnPoint { get; }
    public Vector3 BasePosition { get; }
    public Quaternion BaseRotation { get; }
    /// <summary>처음 스폰과 같은 띄움(<c>spawn_Offset</c>, 기본 (0,10,0) — 낙하 착지).</summary>
    public Vector3 SpawnOffset { get; }
    /// <summary>이미 맵에 서 있는 다른 플레이어 위치(재합류자 본인 제외). null = 없음.</summary>
    public IReadOnlyList<Vector3> OtherPlayerPositions { get; }
    /// <summary>보스 룸 모서리 월드 Pose(프리팹 <c>RejoinCornerPoints</c> 또는 계산 폴백, <see cref="BossRoomRejoin.TryCollectCorners"/>). null·빈 목록 = 없음.</summary>
    public IReadOnlyList<Pose> BossRoomCorners { get; }
    /// <summary>살아 있는 보스 위치. null = 없음.</summary>
    public IReadOnlyList<Vector3> BossPositions { get; }
}

/// <summary>선택 결과 — 위치·회전과 쓴 칸 번호 또는 모서리 index(로그용).</summary>
public readonly struct RejoinSpawnPick
{
    public RejoinSpawnPick(Pose pose, int slotIndex, bool isBossRoomCorner = false)
    {
        Pose = pose;
        SlotIndex = slotIndex;
        IsBossRoomCorner = isBossRoomCorner;
    }

    public Pose Pose { get; }
    /// <summary>첫 스폰 칸 번호, <see cref="IsBossRoomCorner"/> 면 모서리 index(0..3).</summary>
    public int SlotIndex { get; }
    public bool IsBossRoomCorner { get; }
}

/// <summary>
/// 판 도중 재합류 스폰 지점(C14 설계 메모 §3.4, 사용자 답 Q-A) — 순수 함수(EditMode 테스트 대상).
/// <list type="bullet">
/// <item>보스 룸 밖: 맵 첫 스폰 지점 + <c>spawn_Offset</c> + <c>right * (칸 * 2m)</c> — 처음 스폰(<see cref="SlotPose"/>, 판 시작 SpawnAllPlayers)과 같은 공식.
/// 칸은 다른 플레이어가 서 있지 않은(수평 <see cref="OccupiedRadius"/> 안에 아무도 없는) 가장 앞 칸이다. 다른 플레이어가 N명이면 0..N 안에 반드시 빈 칸이 있다.</item>
/// <item>보스 룸 안: 네 모서리 중 다른 플레이어·보스까지의 (수평) 최소 거리가 가장 큰 곳, 동률이면 앞 index(결정적).
/// 모서리 Pose 그대로 선다(텔레포트 도착 지점과 같은 바닥 높이 — spawn_Offset 낙하를 쓰지 않는다). 모서리가 없으면 위 공식.</item>
/// </list>
/// 마지막 전멸 리스폰 지점은 쓰지 않는다(Q-A).
/// </summary>
public static class RejoinSpawnPicker
{
    /// <summary>칸 사이 간격(m) — 처음 스폰 공식과 같다.</summary>
    public const float SlotSpacing = 2f;
    /// <summary>이 수평 거리(m) 안에 다른 플레이어가 있으면 그 칸은 차 있다.</summary>
    public const float OccupiedRadius = 1f;
    /// <summary>첫 스폰 지점을 못 찾았을 때의 높이 — 처음 스폰 폴백과 같다.</summary>
    public const float FallbackHeight = 5f;

    public static RejoinSpawnPick Pick(in RejoinSpawnContext context)
    {
        var corners = context.BossRoomCorners;
        if (context.InBossRoom && corners != null && corners.Count > 0)
        {
            var corner = FarthestCorner(corners, context.OtherPlayerPositions, context.BossPositions);
            return new RejoinSpawnPick(corners[corner], corner, isBossRoomCorner: true);
        }

        var slot = FirstFreeSlot(context);
        return new RejoinSpawnPick(
            SlotPose(context.HasBaseSpawnPoint, context.BasePosition, context.BaseRotation, context.SpawnOffset, slot),
            slot);
    }

    /// <summary>
    /// 처음 스폰과 재합류가 같이 쓰는 칸 공식. 기준점이 있으면 기준 + offset + right * (index*2), 없으면 (index*2, 5, 0)·회전 없음.
    /// </summary>
    public static Pose SlotPose(bool hasBase, Vector3 basePosition, Quaternion baseRotation, Vector3 spawnOffset, int slotIndex)
    {
        if (hasBase)
        {
            var spacing = Vector3.right * (slotIndex * SlotSpacing);
            return new Pose(basePosition + spawnOffset + spacing, baseRotation);
        }

        return new Pose(new Vector3(slotIndex * SlotSpacing, FallbackHeight, 0f), Quaternion.identity);
    }

    /// <summary>다른 플레이어가 서 있지 않은 가장 앞 칸(결정적).</summary>
    public static int FirstFreeSlot(in RejoinSpawnContext context)
    {
        var others = context.OtherPlayerPositions;
        var otherCount = others != null ? others.Count : 0;
        // 다른 사람이 N명이면 0..N 중 하나는 반드시 비어 있다(비둘기집).
        for (var slot = 0; slot < otherCount; slot++)
        {
            var position = SlotPose(context.HasBaseSpawnPoint, context.BasePosition, context.BaseRotation, context.SpawnOffset, slot).position;
            if (!IsOccupied(position, others))
                return slot;
        }

        return otherCount;
    }

    /// <summary>각 모서리에서 다른 플레이어·보스까지 수평 최소 거리가 가장 큰 모서리. 동률(아무도 없음 포함)이면 앞 index.</summary>
    public static int FarthestCorner(IReadOnlyList<Pose> corners, IReadOnlyList<Vector3> others, IReadOnlyList<Vector3> bosses)
    {
        var best = 0;
        var bestDistanceSq = float.NegativeInfinity;
        for (var i = 0; i < corners.Count; i++)
        {
            var position = corners[i].position;
            var nearestSq = Mathf.Min(NearestHorizontalSq(position, others), NearestHorizontalSq(position, bosses));
            if (nearestSq > bestDistanceSq)
            {
                best = i;
                bestDistanceSq = nearestSq;
            }
        }

        return best;
    }

    private static float NearestHorizontalSq(Vector3 from, IReadOnlyList<Vector3> targets)
    {
        var nearest = float.PositiveInfinity;
        if (targets == null)
            return nearest;

        for (var i = 0; i < targets.Count; i++)
        {
            var dx = targets[i].x - from.x;
            var dz = targets[i].z - from.z;
            nearest = Mathf.Min(nearest, dx * dx + dz * dz);
        }

        return nearest;
    }

    private static bool IsOccupied(Vector3 slotPosition, IReadOnlyList<Vector3> others)
    {
        var radiusSq = OccupiedRadius * OccupiedRadius;
        for (var i = 0; i < others.Count; i++)
        {
            var dx = others[i].x - slotPosition.x;
            var dz = others[i].z - slotPosition.z;
            if (dx * dx + dz * dz < radiusSq)
                return true;
        }

        return false;
    }
}
