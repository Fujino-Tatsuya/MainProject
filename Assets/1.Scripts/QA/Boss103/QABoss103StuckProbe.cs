#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// 할 일 140 — 구속(23호 잡기·돌진 밀기) 상태 전환 기록(순수, EditMode <c>QABoss103StuckProbeTests</c>).
/// 같은 값이 이어지면 기록하지 않고, 바뀐 순간만 (시각, 표지)로 남긴다. 표지 = <c>push</c>·<c>carry</c>(23호가 시전자)·<c>push_other</c>·<c>carry_other</c>·<c>free</c>(놓임).
/// </summary>
public sealed class QABoss103RestraintTrace
{
    /// <summary>끼임 줄에 붙이는 직전 구간(초).</summary>
    public const double WindowSeconds = 2.0;
    public const string Free = "free";
    private const int Capacity = 16;

    private readonly List<double> _at = new List<double>(Capacity);
    private readonly List<string> _label = new List<string>(Capacity);

    /// <summary>마지막으로 본 표지(처음 = <see cref="Free"/>).</summary>
    public string Last { get; private set; } = Free;

    /// <summary>지금 표지를 본다 — 직전과 다르면 전환 1건.</summary>
    public void Observe(double now, string label)
    {
        if (string.IsNullOrEmpty(label) || label == Last)
            return;
        Last = label;
        if (_at.Count >= Capacity)
        {
            _at.RemoveAt(0);
            _label.RemoveAt(0);
        }
        _at.Add(now);
        _label.Add(label);
    }

    /// <summary>
    /// 지금부터 <paramref name="window"/> 초 안의 전환 — <c>carry@-1.32+free@-0.20</c>(시각 = 지금 기준 초, 오래된 것부터). 없으면 <c>none</c>.
    /// </summary>
    public string Recent(double now, double window = WindowSeconds)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < _at.Count; i++)
        {
            double ago = _at[i] - now;
            if (ago < -window || ago > 0.0)
                continue;
            if (sb.Length > 0)
                sb.Append('+');
            sb.Append(_label[i]).Append('@').Append(ago.ToString("F2", CultureInfo.InvariantCulture));
        }
        return sb.Length > 0 ? sb.ToString() : "none";
    }

    public void Clear()
    {
        _at.Clear();
        _label.Clear();
        Last = Free;
    }
}

/// <summary>
/// 할 일 140 — 끼임 원인 계측(동작 변경 없음 — 읽기·물리 질의만). <c>qa_relocate</c>(호스트)·<c>qa_wall_escape_request</c>(봇) 줄에 붙는 필드:
/// <list type="bullet">
/// <item><c>state</c> = <see cref="PlayerActionState"/>, 구속이면 <c>Restrained_&lt;표지&gt;</c>(모드는 <c>PlayerRestrainedState.mode</c> 리플렉션).</item>
/// <item><c>what</c> = 캡슐과 겹친 콜라이더 <c>이름(레이어)</c>들(모든 레이어·트리거 제외·자기 몸 제외, 스킨 0.03m — 벽 판정 <c>InWall</c> 과 같은 캡슐).</item>
/// <item><c>ground</c> = 캡슐 중심에서 아래로 쏜 첫 콜라이더 <c>이름(레이어)@발밑부터 거리</c>.</item>
/// <item><c>grab</c> = 직전 2초 안 구속 전환(<see cref="QABoss103RestraintTrace.Recent"/>). 호스트는 계측기(<see cref="QABoss103Recorder"/>)가 매 프레임 모든 플레이어를 본다(<see cref="HostObserve"/>), 봇은 자기 플레이어만.</item>
/// </list>
/// </summary>
public static class QABoss103StuckProbe
{
    private const BindingFlags AnyInst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private const float Skin = 0.03f;
    private const float GroundRay = 20f;
    private const int MaxNames = 6;

    private static readonly FieldInfo CurrentStateField = typeof(PlayerStateController).GetField("currentState", AnyInst);
    private static readonly FieldInfo ModeField = typeof(PlayerRestrainedState).GetField("mode", AnyInst);
    private static readonly FieldInfo InstigatorField = typeof(PlayerRestrainedState).GetField("instigator", AnyInst);

    private static readonly Collider[] Hits = new Collider[16];
    private static readonly RaycastHit[] Rays = new RaycastHit[16];
    private static readonly Dictionary<ulong, QABoss103RestraintTrace> Host = new Dictionary<ulong, QABoss103RestraintTrace>();

    /// <summary>리플렉션 필드를 다 찾았나(못 찾으면 state 는 <c>Restrained_na</c>).</summary>
    public static bool Readable => CurrentStateField != null && ModeField != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Host.Clear();

    /// <summary>[호스트] 판 시작 — 지난 판 기록을 버린다.</summary>
    public static void HostReset() => Host.Clear();

    /// <summary>[호스트] 매 프레임 — 그 플레이어의 구속 표지를 본다.</summary>
    public static void HostObserve(ulong clientId, Player p, double now)
    {
        if (!Host.TryGetValue(clientId, out QABoss103RestraintTrace t))
            Host[clientId] = t = new QABoss103RestraintTrace();
        t.Observe(now, RestraintLabel(p));
    }

    /// <summary>[호스트] 그 플레이어의 직전 2초 구속 전환. 계측기가 없던 판이면 <c>na</c>.</summary>
    public static string HostGrab(ulong clientId, double now) =>
        Host.TryGetValue(clientId, out QABoss103RestraintTrace t) ? t.Recent(now) : "na";

