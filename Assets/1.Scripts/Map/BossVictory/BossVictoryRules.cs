using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 보스 B 승리 연출(BOSS DOWN — PLAN-boss-victory-cinematic) 순수 규칙. 타임라인 경계·보간 곡선·서버 구도 계산.
/// 상태가 없어 EditMode 로 경계를 고정한다. 시간은 전부 <b>처치 확정 시점 t=0 기준 실제 경과 초</b>(기획 v02 §1).
/// </summary>
public static class BossVictoryRules
{
    // ── 타임라인(기획 v02 §1·§3·§4·§5) ──
    public const float FreezeStart = 0.12f;      // 02 히트스톱 — 인물·보스·핵심 VFX 정지
    public const float DescendStart = 0.22f;     // 03 카메라 하강
    public const float DescendEnd = 0.72f;       // 04 정지 구도 완성
    public const float ColorInEnd = 0.80f;       // 0.72–0.80 색 0 → 1
    public const float TypoStart = 0.92f;        // 05 타이포 진입
    public const float TypoSlideEnd = 1.07f;     // 0.92–1.07 왼쪽으로 안착 · 두 띠 정렬
    public const float TypoFadeInEnd = 1.08f;    // 0.92–1.08 불투명도 0 → 1
    public const float FreezeEnd = 2.00f;        // 06 정지 해제 · 붕괴 재개
    public const float ColorOutEnd = 2.24f;      // 2.00–2.24 색 1 → 0
    public const float End = 3.20f;              // 타이포 소거 끝 · 연출 상태 정리

    /// <summary>정지 구간 길이(1.88초) — 서버 사망 연출(클립·디졸브)이 이만큼 늦게 끝난다.</summary>
    public const float FreezeDuration = FreezeEnd - FreezeStart;

    /// <summary>기획 v02 §2 이동 곡선 <c>u²(3−2u)</c>. 0~1 밖은 잘라 낸다.</summary>
    public static float Smooth(float u)
    {
        u = Mathf.Clamp01(u);
        return u * u * (3f - 2f * u);
    }

    /// <summary><paramref name="t"/> 가 [a, b] 구간 어디인지(0~1, smoothstep). b ≤ a 면 계단.</summary>
    public static float SmoothBetween(float t, float a, float b) =>
        b <= a ? (t >= b ? 1f : 0f) : Smooth((t - a) / (b - a));

    /// <summary>인물·보스·핵심 VFX 를 슬로모션으로 늦추는 구간인가(0.12 ≤ t &lt; 2.00). 완전 정지가 아니다(경석 10-27).</summary>
    public static bool IsWorldFrozen(float t) => t >= FreezeStart && t < FreezeEnd;

    /// <summary>색이 원래로 돌아가는 시간(초) — 보스 디졸브가 시작된 순간부터.</summary>
    public const float ColorOutSeconds = ColorOutEnd - FreezeEnd;

    /// <summary>시작 카메라 → 24° 최종 구도 보간 비율(0.22–0.72).</summary>
    public static float DescendBlend(float t) => SmoothBetween(t, DescendStart, DescendEnd);

    /// <summary>24° → 32° 종료 구도 보간 비율(2.00–3.20, 위로 올라가며 후퇴).</summary>
    public static float RiseBlend(float t) => SmoothBetween(t, FreezeEnd, End);

    /// <summary>
    /// 색 전환 강도(0 = 원래 색, 1 = 주황 배경 틴트·회색 인물). 0.72–0.80 들어오고, <b>보스 디졸브가 시작될 때까지 유지</b>한 뒤
    /// 0.24초에 원래 색으로(경석 10-27 — 기획 v02 의 2.00–2.24 원복 대신). <paramref name="dissolveAt"/> = 디졸브 시작 경과(초), 아직이면 +∞.
    /// </summary>
    public static float ColorWeight(float t, float dissolveAt = float.PositiveInfinity)
    {
        if (t < DescendEnd) return 0f;
        float w = SmoothBetween(t, DescendEnd, ColorInEnd);
        if (t >= dissolveAt) w *= 1f - SmoothBetween(t, dissolveAt, dissolveAt + ColorOutSeconds);
        return w;
    }

