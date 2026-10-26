using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 맵 포드 착지 연출(<see cref="MapPodArrival"/>, 할 일 100 C3)의 순수 규칙 — 연출을 켤지·착지점·좌석·도착 연출 끝 판정.
/// 네트워크·씬을 모른다. 서버와 모든 피어가 같은 복제 값으로 같은 답을 얻는다.
/// </summary>
public static class PodArrivalRules
{
    /// <summary>
    /// 맵 포드 착지 연출을 못(안) 하는 이유(없으면 null). 있으면 연출 없이 지금처럼(낙하 스폰·즉시 조작·S0) 간다.
    /// 로비 포드 연출을 거쳐 온 진입(<see cref="MapEntryKind.Lobby"/>)만 연출한다 — Dev Boot·재시작·폴백 로딩은 Direct/Restart.
    /// </summary>
    public static string SkipReason(MapEntryKind entryKind, bool hasSettings, bool hasPodPrefab, bool hasGameManager)
    {
        if (entryKind != MapEntryKind.Lobby) return $"진입 종류 {entryKind}";
        if (!hasSettings) return "PodDropSettings 가 비었다";
        if (!hasPodPrefab) return "포드 프리팹이 비었다";
        if (!hasGameManager) return "GameManager 가 없어 로딩 걷힘(OnMainGameReady)을 못 듣는다";
        return null;
    }

    /// <summary>포드 착지점 = 스폰 슬롯 + 슬롯 회전 기준 로컬 오프셋. 회전은 슬롯 회전 그대로.</summary>
    public static Pose LandingPose(Vector3 slotPosition, Quaternion slotRotation, Vector3 localOffset) =>
        new Pose(slotPosition + slotRotation * localOffset, slotRotation);

    /// <summary>스폰 순서의 좌석 오프셋 — 좌석보다 사람이 많으면 돌려 쓴다. 좌석이 없으면 0(착지점).</summary>
    public static Vector3 SeatOffset(int spawnIndex, IReadOnlyList<Vector3> seatOffsets)
    {
        if (seatOffsets == null || seatOffsets.Count == 0) return Vector3.zero;
        return seatOffsets[Mathf.Max(0, spawnIndex) % seatOffsets.Count];
    }

    /// <summary>좌석 Pose — 착지점 + 착지 회전 기준 로컬 오프셋. 낙하 높이 없이 그 자리에 선다.</summary>
    public static Pose SeatPose(Pose landing, int spawnIndex, IReadOnlyList<Vector3> seatOffsets) =>
        new Pose(landing.position + landing.rotation * SeatOffset(spawnIndex, seatOffsets), landing.rotation);

    /// <summary>
    /// [전 피어] 도착 연출이 끝났는가 — 튜토리얼 S0(착지 타이머·자막·목표 표시)의 시작 조건.
    /// 연출이 꺼진 진입(Direct·Restart·폴백)은 즉시 참. 연출 컴포넌트가 아직 스폰 전이면 거짓(켜질지 모른다), 시작 시각 전이면 거짓.
    /// </summary>
    /// <param name="spawned">맵 연출 NetworkBehaviour 가 스폰돼 복제 값을 읽을 수 있는가.</param>
    /// <param name="armed">서버가 이번 진입에 연출을 켰는가(복제).</param>
    /// <param name="startAt">시작 시각(서버시간, 0 = 아직).</param>
    /// <param name="releaseAt">시간표 해제 시각(시작 기준 초).</param>
    public static bool IsArrivalDone(bool spawned, bool armed, double startAt, double now, float releaseAt)
    {
        if (!spawned) return false;
        if (!armed) return true;
        if (startAt <= 0d) return false;
        return now - startAt >= releaseAt;
    }

    /// <summary>
    /// 착지 순간 효과(이펙트·흔들림)를 지금 낼까 — 착지 시각을 지났고 아직 안 냈으며, 디졸브가 이미 시작된 늦은 피어는 건너뛴다.
    /// 착지 여운이 0(착지 = 디졸브 시작)이면 건너뛸 구간이 없어 처음 본 순간 낸다.
    /// </summary>
    public static bool ShouldPlayImpact(double elapsed, float impactAt, float dissolveAt, bool alreadyPlayed)
    {
        if (alreadyPlayed || elapsed < impactAt) return false;
        return dissolveAt <= impactAt || elapsed < dissolveAt;
    }
}
