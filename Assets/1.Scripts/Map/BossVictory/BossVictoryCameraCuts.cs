using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 — ZZZ 글리치 4단계 카메라 컷(PLAN-boss-victory-glitch). 순수 계산만(테스트 대상 — <c>BossVictoryCameraCutTests</c>).
/// 컷 목록 = 타임라인 '카메라 컷' 트랙(<see cref="BossVictoryTimeline.cameraCut"/> 클립 = 시작 시각 · <see cref="BossVictoryTimeline.cameraCutShots"/> = 같은 순서의 컷 값, 255),
/// 시계는 글리치 시각(<see cref="BossVictoryGlitchPresentation.GlitchTime"/>).
/// 첫 컷 전까지는 게임 카메라 그대로. 컷은 블렌드 없이 끊는다(전환은 글리치 블록 펄스가 가린다).
/// </summary>
public static class BossVictoryCameraCuts
{
    public enum Kind
    {
        Wide,         // 처치자·보스 가운데를 본다(레퍼런스 컷 1·3)
        BossCloseup,  // 보스 몸 중심을 가까이(레퍼런스 컷 2 — 처치자는 뒤에 작게)
        KillerFront,  // 처치자 정면 클로즈업(처음 계획 — 기본 목록엔 없음)
    }

    /// <summary>
    /// 컷 하나. 방위(yaw)는 <b>처치자 → 보스 수평 방향 기준</b>: 0 = 처치자 쪽에서 보스 쪽을 봄 · 180 = 반대편에서 처치자 쪽을 봄.
    /// 카메라 = 초점에서 시선 반대 방향으로 거리만큼, 초점을 바라본다. 시작 시각은 타임라인 클립 시작(다음 컷 시작까지 유지).
    /// </summary>
    [Serializable]
    public struct Cut
    {
        public Kind kind;
        [Tooltip("초점에서 카메라까지 거리(m).")]
        [Min(0.1f)] public float distance;
        [Tooltip("내려다보는 각(도, + = 위에서 아래로).")]
        [Range(-30f, 89f)] public float pitch;
        [Tooltip("방위(도) — 0 = 처치자 쪽에서 보스를 봄, 180 = 반대편에서 처치자를 봄, + = 시계 방향.")]
        [Range(-180f, 180f)] public float yaw;
        [Tooltip("초점 높이 보정(m). 와이드·처치자 = 발밑 기준, 보스 클로즈업 = 보스 몸 중심 기준.")]
        public float lookHeight;
        [Range(10f, 90f)] public float fov;
        [Tooltip("이 컷 동안 카메라를 가리는 벽(앞면이 카메라를 향하지 않는 벽)을 끈다. 끄면 이 컷 동안 벽 숨김 없음 — 숨겼던 벽도 다시 보인다.")]
        public bool hideWalls;

        /// <summary>새 컷 기본값 — 와이드(옛 기본 목록 첫 컷과 같은 구도) · 벽 숨김 켬.</summary>
        public static Cut Default => new Cut { kind = Kind.Wide, distance = 16f, pitch = 22f, yaw = 50f, lookHeight = 1.5f, fov = 40f, hideWalls = true };
    }

    /// <summary>카메라 위치 계산 입력 — 이 피어가 컷 시작 순간 본 위치(복제 위치라 피어 간 미세 차이 허용).</summary>
    public struct Subjects
    {
        public Vector3 Killer;
        /// <summary>보스 발밑(트랜스폼 위치).</summary>
        public Vector3 BossBase;
        /// <summary>보스 몸 중심(렌더러 바운즈 중심, 못 구하면 발밑 + 1m).</summary>
        public Vector3 BossCenter;
    }

    /// <summary>시각 <paramref name="t"/> 에 켜진 컷 번호 — 클립 시작이 t 이하인 것 중 가장 늦은 것(클립 길이와 무관 — 다음 컷 시작까지 유지). 없으면 -1(게임 카메라). 목록 순서와 무관.</summary>
    public static int ActiveIndex(IReadOnlyList<BossVictoryClip> cuts, float t)
    {
        if (cuts == null || t < 0f) return -1;
        int best = -1;
        float bestStart = float.NegativeInfinity;
        for (int i = 0; i < cuts.Count; i++)
        {
            float s = cuts[i].start;
            if (s <= t && s >= bestStart) { best = i; bestStart = s; }
        }
        return best;
    }

    /// <summary>처치자 → 보스 수평 방향(퇴화하면 월드 +Z).</summary>
    public static Vector3 Axis(in Subjects s)
    {
        Vector3 a = Flat(s.BossBase - s.Killer);
        return a.sqrMagnitude > 0.0001f ? a.normalized : Vector3.forward;
    }

    public static Vector3 Focus(in Cut cut, in Subjects s)
    {
        Vector3 basePoint = cut.kind switch
        {
            Kind.BossCloseup => s.BossCenter,
            Kind.KillerFront => s.Killer,
            _ => Vector3.Lerp(s.Killer, s.BossBase, 0.5f),
        };
        return basePoint + Vector3.up * cut.lookHeight;
    }

    public static void Pose(in Cut cut, in Subjects s, out Vector3 position, out Quaternion rotation)
    {
        Vector3 axis = Axis(s);
        float axisYaw = Mathf.Atan2(axis.x, axis.z) * Mathf.Rad2Deg;
        rotation = Quaternion.Euler(cut.pitch, axisYaw + cut.yaw, 0f);
        position = Focus(cut, s) - rotation * Vector3.forward * cut.distance;
    }

    // ── 벽 숨김(옛 연출 할 일 200 과 같은 기준 — Legacy 에 기대지 않게 옮겨 둠) ──

    /// <summary>벽 바운즈의 방 안쪽 법선(수평). 얇은 축 · 부호 = <paramref name="interior"/> 쪽. 정사각에 가까우면 안쪽 방향 그대로.</summary>
    public static Vector3 WallInwardNormal(Bounds wall, Vector3 interior, float squareTolerance = 0.5f)
    {
        Vector3 toInside = Flat(interior - wall.center);
        if (Mathf.Abs(wall.size.x - wall.size.z) < squareTolerance)
            return toInside.sqrMagnitude > 0.0001f ? toInside.normalized : Vector3.zero;
        Vector3 axis = wall.size.x < wall.size.z ? Vector3.right : Vector3.forward;
        float side = Vector3.Dot(axis, toInside);
        if (Mathf.Abs(side) < 0.0001f) return Vector3.zero;
        return side > 0f ? axis : -axis;
    }

    /// <summary>앞면(방 안쪽 면)이 확실히 카메라를 향한 벽만 남긴다 — 카메라가 벽 바깥이거나 시선과 법선이 어긋나면(≤ 문턱) 숨김.</summary>
    public static bool ShouldHideWall(Vector3 inwardNormal, Vector3 wallCenter, Vector3 cameraPosition, Vector3 cameraForward, float facingThreshold)
    {
        Vector3 n = Flat(inwardNormal);
        if (n.sqrMagnitude < 0.0001f) return false;
        n.Normalize();
        if (Vector3.Dot(n, Flat(cameraPosition - wallCenter)) < 0f) return true;
        Vector3 back = Flat(-cameraForward);
        if (back.sqrMagnitude < 0.0001f) return false;
        return Vector3.Dot(n, back.normalized) <= facingThreshold;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