    /// <summary>구속 표지 — 구속이 아니면 <see cref="QABoss103RestraintTrace.Free"/>.</summary>
    public static string RestraintLabel(Player p)
    {
        PlayerRestrainedState s = RestrainedState(p);
        if (s == null)
            return QABoss103RestraintTrace.Free;
        string mode = ModeField != null && ModeField.GetValue(s) is RestraintMode m ? (m == RestraintMode.Push ? "push" : "carry") : "na";
        GameObject by = InstigatorField != null ? InstigatorField.GetValue(s) as GameObject : null;
        bool boss23 = by != null && by.GetComponentInParent<TwentyThreeBoss>() != null;
        return boss23 ? mode : mode + "_other";
    }

    /// <summary><see cref="PlayerActionState"/> 이름, 구속이면 <c>Restrained_&lt;표지&gt;</c>.</summary>
    public static string StateToken(Player p)
    {
        if (p == null)
            return "na";
        PlayerActionState st = p.CurrentState;
        return st == PlayerActionState.Restrained ? "Restrained_" + RestraintLabel(p) : st.ToString();
    }

    private static PlayerRestrainedState RestrainedState(Player p)
    {
        if (p == null || CurrentStateField == null || p.CurrentState != PlayerActionState.Restrained)
            return null;
        PlayerStateController sc = p.GetComponent<PlayerStateController>();
        return sc != null ? CurrentStateField.GetValue(sc) as PlayerRestrainedState : null;
    }

    /// <summary>캡슐과 겹친 콜라이더 <c>이름(레이어)</c>들(<c>+</c> 로 잇고 최대 6개). 없으면 <c>none</c>, 캡슐이 없으면 <c>na</c>.</summary>
    public static string Overlaps(Player p)
    {
        CapsuleCollider capsule = p != null ? p.GetComponent<CapsuleCollider>() : null;
        if (capsule == null)
            return "na";
        Capsule(capsule, out Vector3 top, out Vector3 bottom, out float radius, out _);
        int n = Physics.OverlapCapsuleNonAlloc(top, bottom, radius, Hits, ~0, QueryTriggerInteraction.Ignore);
        var names = new List<string>();
        for (int i = 0; i < n && names.Count < MaxNames; i++)
        {
            Collider c = Hits[i];
            if (c == null || c.transform.IsChildOf(p.transform))
                continue;
            string s = Name(c);
            if (!names.Contains(s))
                names.Add(s);
        }
        return names.Count > 0 ? string.Join("+", names) : "none";
    }

    /// <summary>발밑 콜라이더 — 캡슐 중심에서 아래로 첫 맞은 것 <c>이름(레이어)@발밑부터 m</c>(음수 = 발이 그 면보다 아래). 없으면 <c>none</c>.</summary>
    public static string Ground(Player p)
    {
        CapsuleCollider capsule = p != null ? p.GetComponent<CapsuleCollider>() : null;
        Vector3 origin;
        float toFeet;
        if (capsule != null)
        {
            Capsule(capsule, out _, out _, out _, out float halfHeight);
            origin = capsule.transform.TransformPoint(capsule.center);
            toFeet = halfHeight;
        }
        else if (p != null)
        {
            origin = p.transform.position + Vector3.up;
            toFeet = 1f;
        }
        else
            return "na";
        int n = Physics.RaycastNonAlloc(origin, Vector3.down, Rays, GroundRay, ~0, QueryTriggerInteraction.Ignore);
        int best = -1;
        for (int i = 0; i < n; i++)
        {
            Collider c = Rays[i].collider;
            if (c == null || c.transform.IsChildOf(p.transform))
                continue;
            if (best < 0 || Rays[i].distance < Rays[best].distance)
                best = i;
        }
        if (best < 0)
            return "none";
        float gap = Rays[best].distance - toFeet;
        return Name(Rays[best].collider) + "@" + gap.ToString("F2", CultureInfo.InvariantCulture);
    }

    /// <summary>한 줄에 붙일 필드 묶음(<c> state=… what=… ground=… grab=…</c>, 앞 공백 포함). 값은 토큰화.</summary>
    public static string Fields(Player p, string grab) => Fields(StateToken(p), Overlaps(p), Ground(p), grab);

    /// <summary>이미 읽은 값으로 같은 필드 묶음.</summary>
    public static string Fields(string state, string what, string ground, string grab) =>
        $" state={QABoss103Plan.Token(state)} what={QABoss103Plan.Token(what)} ground={QABoss103Plan.Token(ground)} grab={QABoss103Plan.Token(grab)}";

    private static string Name(Collider c) => $"{c.name}({LayerMask.LayerToName(c.gameObject.layer)})";

    /// <summary><c>InWall</c> 과 같은 캡슐(반지름 − 스킨, 세로 축).</summary>
    private static void Capsule(CapsuleCollider capsule, out Vector3 top, out Vector3 bottom, out float radius, out float halfHeight)
    {
        Transform t = capsule.transform;
        Vector3 lossy = t.lossyScale;
        float fullRadius = capsule.radius * Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.z));
        radius = Mathf.Max(0.01f, fullRadius - Skin);
        float height = Mathf.Max(capsule.height * Mathf.Abs(lossy.y), fullRadius * 2f);
        halfHeight = height * 0.5f;
        float half = Mathf.Max(0f, halfHeight - fullRadius);
        Vector3 center = t.TransformPoint(capsule.center);
        top = center + t.up * half;
        bottom = center - t.up * half;
    }
}
#endif
