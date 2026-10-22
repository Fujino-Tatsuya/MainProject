using System;
using UnityEngine;

/// <summary>
/// 플레이어마다 하나인 쿨 시계. 피어마다 자기 시계로 쌓는다 — <b>시계 동기화 없음</b>(PLAN-91 §3.3).
/// 서버↔오너는 지금처럼 "남은 초"만 주고받는다(<c>CommitCooldownClientRpc</c> 등).
///
/// - 스킬 장부: <see cref="SkillNow"/> — 원천 = <see cref="Time.timeAsDouble"/> 증분(= Time.deltaTime 합, 슬로우 모션 반영).
/// - 배율: <see cref="SetRateSource"/> 로 주입한다. 주입 전 기본 1 = 기존 실시간 쿨과 같다.
///
/// 프리팹에 붙이지 않는다 — 쓰는 쪽이 <see cref="For"/> 로 같은 GameObject 에 하나만 붙여 공유한다.
/// 적분은 <see cref="CooldownClock"/>(순수)이 하고, 여기서는 프레임마다 한 번 원천 시각을 넣는다.
/// 읽기 전에도 그 프레임 분을 먼저 반영하므로 Update 순서와 무관하다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class PlayerCooldownClock : MonoBehaviour
{
    private readonly CooldownClock skillClock = new CooldownClock();
    private Func<float> rateSource;
    private int syncedFrame = -1;

    /// <summary>같은 GameObject 의 쿨 시계. 없으면 붙인다.</summary>
    public static PlayerCooldownClock For(Component owner)
    {
        if (owner == null)
            return null;

        return owner.TryGetComponent(out PlayerCooldownClock clock)
            ? clock
            : owner.gameObject.AddComponent<PlayerCooldownClock>();
    }

    /// <summary>쿨 줄어드는 속도 배율. 주입 전·주입값 null 이면 1.</summary>
    public float Rate => rateSource != null ? rateSource() : 1f;

    /// <summary>스킬 쿨 장부 시각(초). 배율 1 이면 Time.time 과 같은 속도로 흐른다.</summary>
    public float SkillNow
    {
        get
        {
            Sync();
            return (float)skillClock.Now;
        }
    }

    /// <summary>배율 공급자를 바꾼다(상태이상 합류 커밋에서 연결). null = 기본 1.</summary>
    public void SetRateSource(Func<float> source)
    {
        // 바꾸기 전 구간은 이전 배율로 마감한다.
        Sync();
        rateSource = source;
    }

    private void Update() => Sync();

    private void Sync()
    {
        int frame = Time.frameCount;
        if (syncedFrame == frame)
            return;

        syncedFrame = frame;
        skillClock.AdvanceTo(Time.timeAsDouble, Rate);
    }
}
