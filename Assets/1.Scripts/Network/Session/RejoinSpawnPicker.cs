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
        IReadOnlyList<Vector3> otherPlayerPositions)
    {
        InBossRoom = inBossRoom;
        HasBaseSpawnPoint = hasBaseSpawnPoint;
        BasePosition = basePosition;
        BaseRotation = baseRotation;
        SpawnOffset = spawnOffset;
        OtherPlayerPositions = otherPlayerPositions;
    }

    /// <summary>보스 룸 안에서 재합류 — 모서리 선택은 R3c 가 채운다. 지금은 첫 스폰 공식으로 떨어진다.</summary>
    public bool InBossRoom { get; }
    /// <summary>맵의 첫 스폰 지점(<c>MapGenerator.GetRoleSlot(ZoneRole.PlayerSpawn)</c>)을 찾았는가. false 면 처음 스폰과 같은 폴백 좌표.</summary>
    public bool HasBaseSpawnPoint { get; }
    public Vector3 BasePosition { get; }
    public Quaternion BaseRotation { get; }
    /// <summary>처음 스폰과 같은 띄움(<c>spawn_Offset</c>, 기본 (0,10,0) — 낙하 착지).</summary>
    public Vector3 SpawnOffset { get; }
    /// <summary>이미 맵에 서 있는 다른 플레이어 위치(재합류자 본인 제외). null = 없음.</summary>
    public IReadOnlyList<Vector3> OtherPlayerPositions { get; }
}

/// <summary>선택 결과 — 위치·회전과 쓴 칸 번호(로그용).</summary>
public readonly struct RejoinSpawnPick
{
    public RejoinSpawnPick(Pose pose, int slotIndex)
    {
        Pose = pose;
        SlotIndex = slotIndex;
    }

    public Pose Pose { get; }
    public int SlotIndex { get; }
}

/// <summary>
/// 판 도중 재합류 스폰 지점(C14 설계 메모 §3.4, 사용자 답 Q-A) — 순수 함수(EditMode 테스트 대상).
/// <list type="bullet">
/// <item>보스 룸 밖: 맵 첫 스폰 지점 + <c>spawn_Offset</c> + <c>right * (칸 * 2m)</c> — 처음 스폰(<see cref="SlotPose"/>, 판 시작 SpawnAllPlayers)과 같은 공식.
/// 칸은 다른 플레이어가 서 있지 않은(수평 <see cref="OccupiedRadius"/> 안에 아무도 없는) 가장 앞 칸이다. 다른 플레이어가 N명이면 0..N 안에 반드시 빈 칸이 있다.</item>
/// <item>보스 룸 안: 네 모서리 중 다른 플레이어·보스에서 가장 먼 곳 — R3c 가 채운다. 지금은 위 공식으로 떨어진다.</item>
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
        // TODO(C14 R3c): context.InBossRoom 이면 모서리 4지점(프리팹 RejoinCornerPoints + 계산 폴백) 중
        // 다른 플레이어·보스까지 최소 거리가 가장 큰 곳(동률이면 앞 index). 지금은 첫 스폰 공식으로 떨어진다.
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
