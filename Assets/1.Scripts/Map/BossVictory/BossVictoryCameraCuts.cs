using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 — ZZZ 글리치 4단계 카메라 컷(PLAN-boss-victory-glitch). 순수 계산만(테스트 대상 — <c>BossVictoryCameraCutTests</c>).
/// 컷 목록 = 타임라인 '카메라 컷' 트랙(<see cref="BossVictoryTimeline.cameraCut"/> 클립 = 시작 시각 · <see cref="BossVictoryTimeline.cameraCutShots"/> = 같은 순서의 컷 값, 255),
/// 시계는 글리치 시각(<see cref="BossVictoryGlitchPresentation.GlitchTime"/>).
/// 첫 컷 전까지는 게임 카메라 그대로. 컷 사이는 블렌드 없이 끊는다(전환은 글리치 블록 펄스가 가린다). 컷 안에서는 시작 → 끝 화면으로 이동(255 후속 2).
/// </summary>
public static class BossVictoryCameraCuts
{
    /// <summary>컷 기준점 — 카메라 위치·회전을 이 점 + 처치자→보스 수평 축 좌표계로 저장한다(어디서 죽든 같은 구도).</summary>
    public enum Anchor
    {
        Middle,  // 처치자·보스 발밑 가운데
        Killer,  // 처치자 발밑
        Boss,    // 보스 발밑(트랜스폼 위치)
    }

    /// <summary>시작 → 끝 화면 이동 곡선.</summary>
    public enum Ease
    {
        Linear,
        EaseInOut,
        EaseIn,
        EaseOut,
    }

    /// <summary>
    /// 컷 하나(255 후속 2) = 시작 화면 → 끝 화면 2점. 화면 = 기준점 좌표계의 카메라 위치·회전 + FOV.
    /// 기준 좌표계: 원점 = <see cref="anchor"/> 점 · +Z = 처치자 → 보스 수평 방향 · +Y = 위(옛 yaw 0 = 처치자 쪽에서 보스를 봄과 같은 방향 정의).
    /// 끝 화면이 없으면(<see cref="hasEnd"/> 끔) 고정 컷. 컷 구간 = 클립 시작 ~ 다음 컷 시작(마지막 = 로비 전환), 그동안 곡선대로 보간.
    /// </summary>
    [Serializable]
    public struct Cut
    {
        public Anchor anchor;
        [Tooltip("시작 화면 카메라 위치(기준 좌표계 m).")]
        public Vector3 startPosition;
        [Tooltip("시작 화면 카메라 회전(기준 좌표계).")]
        public Quaternion startRotation;
        [Range(10f, 90f)] public float startFov;
        [Tooltip("끝 화면이 있으면 컷 구간 동안 시작 → 끝으로 이동(위치·회전·FOV). 끄면 고정 컷.")]
        public bool hasEnd;
        public Vector3 endPosition;
        public Quaternion endRotation;
        [Range(10f, 90f)] public float endFov;
        public Ease ease;
        [Tooltip("이 컷 동안 카메라를 가리는 벽(앞면이 카메라를 향하지 않는 벽)을 끈다(이동 중이면 매 프레임 그 위치로). 끄면 이 컷 동안 벽 숨김 없음 — 숨겼던 벽도 다시 보인다.")]
        public bool hideWalls;

        /// <summary>새 컷 기본값 — 옛 기본 와이드 구도(가운데 기준·거리 16·내려다보는 각 22·방위 50·초점 높이 1.5·FOV 40) · 고정 · 벽 숨김 켬.</summary>
        public static Cut Default
        {
            get
            {
                Orbit(16f, 22f, 50f, 1.5f, out Vector3 p, out Quaternion r);
                return new Cut
                {
                    anchor = Anchor.Middle, startPosition = p, startRotation = r, startFov = 40f,
                    endPosition = p, endRotation = r, endFov = 40f, ease = Ease.EaseInOut, hideWalls = true,
                };
            }
        }
    }

