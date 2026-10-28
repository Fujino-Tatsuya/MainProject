using UnityEngine;

/// <summary>
/// 뇌진탕(<see cref="StatusEffectType.Concussion"/>, 할 일 125) 한 대상의 대기 상태 — MonoBehaviour 밖 순수 계산(EditMode 테스트가 고정한다).
/// 규칙(계획 §4·명세 §4·팔라딘 3차 제압의 진격):
///  - 맞을 때마다 대기 기절 길이를 들어온 값으로 <b>재설정</b>한다(가산·큰 값 아님).
///  - 밀치기(넉백) 중이면 대기 → 넉백이 끝나는 순간 기절로 바뀐다. 넉백 중이 아니면 걸리는 즉시 기절.
///  - 바뀐 기절은 이미 걸린 기절과 남은 시간 중 큰 값(합산 아님 — <see cref="MergeStun"/>).
///  - 슈퍼아머면 거부(<see cref="StatusEffectImmunityPolicy"/>). 넉백 종료 순간에도 자격을 다시 본다.
///  - 상태가 제압(그로기)으로 바뀌면 대기 중인 기절은 취소(<see cref="Cancel"/> — 호출은 수신측 상태 전이).
/// 서버 전용 값이다 — 복제되는 것은 수신측이 거는 Concussion·Stunned 플래그뿐.
/// </summary>
public struct ConcussionState
{
    /// <summary>제압의 진격(PAL_3_Q_STUN) 테스트값 — 밀치기 끝난 뒤 기절 0.75초.</summary>
    public const float DefaultStunSeconds = 0.75f;

    public enum ApplyResult
    {
        Rejected,   // 슈퍼아머·0 이하 길이 — 아무것도 안 바뀜
        Pending,    // 넉백 중 — 넉백이 끝나면 기절
        StunNow,    // 넉백 중이 아님 — 지금 기절(길이는 MergeStun 으로)
    }

    /// <summary>넉백이 끝나면 걸 기절 길이(초). 0 = 대기 없음.</summary>
    public float PendingStun { get; private set; }

    public bool IsPending => PendingStun > 0f;

    /// <summary>뇌진탕 한 번 적중. <paramref name="activeEffects"/> = 지금 대상에 걸린 플래그(슈퍼아머 판정용).</summary>
    public ApplyResult Apply(float stunSeconds, bool knockedBack, StatusEffectType activeEffects)
    {
        if (stunSeconds <= 0f ||
            StatusEffectImmunityPolicy.ShouldIgnoreApplication(activeEffects, StatusEffectType.Concussion))
            return ApplyResult.Rejected;

        if (!knockedBack)
        {
            PendingStun = 0f;
            return ApplyResult.StunNow;
        }

        PendingStun = stunSeconds;   // 재설정 — 더 길게 남아 있어도 들어온 값으로
        return ApplyResult.Pending;
    }

    /// <summary>
    /// 넉백이 끝났다 — 걸 기절 길이를 돌려주고 대기를 비운다. 대기가 없거나 지금 슈퍼아머면 0(자격 재확인).
    /// </summary>
    public float ConsumeOnKnockbackEnd(StatusEffectType activeEffects)
    {
        float stun = PendingStun;
        PendingStun = 0f;
        if (StatusEffectImmunityPolicy.ShouldIgnoreApplication(activeEffects, StatusEffectType.Stunned))
            return 0f;
        return stun;
    }

    /// <summary>대기 취소(제압 진입·넉백이 아닌 경로로 끝남·해제). 취소했으면 true.</summary>
    public bool Cancel()
    {
        bool had = IsPending;
        PendingStun = 0f;
        return had;
    }

    /// <summary>기절 겹침 — 남은 시간 중 큰 값(합산 아님).</summary>
    public static float MergeStun(float existingRemaining, float incoming) =>
        Mathf.Max(Mathf.Max(0f, existingRemaining), Mathf.Max(0f, incoming));
}
