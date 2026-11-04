using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 체력바 뒤에 깔리는 잔상(지연) 바. 피격 시 앞바는 즉시 줄고, 이 바는 옛 HP에
/// holdSeconds 만큼 머문 뒤 고정 속도(최대HP * drainRatePerSecond /초)로 따라 내려온다.
/// 피해 조각을 큐로 붙잡되 maxHeldHits를 넘으면 가장 오래된 조각부터 놓아준다 —
/// 지속 피해(도트·장판)나 다인 공격에서 잔상이 전투 시작 HP에 영구 고착하는 것을 막는다.
/// HUD가 Unit.ClientHpChanged를 이 클래스의 OnHpChanged로 흘려주고, 매 프레임 Tick을 돌린다.
/// </summary>
[Serializable]
public sealed class DelayedHealthBar
{
    [SerializeField] private Image delayedFill;
    [SerializeField, Min(0f)] private float holdSeconds = 0.4f;
    [SerializeField, Min(0f)] private float drainRatePerSecond = 0.8f;
    [SerializeField, Min(1)] private int maxHeldHits = 5;
    [SerializeField] private bool resetHoldOnDamage = true;

    private readonly Queue<int> held = new Queue<int>();
    private float displayed;
    private float holdTimer;
    private int heldTotal;
    private bool frozen;

    /// <summary>
    /// 잔상을 그 자리에 멈춘다(보스 제압 동안 — 할 일 174). 멈춘 동안 깎인 양이 흰 칸으로 쌓여 보이고,
    /// 풀리면 쌓인 양 전체를 조각 하나로 붙잡아 평소 규칙(holdSeconds 머문 뒤 내려감)으로 따라 내려간다.
    /// 규칙은 <see cref="DelayedHealthFreezeRule"/>.
    /// </summary>
    public void SetFrozen(bool value, int hp)
    {
        if (frozen == value)
            return;

        frozen = value;
        if (value)
            return;

        int pending = DelayedHealthFreezeRule.PendingOnRelease(displayed, hp);
        held.Clear();
        heldTotal = 0;
        if (pending <= 0)
            return;

        held.Enqueue(pending);
        heldTotal = pending;
        holdTimer = holdSeconds;
    }

    /// <summary>잔상 Image 가 연결돼 있는가. 비어 있으면 구독·Tick 을 건너뛰어도 된다(머리 위 플레이어 바).</summary>
    public bool HasFill => delayedFill != null;

    public void Bind(int hp)
    {
        held.Clear();
        heldTotal = 0;
        holdTimer = 0f;
        displayed = Mathf.Max(0, hp);
    }

    public void OnHpChanged(int previous, int next)
    {
        if (next <= 0)
        {
            Bind(0);
            return;
        }

        if (next >= previous)
        {
            // 멈춘 동안 회복은 쌓인 흰 칸을 지우지 않는다 — 잔상은 Tick 이 체력 아래로 내려가지 않게만 맞춘다.
            if (frozen)
                return;
            Bind(next);
            return;
        }

        bool wasEmpty = held.Count == 0;
        int damage = previous - next;
        held.Enqueue(damage);
        heldTotal += damage;

        if (resetHoldOnDamage || wasEmpty)
            holdTimer = holdSeconds;

        int hitLimit = Mathf.Max(1, maxHeldHits);
        while (held.Count > hitLimit)
            heldTotal -= held.Dequeue();
    }

    /// <summary>잔상 Image 를 이 비율로 덮어 그린다(내부 값은 그대로) — 보스 HP바 등장 차오름 동안만(앞 바 뒤에 숨겨 둔다).</summary>
    public void ForceFill(float amount01)
    {
        if (delayedFill != null)
            delayedFill.fillAmount = Mathf.Clamp01(amount01);
    }

    /// <summary>잔상 Image(같은 바 모양 스프라이트) — 등장 차오름의 흰 구간이 이 모양·자리를 빌린다. 없으면 null.</summary>
    public Image FillImage => delayedFill;

    public void Tick(float deltaTime, int hp, int maxHp)
    {
        if (frozen)
        {
            displayed = DelayedHealthFreezeRule.FrozenDisplayed(displayed, hp);
            ApplyFill(maxHp);
            return;
        }

        if (held.Count > 0)
        {
            holdTimer = Mathf.Max(0f, holdTimer - Mathf.Max(0f, deltaTime));
            if (holdTimer <= 0f)
            {
                held.Clear();
                heldTotal = 0;
            }
        }

        float target = Mathf.Max(0, hp) + heldTotal;
        float maxDelta = Mathf.Max(0, maxHp) * Mathf.Max(0f, drainRatePerSecond) * Mathf.Max(0f, deltaTime);
        displayed = Mathf.MoveTowards(displayed, target, maxDelta);
        ApplyFill(maxHp);
    }

    private void ApplyFill(int maxHp)
    {
        if (delayedFill != null)
            delayedFill.fillAmount = maxHp > 0 ? Mathf.Clamp01(displayed / maxHp) : 0f;
    }
}

/// <summary>
/// 잔상 멈춤 규칙(할 일 174) — UnityEngine 의존 없음, EditMode 테스트 대상.
///   · 멈춘 동안 잔상은 내려가지 않는다. 체력이 잔상보다 위로 오르면(회복) 체력까지만 올라간다(앞 바보다 낮게 그리지 않는다).
///   · 풀릴 때 쌓인 양 = 잔상 − 체력(반올림, 0 아래 없음) — 조각 하나로 붙잡아 평소처럼 머문 뒤 내려간다.
/// </summary>
public static class DelayedHealthFreezeRule
{
    public static float FrozenDisplayed(float displayed, int hp) => System.Math.Max(displayed, System.Math.Max(0, hp));

    public static int PendingOnRelease(float displayed, int hp) =>
        System.Math.Max(0, (int)System.Math.Round(displayed - System.Math.Max(0, hp)));
}