    /// <summary>
    /// 글리치 구간 [start, end] 의 세기(0~1). 시작에 빠르게(0.03초) 올라 끝에서 0.06초에 빠진다 — 지직 하고 터졌다 사라지는 모양.
    /// </summary>
    public static float GlitchBurst(float t, float start, float end)
    {
        if (end <= start || t < start || t > end) return 0f;
        float attack = Mathf.Clamp01((t - start) / 0.03f);
        float release = Mathf.Clamp01((end - t) / 0.06f);
        return Mathf.Min(attack, release);
    }

    /// <summary>타이포 불투명도(0.92 부터 <paramref name="fadeInSeconds"/> 동안 진입 · 2.00–3.20 소거).</summary>
    public static float TypoAlpha(float t, float fadeInSeconds = TypoFadeInEnd - TypoStart)
    {
        if (t < TypoStart) return 0f;
        if (t < FreezeEnd) return SmoothBetween(t, TypoStart, TypoStart + Mathf.Max(0.01f, fadeInSeconds));
        return 1f - SmoothBetween(t, FreezeEnd, End);
    }

    /// <summary>타이포 진입 진행(0 = 화면 우측으로 밀린 자리, 1 = 안착). 0.92 부터 <paramref name="settleSeconds"/> 동안.</summary>
    public static float TypoSettle(float t, float settleSeconds = TypoSlideEnd - TypoStart) =>
        SmoothBetween(t, TypoStart, TypoStart + Mathf.Max(0.01f, settleSeconds));

    /// <summary>
    /// 사망 표현 진행 시간: 슬로모션 구간(0.12–2.00)은 <paramref name="slowScale"/> 배로만 흐른다. 서버 사망 연출 대기와 클라 애니가 같은 시계를 따른다.
    /// a(t) = t (t &lt; 0.12) · 0.12 + (t − 0.12)·s (0.12 ≤ t &lt; 2.00) · 0.12 + 1.88·s + (t − 2.00) (t ≥ 2.00). s = 0 이면 완전 정지.
    /// </summary>
    public static float DeathPresentationTime(float t, float slowScale = 0f)
    {
        float s = Mathf.Max(0f, slowScale);
        if (t < FreezeStart) return Mathf.Max(0f, t);
        if (t < FreezeEnd) return FreezeStart + (t - FreezeStart) * s;
        return FreezeStart + FreezeDuration * s + (t - FreezeEnd);
    }

    // ── 구도(서버가 한 번 계산 — 모든 피어 같은 결과) ────────────────────

    /// <summary>구도 계산 입력. 위치는 서버가 처치 순간 본 값.</summary>
    public struct Subjects
    {
        public Vector3 Killer;
        /// <summary>막타 캐릭터가 보던 방향(수평). 막타 → 타격점 방향이 퇴화할 때 쓴다.</summary>
        public Vector3 KillerForward;
        public Vector3 HitPoint;
        public Vector3 Boss;
        /// <summary>보스 몸 반지름(m) — 화면 안에 들어올 범위.</summary>
        public float BossRadius;
        /// <summary>보스 키(m) — 피격점이 아닌 몸통 중앙을 화면 안에 두는 데 쓴다.</summary>
        public float BossHeight;
        /// <summary>살아 있는 팀원(막타 제외). clientId 순으로 넣는다(같은 입력 = 같은 결과).</summary>
        public IReadOnlyList<Vector3> Teammates;
    }

