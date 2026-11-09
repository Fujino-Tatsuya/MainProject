using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 — ZZZ 글리치 4단계 카메라 컷(PLAN-boss-victory-glitch). 순수 계산만(테스트 대상 — <c>BossVictoryCameraCutTests</c>).
/// 컷 목록 = 타임라인 '카메라 컷' 트랙(<see cref="BossVictoryTimeline.cameraCut"/> 클립 = 시작 시각 · <see cref="BossVictoryTimeline.cameraCutShots"/> = 같은 순서의 컷 값, 255),
/// 시계는 글리치 시각(<see cref="BossVictoryGlitchPresentation.GlitchTime"/>).
/// 첫 컷 전까지는 게임 카메라 그대로. 컷 사이는 블렌드 없이 끊는다(전환은 글리치 블록 펄스가 가린다). 컷 안에서는 화면 키(시작·중간·끝)를 따라 이동(255 후속 6 — 옛 시작→끝 2점을 일반화).
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

    /// <summary>이동 곡선(백대쉬 곡선 · 옛 컷 시작→끝 곡선). 컷 화면 키 구간은 <see cref="BossVictoryKeyCurve"/>(같은 순서 + 계단).</summary>
    public enum Ease
    {
        Linear,
        EaseInOut,
        EaseIn,
        EaseOut,
    }

    /// <summary>컷 화면 키(255 후속 6) — 컷 구간 비율 위치의 기준 좌표계 카메라 위치·회전·FOV + 다음 키까지 곡선.</summary>
    [Serializable]
    public struct CutKey : IBossVictoryKey
    {
        [Range(0f, 1f)] public float u;
        public Vector3 position;
        public Quaternion rotation;
        [Range(10f, 90f)] public float fov;
        public BossVictoryKeyCurve curve;

        public float U => u;
        public BossVictoryKeyCurve Curve => curve;

        public static CutKey Make(float u, Vector3 position, Quaternion rotation, float fov, BossVictoryKeyCurve curve = BossVictoryKeyCurve.EaseInOut) =>
            new CutKey { u = u, position = position, rotation = rotation, fov = fov, curve = curve };
    }

    /// <summary>
    /// 컷 하나 = 화면 키 목록(255 후속 6 — 옛 시작 → 끝 2점 일반화). 화면 = 기준점 좌표계의 카메라 위치·회전 + FOV.
    /// 기준 좌표계: 원점 = <see cref="anchor"/> 점 · +Z = 처치자 → 보스 수평 방향 · +Y = 위(옛 yaw 0 = 처치자 쪽에서 보스를 봄과 같은 방향 정의).
    /// 키 1개 = 고정 컷 · 2개 이상 = 첫 키(u 0) → … → 끝 키(u 1). 컷 구간 = 클립 시작 ~ 다음 컷 시작(마지막 = 로비 전환), 키 u = 그 구간 비율.
    /// 🔴 구조체 복사는 키 목록을 같이 쓴다 — 붙여넣기는 <see cref="WithCopiedKeys"/>.
    /// </summary>
    [Serializable]
    public struct Cut
    {
        public Anchor anchor;
        [Tooltip("화면 키 — 시작(u 0)·중간·끝(u 1). 1개 = 고정 컷.")]
        public List<CutKey> keys;
        [Tooltip("키 3개 이상이면 위치를 부드러운 곡선(Catmull-Rom)으로 잇는다. 끄면 키 사이 직선.")]
        public bool smoothPath;
        [Tooltip("이 컷 동안 카메라를 가리는 벽(앞면이 카메라를 향하지 않는 벽)을 끈다(이동 중이면 매 프레임 그 위치로). 끄면 이 컷 동안 벽 숨김 없음 — 숨겼던 벽도 다시 보인다.")]
        public bool hideWalls;

        // 옛 2점 형식(255 후속 2) — 읽기만 해서 키로 옮긴다(MigrateToKeys). 키가 있으면 쓰지 않는다.
        [HideInInspector] public Vector3 startPosition;
        [HideInInspector] public Quaternion startRotation;
        [HideInInspector] public float startFov;
        [HideInInspector] public bool hasEnd;
        [HideInInspector] public Vector3 endPosition;
        [HideInInspector] public Quaternion endRotation;
        [HideInInspector] public float endFov;
        [HideInInspector] public Ease ease;

        public bool HasKeys => keys != null && keys.Count > 0;

        /// <summary>움직이는 컷인가(키 2개 이상 — 옛 형식이면 끝 화면이 있다).</summary>
        public bool Moves => HasKeys ? keys.Count >= 2 : hasEnd;

        /// <summary>옛 2점 → 키(시작 = 시작 키 · 끝 = 끝 키 · 끝 없으면 키 1개). 이미 키가 있으면 그대로. 바꿨으면 true.</summary>
        public bool MigrateToKeys()
        {
            if (HasKeys) return false;
            keys = LegacyKeys();
            return true;
        }

        /// <summary>옛 2점 값으로 만든 키 목록(이관 · 이관 전 값을 읽는 Pose).</summary>
        public List<CutKey> LegacyKeys()
        {
            var list = new List<CutKey>(2) { CutKey.Make(0f, startPosition, startRotation, startFov, (BossVictoryKeyCurve)(int)ease) };
            if (hasEnd) list.Add(CutKey.Make(1f, endPosition, endRotation, endFov));
            return list;
        }

        /// <summary>키 목록을 새 목록으로 복사한 컷(붙여넣기·복제용).</summary>
        public Cut WithCopiedKeys()
        {
            Cut c = this;
            if (keys != null) c.keys = new List<CutKey>(keys);
            return c;
        }

        /// <summary>새 컷 기본값 — 옛 기본 와이드 구도(가운데 기준·거리 16·내려다보는 각 22·방위 50·초점 높이 1.5·FOV 40) · 고정 · 벽 숨김 켬.</summary>
        public static Cut Default
        {
            get
            {
                Orbit(16f, 22f, 50f, 1.5f, out Vector3 p, out Quaternion r);
                return new Cut
                {
                    anchor = Anchor.Middle, keys = new List<CutKey> { CutKey.Make(0f, p, r, 40f) }, hideWalls = true,
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

    /// <summary>컷 진행 <paramref name="u"/>(0 = 컷 시작, 1 = 다음 컷 시작) 의 월드 카메라 자세·FOV. 키 1개면 고정. 키가 없으면(이관 전) 옛 2점 값.</summary>
    public static void Pose(in Cut cut, in Subjects s, float u, out Vector3 position, out Quaternion rotation, out float fov)
    {
        LocalPose(cut.HasKeys ? cut.keys : cut.LegacyKeys(), cut.smoothPath, u, out Vector3 lp, out Quaternion lr, out fov);
        ToWorld(cut.anchor, s, lp, lr, out position, out rotation);
    }

    /// <summary>화면 키 목록의 비율 <paramref name="u"/> 기준 좌표계 자세 — 구간 곡선 뒤 위치 Lerp(부드러운 곡선이면 키 3개 이상에서 Catmull-Rom)·회전 Slerp·FOV Lerp.</summary>
    public static void LocalPose(IReadOnlyList<CutKey> keys, bool smoothPath, float u, out Vector3 position, out Quaternion rotation, out float fov)
    {
        if (keys == null || keys.Count == 0)
        {
            position = Vector3.zero; rotation = Quaternion.identity; fov = 40f;
            return;
        }
        CutKey a = keys[0];
        if (keys.Count == 1)
        {
            position = a.position; rotation = Normalize(a.rotation); fov = a.fov;
            return;
        }
        float k = BossVictoryKeys.EasedSegment(keys, u, out int i);
        a = keys[i];
        CutKey b = keys[i + 1];
        if (smoothPath && keys.Count >= 3)
        {
            Vector3 p0 = keys[Mathf.Max(0, i - 1)].position, p3 = keys[Mathf.Min(keys.Count - 1, i + 2)].position;
            position = BossVictoryKeys.CatmullRom(p0, a.position, b.position, p3, k);
        }
        else
        {
            position = Vector3.LerpUnclamped(a.position, b.position, k);
        }
        rotation = Quaternion.Slerp(Normalize(a.rotation), Normalize(b.rotation), k);
        fov = Mathf.LerpUnclamped(a.fov, b.fov, k);
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
