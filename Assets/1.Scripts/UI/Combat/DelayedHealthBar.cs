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

    public void Tick(float deltaTime, int hp, int maxHp)
    {
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

        if (delayedFill != null)
            delayedFill.fillAmount = maxHp > 0 ? Mathf.Clamp01(displayed / maxHp) : 0f;
    }
}
