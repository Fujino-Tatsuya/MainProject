#if UNITY_EDITOR
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// [서버] 할 일 88 — 스위트 동안 보스 제한시간(<see cref="BossTimerManager"/>)을 멈춰 둔다(게임 코드 무수정, 리플렉션).
/// <para>풀타임 1차(88-20261022_180739): 존4 스킵(G8)에서 시작된 420초 제한시간이 런2 도중 만료돼 파티가 보스방으로 끌려갔고 중간보스 런이 공회전했다.
/// 중간보스 런은 런당 ~5분이라 한 번은 반드시 넘긴다. 일시정지 Dev API 가 없어(<see cref="BossTimerManager.StopForRetryServer"/> 는 되살릴 수 없다)
/// 비공개 만료 시각 <c>_expiresAt</c> 을 <see cref="HoldSeconds"/> 뒤로 미루고, 정리 때 남은 시간을 그대로 되돌린다.</para>
/// <list type="bullet">
/// <item><see cref="Tick"/>(호스트 스위트가 매 프레임): Running 인데 만료 시각이 우리가 쓴 값이 아니면(새로 시작·재시작된 타이머 포함) 남은 시간을 기억하고 미룬다.
///   이미 Expired 면 <see cref="BossTimerManager.StopForRetryServer"/> 로 강제 이동 재시도를 끊는다(되돌리지 않는다).</item>
/// <item><see cref="End"/>(정상 종료·Stop Suite·예외 — 스위트 Close, 에디터 감시자 정지): 아직 우리가 미룬 값 그대로면 지금 + 기억한 남은 시간으로 되돌린다.</item>
/// </list>
/// 로그 = <c>[QA88] event=boss_timer_hold|boss_timer_restore</c>(host.log).
/// </summary>
public sealed class QABossTimerHold88
{
    private const string Tag = QAInterruptLag88Log.QA88Tag;
    /// <summary>미룰 시간(초, 게임 시간) — 스위트 상한보다 충분히 길다.</summary>
    public const double HoldSeconds = 1_000_000d;

    private static readonly FieldInfo ExpiresField =
        typeof(BossTimerManager).GetField("_expiresAt", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo NowMethod = typeof(BossTimerManager).GetMethod("TimerNow",
        BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(NetworkManager) }, null);

    /// <summary>리플렉션 대상(<c>_expiresAt</c>·<c>TimerNow</c>)을 찾았는가 — EditMode 테스트가 게임 코드 이름 변경을 잡는다.</summary>
    public static bool ReflectionOk =>
        ExpiresField != null && ExpiresField.FieldType == typeof(NetworkVariable<double>) && NowMethod != null && NowMethod.ReturnType == typeof(double);

