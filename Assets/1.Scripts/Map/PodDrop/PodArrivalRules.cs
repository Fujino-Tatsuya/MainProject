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
    /// 연출이 꺼진 진입(Direct·Restart·폴백)은 즉시 참. 연출 컴포넌트가 아직 스폰 전이면 거짓(켜질지 모른다), 시작 시각·열기 끝 이벤트 전이면 거짓.
    /// </summary>
    /// <param name="spawned">맵 연출 NetworkBehaviour 가 스폰돼 복제 값을 읽을 수 있는가.</param>
    /// <param name="armed">서버가 이번 진입에 연출을 켰는가(복제).</param>
    /// <param name="startAt">시작 시각(서버시간, 0 = 아직).</param>
    /// <param name="openedAt">해치 열기·날개 접기 끝 이벤트 시각(서버시간, 0 = 아직) — 디졸브 시작.</param>
    /// <param name="releaseAfterOpen">열기 끝 → 해제(초) = 디졸브 + 레터박스 아웃.</param>
    public static bool IsArrivalDone(bool spawned, bool armed, double startAt, double openedAt, double now, float releaseAfterOpen)
    {
        if (!spawned) return false;
        if (!armed) return true;
        if (startAt <= 0d || openedAt <= 0d) return false;
        return now - openedAt >= releaseAfterOpen;
    }

    /// <summary>
    /// 착지 순간 효과(이펙트·흔들림)를 지금 낼까 — 지면에 닿은 시각을 지났고 아직 안 냈으며, 디졸브가 이미 시작된 늦은 피어는 건너뛴다.
    /// 착지 = 디졸브 시작이면 건너뛸 구간이 없어 처음 본 순간 낸다. 아직 안 온 시각은 +∞(<see cref="PodDropTimeline.NotYet"/>).
    /// </summary>
    public static bool ShouldPlayImpact(double elapsed, double impactAt, double dissolveAt, bool alreadyPlayed)
    {
        if (alreadyPlayed || elapsed < impactAt) return false;
        return dissolveAt <= impactAt || elapsed < dissolveAt;
    }

    /// <summary>
    /// 해치를 열기 클립 0프레임(닫힌 자세)에 고정해 둘까(할 일 106) — 연출 시작 전·낙하·날개/다리 펴기·착륙 내내 참, 지면에 닿은 순간부터 거짓(열기 재생).
    /// 고정 안 하면 Hatches 레이어가 PassThrough 라 베이스 DP_Idle 의 열린 해치가 보인다. 펴기 끝이 아직이면 착지 시각은 +∞ 라 계속 고정.
    /// </summary>
    public static bool ShouldHoldHatchClosed(bool started, double elapsed, double touchdownAt) =>
        !started || elapsed < touchdownAt;
}