    /// <summary>카메라 위치 계산 입력 — 이 피어가 컷 시작 순간 본 위치(복제 위치라 피어 간 미세 차이 허용).</summary>
    public struct Subjects
    {
        public Vector3 Killer;
        /// <summary>보스 발밑(트랜스폼 위치).</summary>
        public Vector3 BossBase;
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

    public static Vector3 AnchorPoint(Anchor anchor, in Subjects s) => anchor switch
    {
        Anchor.Killer => s.Killer,
        Anchor.Boss => s.BossBase,
        _ => Vector3.Lerp(s.Killer, s.BossBase, 0.5f),
    };

    /// <summary>기준 좌표계 — 원점 = 기준점 · 회전 = 처치자→보스 수평 축을 +Z 로.</summary>
    public static void Frame(Anchor anchor, in Subjects s, out Vector3 origin, out Quaternion rotation)
    {
        origin = AnchorPoint(anchor, s);
        rotation = Quaternion.LookRotation(Axis(s), Vector3.up);
    }

    /// <summary>월드 카메라 자세 → 기준 좌표계(저장용).</summary>
    public static void ToLocal(Anchor anchor, in Subjects s, Vector3 worldPosition, Quaternion worldRotation, out Vector3 localPosition, out Quaternion localRotation)
    {
        Frame(anchor, s, out Vector3 origin, out Quaternion frame);
        Quaternion inv = Quaternion.Inverse(frame);
        localPosition = inv * (worldPosition - origin);
        localRotation = Normalize(inv * worldRotation);
    }

    /// <summary>기준 좌표계 → 월드 카메라 자세.</summary>
    public static void ToWorld(Anchor anchor, in Subjects s, Vector3 localPosition, Quaternion localRotation, out Vector3 worldPosition, out Quaternion worldRotation)
    {
        Frame(anchor, s, out Vector3 origin, out Quaternion frame);
        worldPosition = origin + frame * localPosition;
        worldRotation = Normalize(frame * localRotation);
    }

    /// <summary>곡선 값(0~1 → 0~1).</summary>
    public static float Evaluate(Ease ease, float u)
    {
        u = Mathf.Clamp01(u);
        return ease switch
        {
            Ease.EaseIn => u * u,
            Ease.EaseOut => 1f - (1f - u) * (1f - u),
            Ease.EaseInOut => u * u * (3f - 2f * u),
            _ => u,
        };
    }

    /// <summary>컷 진행 <paramref name="u"/>(0 = 컷 시작, 1 = 다음 컷 시작) 의 월드 카메라 자세·FOV. 끝 화면이 없으면 시작 화면 고정.</summary>
    public static void Pose(in Cut cut, in Subjects s, float u, out Vector3 position, out Quaternion rotation, out float fov)
    {
        Vector3 lp = cut.startPosition;
        Quaternion lr = Normalize(cut.startRotation);
        fov = cut.startFov;
        if (cut.hasEnd)
        {
            float k = Evaluate(cut.ease, u);
            lp = Vector3.Lerp(cut.startPosition, cut.endPosition, k);
            lr = Quaternion.Slerp(lr, Normalize(cut.endRotation), k);
            fov = Mathf.Lerp(cut.startFov, cut.endFov, k);
        }
        ToWorld(cut.anchor, s, lp, lr, out position, out rotation);
    }

    /// <summary>옛 매개변수 구도(초점 높이에서 거리·내려다보는 각·방위로 돌아본 카메라)를 기준 좌표계 자세로 — 새 컷 기본값용.</summary>
    public static void Orbit(float distance, float pitch, float yaw, float lookHeight, out Vector3 localPosition, out Quaternion localRotation)
    {
        localRotation = Quaternion.Euler(pitch, yaw, 0f);
        localPosition = Vector3.up * lookHeight - localRotation * Vector3.forward * distance;
    }

    // 직렬화 기본값(0,0,0,0) 같은 퇴화 쿼터니언은 단위로.
    static Quaternion Normalize(Quaternion q)
    {
        float m = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return m < 1e-6f ? Quaternion.identity : new Quaternion(q.x / m, q.y / m, q.z / m, q.w / m);
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
