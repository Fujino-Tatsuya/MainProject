using System;
using System.Collections.Generic;
using UnityEngine;

// 평타 오너 예측(할 일 256)의 서버 2차 검증 — 순수 규칙. MonoBehaviour 아님, EditMode 테스트 대상(BasicAttackVerifyTests).
// 오너가 자기 화면에서 콤보를 이어 휘두르고 Hit 시점에 판정을 요청하면, 서버는 거부·롤백 없이 판정 요청만 이 규칙으로 거른다.
// 걸리면 그 히트는 데미지만 없다(모션은 그대로). 문서: Docs/tech/owner-predicted-basic-attack.md.

/// <summary>검증 수치 — xlsx <c>SlowMotion</c> 시트(간파 허용 오차와 같은 곳, <see cref="InterruptManager"/> 칸).</summary>
public readonly struct BasicAttackVerifySettings
{
    public const float DefaultIntervalSlack = 0.15f;
    public const float DefaultAttackSpeedWindow = 0.5f;

    /// <summary>간격 여유(초) — 실제 간격에 더해 기대 간격과 비교한다(도착 지터·프레임 오차).</summary>
    public readonly float IntervalSlack;

    /// <summary>공속 창(초) — 기대 간격을 나누는 공속 = 이 시간 안에 있었던 최대 공속(공속 버프가 막 풀린 순간 관대).</summary>
    public readonly float AttackSpeedWindow;

    public BasicAttackVerifySettings(float intervalSlack, float attackSpeedWindow)
    {
        IntervalSlack = Mathf.Max(0f, intervalSlack);
        AttackSpeedWindow = Mathf.Max(0f, attackSpeedWindow);
    }

    public static BasicAttackVerifySettings Default => new BasicAttackVerifySettings(DefaultIntervalSlack, DefaultAttackSpeedWindow);

    /// <summary>지금 수치 — 세션의 <see cref="InterruptManager"/> 칸, 없으면(오프라인·테스트) 기본값.</summary>
    public static BasicAttackVerifySettings Current =>
        InterruptManager.Instance != null ? InterruptManager.Instance.BasicAttackVerify : Default;
}

/// <summary>
/// 한 타(콤보 단)의 클립 이벤트 시각 — 공속 1 기준 실제 초(클립 시각 ÷ 애니메이터 상태 속도).
/// 진입 시각(<see cref="EntryTime"/>)은 그 타가 처음부터 재생될 때 0, 루프백으로 들어올 때는 <see cref="LoopBackEntryTime"/>.
/// </summary>
public readonly struct BasicAttackStepTiming
{
    public readonly float[] HitTimes;
    public readonly float EndTime;
    public readonly float LoopBackEntryTime;

    public BasicAttackStepTiming(float[] hitTimes, float endTime, float loopBackEntryTime = 0f)
    {
        HitTimes = hitTimes ?? Array.Empty<float>();
        EndTime = endTime;
        LoopBackEntryTime = Mathf.Max(0f, loopBackEntryTime);
    }

    public int HitCount => HitTimes.Length;

    public float HitTime(int hitNumber) =>
        HitTimes.Length == 0 ? 0f : HitTimes[Mathf.Clamp(hitNumber, 0, HitTimes.Length - 1)];

    /// <summary>
    /// 클립 이벤트에서 시각을 읽는다 — Hit(여러 개면 시각 순)·End. <paramref name="stateSpeed"/> = 애니메이터 상태 Speed(공속 파라미터 제외).
    /// End 가 없으면 클립 길이. 클립이 없으면 빈 타이밍(기대 간격 0 = 하한 없음).
    /// </summary>
    public static BasicAttackStepTiming FromClip(AnimationClip clip, string functionName, int hitParameter, int endParameter,
        float stateSpeed = 1f, float loopBackEntryClipTime = 0f)
    {
        float speed = stateSpeed > 0f ? stateSpeed : 1f;
        if (clip == null)
            return new BasicAttackStepTiming(Array.Empty<float>(), 0f);

        var hits = new List<float>();
        float end = -1f;
        foreach (AnimationEvent clipEvent in clip.events)
        {
            if (clipEvent.functionName != functionName)
                continue;

            if (clipEvent.intParameter == hitParameter)
                hits.Add(clipEvent.time / speed);
            else if (clipEvent.intParameter == endParameter && end < 0f)
                end = clipEvent.time / speed;
        }

        hits.Sort();
        if (end < 0f)
            end = clip.length / speed;

        return new BasicAttackStepTiming(hits.ToArray(), end, loopBackEntryClipTime / speed);
    }
}

