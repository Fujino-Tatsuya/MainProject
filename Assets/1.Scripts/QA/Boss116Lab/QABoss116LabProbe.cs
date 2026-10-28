#if UNITY_EDITOR
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 할 일 116-C4 — 대쉬 간파 예측 랩 측정 도구(읽기만, 게임 동작 불변). 랩 디렉터(<see cref="QABoss116LabDirector"/>)가 켜는 동안만
/// 전투 봇이 시도 줄(<c>dashint</c>)에 판단 근거 값을 붙인다(<see cref="Snapshot"/>) — 103·116 배치(랩 아님)는 꺼져 있어 줄이 그대로다.
/// <list type="bullet">
/// <item>대시 시작 게이트 값(<c>PlayerDashController.TryBeginPredictedDash</c> 검사 순서와 같은 값) — 충전·다음 충전·상태·CanMove·접지·입력 주체.</item>
/// <item>게임 대시 거부 로그(<c>[Dash] 시작 불가 …</c>) 캡처(<see cref="BeginCapture"/>/<see cref="EndCapture"/>) — 같은 호출 안에서 찍힌 줄만.</item>
/// <item>벽 거리(<see cref="WallDistance"/>) — 내 위치에서 가장 가까운 Wall/Env 표면(수평 m) — 구간(<see cref="QABoss116Lab.Zone"/>) 근거.</item>
/// </list>
/// </summary>
public static class QABoss116LabProbe
{
    /// <summary>랩 판 진행 중 — 봇이 상세 값을 붙인다.</summary>
    public static bool Active { get; set; }

    /// <summary>디렉터가 정한 지금 구간(wall·open) — 시도 줄 <c>zonePlanned</c>.</summary>
    public static string PlannedZone { get; set; } = "";

    private static int _wallMask = -1;
    private static int WallMask => _wallMask >= 0 ? _wallMask : _wallMask = LayerMask.GetMask("Wall", "Env");
    private const float WallScanRadius = 8f;
    private static readonly Collider[] Hits = new Collider[32];
    private static readonly FieldInfo CurrentEntryField =
        typeof(TwentyThreeBoss).GetField("_currentEntry", BindingFlags.Instance | BindingFlags.NonPublic);