    /// <summary>지금 켜져 있는 것(감시자가 Play 를 멈추기 전에 되돌릴 수 있게).</summary>
    public static QABossTimerHold88 Active { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active = null;

    private bool _on;
    private BossTimerManager _timer;
    private double _heldExpiresAt;    // 우리가 쓴 만료 시각. 0 = 미루는 중 아님
    private double _savedRemaining;   // 미루기 직전 남은 시간(초)
    private int _holds, _expiredStops;
    private bool _failLogged;

    /// <summary>[서버] 켠다(이전 것이 남아 있으면 먼저 되돌린다). 곧바로 한 번 <see cref="Tick"/>.</summary>
    public static QABossTimerHold88 Begin()
    {
        ReleaseActive("replaced");
        var h = new QABossTimerHold88 { _on = true };
        Active = h;
        BossTimerManager t = BossTimerManager.Instance;
        Debug.Log($"{Tag} event=boss_timer_hold_begin expiresField={(ExpiresField != null ? "found" : "missing")} nowMethod={(NowMethod != null ? "found" : "missing")} " +
                  $"timer={(t != null ? t.State.ToString() : "none")} remainingS={(t != null ? t.RemainingSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) : "na")}");
        h.Tick("suite_begin");
        return h;
    }

    /// <summary>켜져 있는 것을 되돌린다(없으면 무시). 스위트 정리·에디터 감시자에서 부른다.</summary>
    public static void ReleaseActive(string reason)
    {
        QABossTimerHold88 h = Active;
        Active = null;
        h?.End(reason);
    }

    /// <summary>[서버] 새로 돌기 시작한(또는 아직 안 미룬) 제한시간을 미룬다. 매 프레임 불러도 된다(미룬 값이면 아무것도 안 한다).</summary>
    public void Tick(string where)
    {
        if (!_on)
            return;
        BossTimerManager t = BossTimerManager.Instance;
        if (t == null || !t.IsServer || !t.IsSpawned)
            return;
        if (t != _timer)
        {
            _timer = t;
            _heldExpiresAt = 0d;
        }

        switch (t.State)
        {
            case BossTimerManager.TimerState.Running:
                if (!TryGetVar(t, out NetworkVariable<double> expires) || !TryNow(t, out double now))
                    return;
                if (_heldExpiresAt > 0d && expires.Value == _heldExpiresAt)
                    return;
                _savedRemaining = BossTimerManager.RemainingAt(expires.Value, now);
                _heldExpiresAt = now + HoldSeconds;
                expires.Value = _heldExpiresAt;
                _holds++;
                Debug.Log($"{Tag} event=boss_timer_hold where={QAInterruptLag88Plan.LogToken(where)} n={_holds} savedRemainingS={F(_savedRemaining)} " +
                          $"limitS={F(t.LimitSeconds)} holdS={HoldSeconds:0}");
                break;
            case BossTimerManager.TimerState.Expired:
                // 이미 만료 — 강제 이동 재시도(0.5초마다 ForceStartEncounter)를 끊는다. 되돌리지 않는다(만료로 되살리면 정리 직후 끌려간다).
                t.StopForRetryServer();
                _heldExpiresAt = 0d;
                _expiredStops++;
                Debug.LogWarning($"{Tag} event=boss_timer_hold where={QAInterruptLag88Plan.LogToken(where)} action=stop_expired n={_expiredStops} — 이미 만료돼 Stopped 로 둔다(원복 안 함)");
                break;
        }
    }

    /// <summary>[서버] 되돌린다 — 아직 우리가 미룬 값이면 지금 + 미루기 직전 남은 시간. 여러 번 불러도 된다.</summary>
    public void End(string reason)
    {
        if (!_on)
            return;
        _on = false;
        if (Active == this)
            Active = null;
        string result;
        try
        {
            result = Restore();
        }
        catch (System.Exception e)
        {
            result = "error_" + e.GetType().Name;
            Debug.LogWarning($"{Tag} 보스 제한시간 원복 중 오류: {e.Message}");
        }
        Debug.Log($"{Tag} event=boss_timer_restore reason={QAInterruptLag88Plan.LogToken(reason)} result={result} holds={_holds} expiredStops={_expiredStops} " +
                  $"restoredRemainingS={(result == "restored" ? F(_savedRemaining) : "na")}");
    }

    private string Restore()
    {
        BossTimerManager t = _timer;
        if (_heldExpiresAt <= 0d)
            return "not_held";
        if (t == null || !t.IsServer || !t.IsSpawned)
            return "timer_gone";
        if (t.State != BossTimerManager.TimerState.Running)
            return "state_" + t.State;   // 미루는 동안 보스방 도착(Stopped) 등 — 그대로 둔다
        if (!TryGetVar(t, out NetworkVariable<double> expires) || !TryNow(t, out double now))
            return "reflection_failed";
        if (expires.Value != _heldExpiresAt)
            return "changed_by_game";    // 게임이 다시 시작했다 — 건드리지 않는다
        expires.Value = now + _savedRemaining;
        _heldExpiresAt = 0d;
        return "restored";
    }

    private bool TryGetVar(BossTimerManager t, out NetworkVariable<double> v)
    {
        v = ExpiresField != null ? ExpiresField.GetValue(t) as NetworkVariable<double> : null;
        if (v == null)
            LogFailOnce("expires_field_missing");
        return v != null;
    }

    private bool TryNow(BossTimerManager t, out double now)
    {
        now = 0d;
        if (NowMethod == null)
        {
            LogFailOnce("timer_now_missing");
            return false;
        }
        now = (double)NowMethod.Invoke(null, new object[] { t.NetworkManager });
        return true;
    }

    private void LogFailOnce(string reason)
    {
        if (_failLogged)
            return;
        _failLogged = true;
        Debug.LogWarning($"{Tag} event=boss_timer_hold ok=false reason={reason} — 제한시간을 멈출 수 없다(BossTimerManager 비공개 멤버 이름 확인)");
    }

    private static string F(double v) => v.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
}
#endif