/// <summary>검증 결과. <see cref="Accepted"/> 만 데미지가 들어간다.</summary>
public enum BasicAttackHitVerdict
{
    Accepted,
    /// <summary>같은 (타, 히트 번호) 를 이미 받았다.</summary>
    Duplicate,
    /// <summary>타 순서가 어긋났다(직전 +1 / 루프 0 / 새 시작 0 이 아님) — 그 타의 히트는 전부.</summary>
    OutOfOrder,
    /// <summary>시작 통지를 받지 않은 타의 히트.</summary>
    UnknownSwing,
    /// <summary>실제 간격 + 여유 &lt; 기대 간격 ÷ 최근 최대 공속.</summary>
    TooFast
}

public static class BasicAttackVerifyRules
{
    /// <summary>
    /// 타 순서 — 이어지는 콤보면 직전 +1, 마지막 타 뒤 루프(<paramref name="loops"/>)면 0, 새 콤보면 0 만.
    /// </summary>
    public static bool IsValidStepOrder(bool continuesCombo, int previousStep, int nextStep, int stepCount, bool loops)
    {
        if (stepCount <= 0 || nextStep < 0 || nextStep >= stepCount)
            return false;

        if (!continuesCombo)
            return nextStep == 0;

        if (previousStep < 0)
            return false;

        if (nextStep == previousStep + 1)
            return true;

        return loops && previousStep == stepCount - 1 && nextStep == 0;
    }

    /// <summary>
    /// 기대 간격(공속 1 기준 초) — 직전 히트 (타 a, 히트 i) → 이번 히트 (타 b, 히트 j).
    /// 같은 타(같은 휘두름) = 히트 간 시각. 다른 휘두름 = a 의 Hit→End + b 의 진입→Hit(루프백 진입이면 LoopBackEntryTime).
    /// 크로스페이드·회전 지연은 간격을 늘리기만 하므로 넣지 않는다(관대).
    /// </summary>
    public static float ExpectedInterval(in BasicAttackStepTiming previous, int previousHit, in BasicAttackStepTiming next, int nextHit,
        bool sameSwing, bool nextIsLoopBack)
    {
        if (sameSwing)
            return Mathf.Max(0f, next.HitTime(nextHit) - next.HitTime(previousHit));

        float tail = Mathf.Max(0f, previous.EndTime - previous.HitTime(previousHit));
        float entry = nextIsLoopBack ? next.LoopBackEntryTime : 0f;
        float lead = Mathf.Max(0f, next.HitTime(nextHit) - entry);
        return tail + lead;
    }

    /// <summary>실제 간격 + 여유 ≥ 기대 간격 ÷ 공속. 공속이 0 이하이면 1 로 본다.</summary>
    public static bool PassesInterval(float actualInterval, float expectedInterval, float attackSpeed, float slack)
    {
        float speed = attackSpeed > 0f ? attackSpeed : 1f;
        return actualInterval + slack >= expectedInterval / speed;
    }
}

/// <summary>
/// 최근 공속 기록 — 창(<see cref="BasicAttackVerifySettings.AttackSpeedWindow"/>) 안의 최대값. 창 시작 시점에 유효하던 값(창보다 오래된 마지막 기록)도 넣는다.
/// </summary>
public sealed class BasicAttackSpeedHistory
{
    const int Capacity = 32;