    private static bool _capturing;
    private static string _captured;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Active = false;
        PlannedZone = "";
        _capturing = false;
        _captured = null;
        Application.logMessageReceived -= OnLog;
    }

    // ── 게임 로그 캡처 ────────────────────────────────────────

    /// <summary>이 호출부터 <see cref="EndCapture"/> 까지 찍힌 <c>[Dash] 시작 불가</c> 줄 하나를 잡는다(같은 프레임·동기 호출 전제).</summary>
    public static void BeginCapture()
    {
        _captured = null;
        if (_capturing)
            return;
        _capturing = true;
        Application.logMessageReceived += OnLog;
    }

    /// <summary>잡은 줄(토큰화, 없으면 빈칸).</summary>
    public static string EndCapture()
    {
        if (_capturing)
        {
            Application.logMessageReceived -= OnLog;
            _capturing = false;
        }
        string s = _captured;
        _captured = null;
        return s == null ? "" : QABoss103Plan.Token(s.Length > 140 ? s.Substring(0, 140) : s);
    }

    private static void OnLog(string condition, string stackTrace, LogType type)
    {
        if (_captured == null && condition != null && condition.StartsWith("[Dash] 시작 불가", System.StringComparison.Ordinal))
            _captured = condition;
    }

    // ── 값 ────────────────────────────────────────────────────

    /// <summary>내 위치에서 가장 가까운 Wall/Env 표면까지 수평 거리(m). <see cref="WallScanRadius"/> 안에 없으면 -1.</summary>
    public static float WallDistance(Vector3 pos, out string name)
    {
        name = "";
        int n = Physics.OverlapSphereNonAlloc(pos + Vector3.up, WallScanRadius, Hits, WallMask, QueryTriggerInteraction.Ignore);
        float best = -1f;
        for (int i = 0; i < n; i++)
        {
            Collider c = Hits[i];
            if (c == null)
                continue;
            Vector3 probe = new Vector3(pos.x, c.bounds.center.y, pos.z);
            Vector3 p = c is MeshCollider mc && !mc.convex ? c.bounds.ClosestPoint(probe) : c.ClosestPoint(probe);
            float d = new Vector2(p.x - pos.x, p.z - pos.z).magnitude;
            if (best < 0f || d < best)
            {
                best = d;
                name = c.name;
            }
        }
        return best;
    }

    /// <summary>보스가 지금 하는 공격 행 이름(<c>_currentEntry.attackId</c>, 리플렉션). 모르면 빈칸.</summary>
    public static string AttackOf(TwentyThreeBoss boss)
    {
        if (boss == null || CurrentEntryField == null)
            return "";
        object entry = CurrentEntryField.GetValue(boss);
        if (entry == null)
            return "";
        FieldInfo id = entry.GetType().GetField("attackId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object v = id != null ? id.GetValue(entry) : null;
        return v != null ? v.ToString() : "";
    }

    /// <summary>
    /// 시도 판단 근거 값(key, value 순서로 <paramref name="kv"/> 에 붙인다). <paramref name="prefix"/> = 끝 시점 값이면 "end"(<c>endState</c> 만).
    /// 키: zonePlanned wallM wallName bossM attack bossState charge maxCharge nextChargeS state canMove canDashAct grounded inputSrc dashOn skillBusy activeSkill intCdS windowS.
    /// </summary>
    public static void Snapshot(Player me, TwentyThreeBoss boss, List<string> kv)
    {
        if (me == null)
            return;
        Vector3 pos = me.transform.position;
        float wall = WallDistance(pos, out string wallName);
        Add(kv, "zonePlanned", PlannedZone);
        Add(kv, "wallM", F2(wall));
        Add(kv, "wallName", wallName);
        if (boss != null)
        {
            Vector3 b = boss.transform.position;
            Add(kv, "bossM", F2(new Vector2(b.x - pos.x, b.z - pos.z).magnitude));
            Add(kv, "attack", AttackOf(boss));
            Add(kv, "bossState", boss.State.ToString());
        }
        PlayerDashController dash = me.GetComponent<PlayerDashController>();
        if (dash != null)
        {
            Add(kv, "charge", dash.PredictedCharge.ToString(CultureInfo.InvariantCulture));
            Add(kv, "maxCharge", dash.MaxCharge.ToString(CultureInfo.InvariantCulture));
            Add(kv, "nextChargeS", F2(dash.RemainingRecharge));
            Add(kv, "dashOn", B(dash.DashEnabled));
            Add(kv, "windowS", F3(dash.DashInterruptWindow));
        }
        Add(kv, "state", me.CurrentState.ToString());
        Add(kv, "canMove", B(me.CanMove));
        PlayerStateController sc = me.GetComponent<PlayerStateController>();
        if (sc != null)
            Add(kv, "canDashAct", B(sc.CanDashFromAction));
        PlayerGroundingSensor ground = me.GetComponent<PlayerGroundingSensor>();
        Add(kv, "grounded", ground != null ? B(ground.IsGrounded) : "na");
        Add(kv, "inputSrc", B(me.IsInputSource));
        PlayerSkillController skills = me.GetComponent<PlayerSkillController>();
        if (skills != null)
        {
            Add(kv, "skillBusy", B(skills.IsBusy));
            Add(kv, "activeSkill", skills.ActiveSkill != null ? skills.ActiveSkill.GetType().Name : "none");
            Add(kv, "intCdS", F2(skills.GetCooldownRemaining(PlayerSkillSlot.Interrupt)));
        }
    }

    public static void Add(List<string> kv, string k, string v)
    {
        kv.Add(k);
        kv.Add(string.IsNullOrEmpty(v) ? "na" : v);
    }

    public static string B(bool v) => v ? "true" : "false";
    public static string F2(double v) => double.IsNaN(v) ? "na" : v.ToString("0.00", CultureInfo.InvariantCulture);
    public static string F3(double v) => double.IsNaN(v) ? "na" : v.ToString("0.000", CultureInfo.InvariantCulture);
}
#endif