    /// <summary>구도 파라미터(SO 에서 온다).</summary>
    public struct Framing
    {
        public float FinalPitch;     // 24°
        public float EndPitch;       // 32°
        public float Fov;            // 34°
        public float Aspect;         // 기준 화면비 16:9 — 피어마다 해상도가 달라도 같은 후보를 고른다
        public float MinDistance;
        public float MaxDistance;
        public float DistanceStep;
        public float EndDistanceScale; // 32° 종료 구도 거리 배수(후퇴)
        public float CharacterHeight;  // 플레이어 몸 높이(m) — 화면 안에 들어올 범위
        /// <summary>
        /// 방위각 후보(도). 막타 → 타격점 반대 방향(막타 뒤)에서 위에서 봐 시계 방향으로 몇 도 돌린 자리.
        /// <b>음수 = 카메라가 막타 오른쪽 뒤 → 막타가 화면 왼쪽</b>(원하는 구도). 양수도 넣어 두면 점수가 고른다.
        /// </summary>
        public float[] YawCandidates;
    }

    public struct Pose
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Pose(Vector3 p, Quaternion r) { Position = p; Rotation = r; }
    }

    /// <summary>계산 결과 — 24° 최종 구도, 32° 종료 구도, 고른 후보(진단용).</summary>
    public struct Composition
    {
        public Pose Final;
        public Pose EndPose;
        public float Yaw;
        public float Distance;
        public float Score;
    }

    /// <summary>
    /// 막타 = 좌측 전경, 타격점·보스 = 중앙~우측 중경, 팀원 = 좌우 후경(기획 v02 §2 표)을 점수로 고른다.
    /// 후보 = (방위각 × 거리)를 고정 순서로 돌고, 같은 점수면 먼저 나온 후보. 물리(가림) 판정은 하지 않는다 — 실제 전투 위치를 유지한다.
    /// </summary>
    public static Composition Compose(Subjects s, Framing f)
    {
        Vector3 fwd = Flat(s.HitPoint - s.Killer);
        if (fwd.sqrMagnitude < 0.0001f) fwd = Flat(s.KillerForward);
        if (fwd.sqrMagnitude < 0.0001f) fwd = Flat(s.Boss - s.Killer);
        if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
        fwd.Normalize();

        // 주시점 = 타격점. 정지 자세가 정해진 뒤 고정한다(기획 §2).
        Vector3 look = s.HitPoint;
        float[] yaws = f.YawCandidates != null && f.YawCandidates.Length > 0 ? f.YawCandidates : new[] { 25f };
        float step = Mathf.Max(0.1f, f.DistanceStep);

        Composition best = default;
        best.Score = float.NegativeInfinity;
        bool found = false;

        for (int y = 0; y < yaws.Length; y++)
        {
            for (float d = Mathf.Max(0.5f, f.MinDistance); d <= f.MaxDistance + 0.0001f; d += step)
            {
                Pose pose = PoseAt(look, fwd, yaws[y], f.FinalPitch, d);
                float score = Score(pose, s, f, d);
                if (score > best.Score)
                {
                    best = new Composition { Final = pose, Yaw = yaws[y], Distance = d, Score = score };
                    found = true;
                }
            }
        }

        if (!found)
            best = new Composition { Final = PoseAt(look, fwd, yaws[0], f.FinalPitch, f.MaxDistance), Yaw = yaws[0], Distance = f.MaxDistance };

        best.EndPose = PoseAt(look, fwd, best.Yaw, f.EndPitch, best.Distance * Mathf.Max(1f, f.EndDistanceScale));
        return best;
    }

    /// <summary>주시점 <paramref name="look"/> 을 보는 카메라 자세. 막타 뒤(−fwd)에서 <paramref name="yaw"/> 도 돌리고 <paramref name="pitch"/> 도 내려다본다.</summary>
    public static Pose PoseAt(Vector3 look, Vector3 fwd, float yaw, float pitch, float distance)
    {
        Vector3 back = Quaternion.AngleAxis(yaw, Vector3.up) * (-fwd);
        float p = pitch * Mathf.Deg2Rad;
        Vector3 offset = back.normalized * (Mathf.Cos(p) * distance) + Vector3.up * (Mathf.Sin(p) * distance);
        Vector3 pos = look + offset;
        Quaternion rot = Quaternion.LookRotation(look - pos, Vector3.up);
        return new Pose(pos, rot);
    }

    /// <summary>카메라 기준 화면 좌표(0~1, y 위가 1)와 깊이(m). 뒤에 있으면 깊이 ≤ 0.</summary>
    public static Vector3 ToViewport(Pose pose, Vector3 world, float fovDeg, float aspect)
    {
        Vector3 local = Quaternion.Inverse(pose.Rotation) * (world - pose.Position);
        if (local.z <= 0.01f) return new Vector3(0.5f, 0.5f, local.z);
        float tanHalf = Mathf.Tan(fovDeg * 0.5f * Mathf.Deg2Rad);
        float x = local.x / (local.z * tanHalf * aspect);
        float y = local.y / (local.z * tanHalf);
        return new Vector3(0.5f + x * 0.5f, 0.5f + y * 0.5f, local.z);
    }

    static float Score(Pose pose, Subjects s, Framing f, float distance)
    {
        float h = Mathf.Max(0.5f, f.CharacterHeight);
        Vector3 kFoot = ToViewport(pose, s.Killer, f.Fov, f.Aspect);
        Vector3 kHead = ToViewport(pose, s.Killer + Vector3.up * h, f.Fov, f.Aspect);
        Vector3 hit = ToViewport(pose, s.HitPoint, f.Fov, f.Aspect);
        Vector3 boss = ToViewport(pose, s.Boss + Vector3.up * (Mathf.Max(0.5f, s.BossHeight) * 0.5f), f.Fov, f.Aspect);

        // 필수: 막타(발·머리)·타격점·보스 몸통이 화면 안(안전 영역).
        if (!InSafe(kFoot, 0.04f) || !InSafe(kHead, 0.04f) || !InSafe(hit, 0.06f) || !InSafe(boss, 0.04f))
            return float.NegativeInfinity;

        float score = 0f;

        // 높음: 막타가 왼쪽(0.15~0.42)이고 타격점이 중앙~우측(0.45~0.8).
        score += 3f * Band(kFoot.x, 0.15f, 0.42f);
        score += 2f * Band(hit.x, 0.45f, 0.80f);
        // 높음: 막타가 보스보다 카메라에 가깝다(전경).
        score += kFoot.z < boss.z ? 2f : -2f;
        // 높음: 보스가 막타를 가리지 않는다 — 화면에서 둘 사이가 벌어져 있다.
        score += 2f * Mathf.Clamp01((Mathf.Abs(boss.x - kFoot.x) - 0.12f) / 0.2f);
        // 높음: 막타가 크게 읽힌다 — 화면 키 0.35~0.7 이 좋다.
        float killerSize = kHead.y - kFoot.y;
        score += 2f * Band(killerSize, 0.35f, 0.70f);

        // 중간: 팀원이 화면에 들어오고 좌우로 갈린다(후경).
        if (s.Teammates != null && s.Teammates.Count > 0)
        {
            float minX = 1f, maxX = 0f;
            for (int i = 0; i < s.Teammates.Count; i++)
            {
                Vector3 t = ToViewport(pose, s.Teammates[i] + Vector3.up * (h * 0.5f), f.Fov, f.Aspect);
                if (InSafe(t, 0.02f))
                {
                    score += 1f;
                    score += t.z > kFoot.z ? 0.5f : 0f;   // 막타보다 뒤(후경)
                    minX = Mathf.Min(minX, t.x);
                    maxX = Mathf.Max(maxX, t.x);
                }
            }
            if (s.Teammates.Count >= 2 && maxX > minX) score += Mathf.Clamp01((maxX - minX) / 0.4f);
        }

        // 낮음: 가까울수록 조금 낫다(같은 조건이면 크게).
        score -= 0.02f * distance;
        return score;
    }

    static bool InSafe(Vector3 v, float margin) =>
        v.z > 0.05f && v.x >= margin && v.x <= 1f - margin && v.y >= margin && v.y <= 1f - margin;

    static float Band(float v, float lo, float hi)
    {
        if (v >= lo && v <= hi) return 1f;
        float d = v < lo ? lo - v : v - hi;
        return Mathf.Clamp01(1f - d / 0.15f);
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
}
