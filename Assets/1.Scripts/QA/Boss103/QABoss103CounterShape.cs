using System.Globalization;
using UnityEngine;

/// <summary>
/// 할 일 164 — 봇 간파 "지금 누르면 닿는가" 를 서버 판정과 같은 형상 겹침으로 본다(할 일 159 조사 1).
/// 형상 = <see cref="PlayerInterruptSkillBase.TryGetPlacedJudgeShape"/>(앵커를 보스 방향으로 돌려 지금 위치에 놓은 것 — 시도 예측·랩 기즈모와 같은 값),
/// 질의 = <see cref="HitboxAnchorShape.Overlap"/>(서버 <c>OverlapHitboxAnchor</c> 와 같은 오버랩)을 보스 HurtBox 층으로.
/// 상자 크기·높이는 읽지 않고 프리팹 앵커 그대로 따라간다(웰즈·23호 상자 높이 조정 163 등).
/// <list type="bullet">
/// <item>여유 = 거리 빼기가 아니라 보스 쪽에서 <see cref="BackoffM"/> 뒤로 물린 형상도 겹치나(<see cref="Probe.Backoff"/>).</item>
/// <item>틈(<see cref="GapM"/>) = 형상을 보스 쪽으로 몇 m 밀어야 처음 겹치나 — 창 열림 포기 거리·대쉬 간파 판단용(옛 2D 꼭짓점 사거리 대신).</item>
/// </list>
/// 서버 계측기(<c>pn_server_cast</c>)는 판정 순간 앵커의 지금 형상(돌리지 않음)에 같은 검사를 한다(<see cref="ProbeShape"/>).
/// </summary>
public static class QABoss103CounterShape
{
    /// <summary>누름 여유 — 보스 쪽에서 이만큼(m) 뒤로 물린 형상도 겹쳐야 누름 자리.</summary>
    public const float BackoffM = 0.2f;
    /// <summary>틈 탐색 보폭(m)과 이분 탐색 횟수.</summary>
    public const float GapStepM = 0.25f;
    public const int GapBisect = 4;

    /// <summary>검사 1회 결과. <see cref="Valid"/> = 형상·대상을 읽었다(아니면 나머지는 의미 없음).</summary>
    public struct Probe
    {
        public bool Valid;
        public bool Overlap;
        public bool Backoff;
        public float BoxMinY, BoxMaxY;
        /// <summary>앵커 정면 ↔ 보스 방향 수평 각(도). 못 읽으면 NaN.</summary>
        public float YawErrDeg;

        /// <summary>누름 자리(여유 포함) = 물린 형상도 겹친다.</summary>
        public bool InSpot => Valid && Backoff;
        /// <summary>지금 누르면 닿는다(여유 없음).</summary>
        public bool Touches => Valid && Overlap;

        public static Probe Invalid => new Probe { YawErrDeg = float.NaN, BoxMinY = float.NaN, BoxMaxY = float.NaN };
    }

    /// <summary>
    /// 이 플레이어의 간파 판정 형상을 <paramref name="target"/> 쪽으로 돌려 <paramref name="resolvePosition"/> 에 놓는다(<see cref="PlayerInterruptSkillBase.TryGetPlacedJudgeShape"/>).
    /// <paramref name="anchorForward"/> = 지금 앵커 정면(몸 방향 오차 계측용).
    /// </summary>
    public static bool TryPlace(Player me, Vector3 resolvePosition, Vector3 toTarget, out HitboxAnchorShape shape, out Vector3 anchorForward)
    {
        shape = default;
        anchorForward = Vector3.zero;
        PlayerSkillController skills = me != null ? me.GetComponent<PlayerSkillController>() : null;
        PlayerInterruptSkillBase skill = skills != null ? skills.GetSkill(PlayerSkillSlot.Interrupt) as PlayerInterruptSkillBase : null;
        if (skill == null || skill.HitboxAnchor == null || !skill.TryGetPlacedJudgeShape(resolvePosition, toTarget, out shape))
            return false;
        anchorForward = skill.HitboxAnchor.transform.forward;
        return true;
    }

    /// <summary>
    /// 봇 검사 — 지금 위치(<paramref name="myPos"/>)에서 <paramref name="aimAt"/>(봇이 누를 때 겨누는 보스 위치) 쪽으로 돌린 판정 형상이
    /// <paramref name="hurt"/> 에 닿나 + 0.2m 물려도 닿나.
    /// </summary>
    public static Probe ProbeAt(Player me, Vector3 myPos, Vector3 aimAt, Collider hurt, Collider[] buf)
    {
        if (me == null || hurt == null || !hurt.enabled)
            return Probe.Invalid;
        Vector3 to = FlatTo(myPos, aimAt);
        if (to == Vector3.zero || !TryPlace(me, myPos, to, out HitboxAnchorShape shape, out Vector3 fwd))
            return Probe.Invalid;
        return ProbeShape(shape, to, fwd, hurt, buf);
    }

    /// <summary>
    /// 놓인 형상 하나 검사 — 겹침·<see cref="BackoffM"/> 물림 겹침·월드 높이 범위·앵커 정면 ↔ <paramref name="toTarget"/> 수평 각.
    /// <paramref name="anchorForward"/> 가 0 이면 각은 NaN.
    /// </summary>
    public static Probe ProbeShape(HitboxAnchorShape shape, Vector3 toTarget, Vector3 anchorForward, Collider hurt, Collider[] buf)
    {
        if (hurt == null || !hurt.enabled)
            return Probe.Invalid;
        toTarget.y = 0f;
        Vector3 dir = toTarget.sqrMagnitude > 1e-8f ? toTarget.normalized : Vector3.zero;
        VerticalRange(shape, out float minY, out float maxY);
        return new Probe
        {
            Valid = true,
            Overlap = Touches(shape, hurt, buf),
            Backoff = Touches(shape.Offset(-dir * BackoffM), hurt, buf),
            BoxMinY = minY,
            BoxMaxY = maxY,
            YawErrDeg = YawDeg(anchorForward, toTarget),
        };
    }