    readonly double[] times = new double[Capacity];
    readonly float[] speeds = new float[Capacity];
    int count;
    int head; // 다음 쓸 칸

    public void Clear()
    {
        count = 0;
        head = 0;
    }

    public void Record(double now, float speed)
    {
        if (speed <= 0f)
            return;

        // 같은 값이 이어지면 기록하지 않는다 — 창 시작 값은 마지막 기록이 들고 있다.
        if (count > 0 && Mathf.Approximately(speeds[(head - 1 + Capacity) % Capacity], speed))
            return;

        times[head] = now;
        speeds[head] = speed;
        head = (head + 1) % Capacity;
        if (count < Capacity)
            count++;
    }

    /// <summary>[now − window, now] 의 최대 공속. 기록이 없으면 <paramref name="fallback"/>.</summary>
    public float MaxInWindow(double now, float window, float fallback)
    {
        if (count == 0)
            return fallback;

        float max = 0f;
        bool tookPreWindow = false;
        for (int n = 0; n < count; n++)
        {
            int i = (head - 1 - n + Capacity * 2) % Capacity; // 최신부터
            if (times[i] >= now - window)
            {
                max = Mathf.Max(max, speeds[i]);
                continue;
            }

            // 창보다 오래된 것 중 가장 최신 = 창 시작 때 유효하던 값.
            if (!tookPreWindow)
            {
                max = Mathf.Max(max, speeds[i]);
                tookPreWindow = true;
            }
            break;
        }

        return max > 0f ? max : fallback;
    }
}

/// <summary>
/// [서버] 한 플레이어 평타의 2차 검증 상태. 타 시작 통지(<see cref="BeginSwing"/>)·종료(<see cref="EndCombo"/>)·판정 요청(<see cref="VerifyHit"/>).
/// 휘두름은 오너가 매기는 일련번호(<c>swingSerial</c>, 타마다 +1, 넘치면 돈다)로 구분한다 — 루프로 같은 타 번호가 다시 와도 다른 휘두름이다.
/// </summary>
public sealed class BasicAttackServerVerifier
{
    readonly BasicAttackSpeedHistory speedHistory = new BasicAttackSpeedHistory();

    bool comboActive;
    bool hasSwing;
    byte swingSerial;
    int swingStep = -1;
    bool swingOrderValid;
    bool swingIsLoopBack;
    bool swingStartsCombo;
    uint swingHitMask;

    bool hasLastHit;
    int lastHitStep;
    int lastHitNumber;
    byte lastHitSerial;
    double lastHitTime;

    public bool ComboActive => comboActive;
    public int CurrentStep => swingStep;

    public void RecordAttackSpeed(double now, float speed) => speedHistory.Record(now, speed);

    /// <summary>
    /// 타 시작 통지. 서버는 거부하지 않는다 — 순서가 어긋나면 그 휘두름의 히트만 데미지가 없다. 반환 = 순서 유효.
    /// <paramref name="continuesCombo"/> = 오너가 직전 타의 창 안 입력으로 이었다고 보냄. 서버가 콤보를 끝낸 뒤(종료 통지·끊기)에 온
    /// '이어짐' 은 새 시작으로 본다(끊김과 통지가 엇갈린 경우 — 0 이 아니면 순서 위반).
    /// </summary>
    public bool BeginSwing(byte serial, int stepIndex, int stepCount, bool loops, bool continuesCombo)
    {
        bool continues = continuesCombo && comboActive && hasSwing;
        bool valid = BasicAttackVerifyRules.IsValidStepOrder(continues, swingStep, stepIndex, stepCount, loops);

        swingIsLoopBack = continues && stepIndex == 0;
        swingStartsCombo = !continues;
        hasSwing = true;
        comboActive = true;
        swingSerial = serial;
        swingStep = stepIndex;
        swingOrderValid = valid;
        swingHitMask = 0;

        if (swingStartsCombo)
            hasLastHit = false; // 새 콤보의 첫 히트는 하한 없이 통과

        return valid;
    }