    /// <summary>형상이 <paramref name="hurt"/> 에 겹치나 — 그 콜라이더 층만 질의(트리거 포함, 서버 판정과 같은 오버랩).</summary>
    public static bool Touches(HitboxAnchorShape shape, Collider hurt, Collider[] buf)
    {
        if (hurt == null || buf == null)
            return false;
        int n = shape.Overlap(buf, 1 << hurt.gameObject.layer);
        for (int i = 0; i < n; i++)
            if (buf[i] == hurt)
                return true;
        return false;
    }

    /// <summary>
    /// 틈(m) — 형상을 수평 <paramref name="toTarget"/> 쪽으로 이만큼 밀면 처음 겹친다(지금 겹치면 0). <paramref name="maxM"/> 안에 없으면 NaN
    /// (높이가 안 맞아 밀어도 안 닿는 경우 포함). 보폭 <see cref="GapStepM"/> 뒤 이분 탐색 <see cref="GapBisect"/> 회.
    /// </summary>
    public static float GapM(HitboxAnchorShape shape, Vector3 toTarget, Collider hurt, Collider[] buf, float maxM)
    {
        if (hurt == null || !hurt.enabled)
            return float.NaN;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude < 1e-8f)
            return Touches(shape, hurt, buf) ? 0f : float.NaN;
        Vector3 dir = toTarget.normalized;
        if (Touches(shape, hurt, buf))
            return 0f;
        float lo = 0f;
        for (float d = GapStepM; d <= maxM + 1e-4f; d += GapStepM)
        {
            if (!Touches(shape.Offset(dir * d), hurt, buf))
            {
                lo = d;
                continue;
            }
            float hi = d;
            for (int i = 0; i < GapBisect; i++)
            {
                float mid = (lo + hi) * 0.5f;
                if (Touches(shape.Offset(dir * mid), hurt, buf))
                    hi = mid;
                else
                    lo = mid;
            }
            return hi;
        }
        return float.NaN;
    }

    /// <summary>봇 틈 — 지금 위치에서 보스 쪽으로 돌린 판정 형상의 <see cref="GapM"/>. 형상을 못 읽으면 NaN.</summary>
    public static float GapAt(Player me, Vector3 myPos, Vector3 aimAt, Collider hurt, Collider[] buf, float maxM)
    {
        if (me == null || hurt == null || !hurt.enabled)
            return float.NaN;
        Vector3 to = FlatTo(myPos, aimAt);
        if (to == Vector3.zero || !TryPlace(me, myPos, to, out HitboxAnchorShape shape, out _))
            return float.NaN;
        return GapM(shape, to, hurt, buf, maxM);
    }

    /// <summary>형상의 월드 높이 범위(최저·최고 y).</summary>
    public static void VerticalRange(HitboxAnchorShape s, out float minY, out float maxY)
    {
        switch (s.Kind)
        {
            case OverlapCollider.Box:
                Vector3 h = s.Box.halfExtents;
                Quaternion q = s.Box.orientation;
                float ey = Mathf.Abs((q * Vector3.right).y) * h.x + Mathf.Abs((q * Vector3.up).y) * h.y + Mathf.Abs((q * Vector3.forward).y) * h.z;
                minY = s.Box.center.y - ey;
                maxY = s.Box.center.y + ey;
                return;
            case OverlapCollider.Sphere:
                minY = s.Sphere.center.y - s.Sphere.radius;
                maxY = s.Sphere.center.y + s.Sphere.radius;
                return;
            case OverlapCollider.Capsule:
                minY = Mathf.Min(s.Capsule.point0.y, s.Capsule.point1.y) - s.Capsule.radius;
                maxY = Mathf.Max(s.Capsule.point0.y, s.Capsule.point1.y) + s.Capsule.radius;
                return;
            default:
                minY = maxY = float.NaN;
                return;
        }
    }

    /// <summary>두 방향의 수평 사이각(도, 0~180). 어느 쪽이든 수평 성분이 없으면 NaN.</summary>
    public static float YawDeg(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        if (a.sqrMagnitude < 1e-8f || b.sqrMagnitude < 1e-8f)
            return float.NaN;
        return Vector3.Angle(a, b);
    }

    /// <summary>로그 꼬리 — <c>overlap=… backoff=… boxMinY=… boxMaxY=… yawErrDeg=…</c>(값 토큰에 공백 없음, 못 읽으면 na).</summary>
    public static string Format(Probe p)
    {
        if (!p.Valid)
            return "overlap=na backoff=na boxMinY=na boxMaxY=na yawErrDeg=na";
        return $"overlap={(p.Overlap ? "true" : "false")} backoff={(p.Backoff ? "true" : "false")} " +
               $"boxMinY={F(p.BoxMinY, "F2")} boxMaxY={F(p.BoxMaxY, "F2")} yawErrDeg={F(p.YawErrDeg, "F0")}";
    }

    private static Vector3 FlatTo(Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        d.y = 0f;
        return d.sqrMagnitude > 1e-8f ? d : Vector3.zero;
    }

    private static string F(float v, string fmt) =>
        float.IsNaN(v) || float.IsInfinity(v) ? "na" : v.ToString(fmt, CultureInfo.InvariantCulture);
}