    /// <summary>콤보 종료(오너 종료 통지·서버 끊기). 다음 시작은 새 콤보다.</summary>
    public void EndCombo()
    {
        comboActive = false;
        hasSwing = false;
        swingStep = -1;
        swingHitMask = 0;
        hasLastHit = false;
    }

    /// <summary>전부 지운다(스폰·사망 등). 공속 기록도.</summary>
    public void Reset()
    {
        EndCombo();
        speedHistory.Clear();
    }

    /// <summary>
    /// 판정 요청 검증. <paramref name="timeline"/>[타] = 그 타의 클립 이벤트 시각(공속 1 기준), <paramref name="currentAttackSpeed"/> = 지금 공속
    /// (기록이 없을 때만 쓴다). 받아들이면 그 히트를 기록한다 — 거부된 히트는 다음 간격의 기준이 되지 않는다.
    /// </summary>
    public BasicAttackHitVerdict VerifyHit(double now, byte serial, int stepIndex, int hitNumber,
        IReadOnlyList<BasicAttackStepTiming> timeline, float currentAttackSpeed, in BasicAttackVerifySettings settings)
    {
        if (!hasSwing || serial != swingSerial || stepIndex != swingStep)
            return BasicAttackHitVerdict.UnknownSwing;

        if (!swingOrderValid)
            return BasicAttackHitVerdict.OutOfOrder;

        if (hitNumber < 0 || hitNumber >= 32)
            return BasicAttackHitVerdict.OutOfOrder;

        uint bit = 1u << hitNumber;
        if ((swingHitMask & bit) != 0)
            return BasicAttackHitVerdict.Duplicate;

        if (hasLastHit && timeline != null && stepIndex < timeline.Count && lastHitStep < timeline.Count)
        {
            bool sameSwing = lastHitSerial == serial;
            float expected = BasicAttackVerifyRules.ExpectedInterval(timeline[lastHitStep], lastHitNumber, timeline[stepIndex], hitNumber,
                sameSwing, !sameSwing && swingIsLoopBack);
            float speed = speedHistory.MaxInWindow(now, settings.AttackSpeedWindow, currentAttackSpeed);
            if (!BasicAttackVerifyRules.PassesInterval((float)(now - lastHitTime), expected, speed, settings.IntervalSlack))
                return BasicAttackHitVerdict.TooFast;
        }

        swingHitMask |= bit;
        hasLastHit = true;
        lastHitStep = stepIndex;
        lastHitNumber = hitNumber;
        lastHitSerial = serial;
        lastHitTime = now;
        return BasicAttackHitVerdict.Accepted;
    }

    /// <summary>
    /// 고정 간격 검증(거너 연사 — 발 = 휘두름 하나·히트 하나). 직전 수락 발로부터 실제 간격 + 여유 ≥ 기대 간격 ÷ 최근 최대 공속.
    /// 직전 발이 없으면(새 연사) 통과. 받아들이면 그 발을 기록한다.
    /// </summary>
    public BasicAttackHitVerdict VerifyFixedInterval(double now, float expectedInterval, float currentAttackSpeed,
        in BasicAttackVerifySettings settings)
    {
        if (hasLastHit)
        {
            float speed = speedHistory.MaxInWindow(now, settings.AttackSpeedWindow, currentAttackSpeed);
            if (!BasicAttackVerifyRules.PassesInterval((float)(now - lastHitTime), expectedInterval, speed, settings.IntervalSlack))
                return BasicAttackHitVerdict.TooFast;
        }

        hasLastHit = true;
        lastHitStep = 0;
        lastHitNumber = 0;
        lastHitSerial = 0;
        lastHitTime = now;
        return BasicAttackHitVerdict.Accepted;
    }
}
