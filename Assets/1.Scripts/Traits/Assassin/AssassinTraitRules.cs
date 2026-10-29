using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 칼날의 영역(ASN_3_E_AREA, 할 일 128 2/5) — 어쌔신 판정 치수에 범위 배율(⑦ <see cref="PlayerTraitModifiers.AreaMultiplier"/>)을 바르는 순수 규칙.
/// 면적이 아니라 치수 배율이다(명세 — 길이·폭·반경 각각 ×1.25). 자기 이동·중심 오프셋은 바꾸지 않는다. VFX 크기는 여기서 다루지 않는다(판정·예고만).
/// </summary>
public static class AssassinTraitAreaRules
{
    /// <summary>일반 E 강타(강화한 기본 공격) — 판정 도형 길이·폭.</summary>
    public static readonly TraitAttackKey EnhancedStrike = new TraitAttackKey(AttackType.Default, TraitAttackForm.Enhanced);

    /// <summary>변신 E 원형 5타 — 원 반경.</summary>
    public static readonly TraitAttackKey CircleStrike = new TraitAttackKey(AttackType.SkillE, TraitAttackForm.Alternate);

    /// <summary>그 공격 키의 길이(Length 축)·폭(Width 축) 배율. 특성 출처가 없으면 둘 다 1, 음수는 0.</summary>
    public static void AreaScale(PlayerTraitModifiers modifiers, TraitAttackKey key, out float lengthMultiplier, out float widthMultiplier)
    {
        lengthMultiplier = modifiers != null ? Mathf.Max(0f, modifiers.AreaMultiplier(key, TraitAreaAxis.Length)) : 1f;
        widthMultiplier = modifiers != null ? Mathf.Max(0f, modifiers.AreaMultiplier(key, TraitAreaAxis.Width)) : 1f;
    }

    /// <summary>
    /// 평타 판정 도형(할 일 153)에 길이·폭 배율을 바른 <b>복사본</b> — 부채꼴이면 range × 길이, angle × 폭(<see cref="AttackShape.Scaled"/>).
    /// 기준점(몸 발밑)은 그대로라 꼭짓점은 안 움직인다. 배율이 둘 다 1 이면 원본을 그대로 돌려준다(SO 를 고치지 않으므로 공유해도 된다).
    /// </summary>
    public static AttackShapeSet ScaleArea(PlayerTraitModifiers modifiers, TraitAttackKey key, AttackShapeSet area)
    {
        AreaScale(modifiers, key, out float length, out float width);
        return area == null || (length == 1f && width == 1f) ? area : area.Scaled(length, width);
    }

    /// <summary>원 반경 × 배율(0 미만 배율은 0).</summary>
    public static float ScaleRadius(float radius, float multiplier) => radius * Mathf.Max(0f, multiplier);

    /// <summary>스킬 판정·예고용 — 그 공격 키의 반경 배율. 특성 출처가 없으면 그대로.</summary>
    public static float ScaleRadius(PlayerTraitModifiers modifiers, TraitAttackKey key, float radius) =>
        ScaleRadius(radius, modifiers != null ? modifiers.AreaMultiplier(key, TraitAreaAxis.Radius) : 1f);
}

/// <summary>절제된 분노 적용 중 서버가 이번 틱에 할 일(<see cref="AssassinRestraintRules.Decide"/>).</summary>
public enum AssassinRestraintAction
{
    None,
    /// <summary>변신 중 — 종료를 요청한다(해제 제한 시간 무시). 실제 종료는 기존 "현재 공격 완료 후" 경로가 한다.</summary>
    RequestTransformEnd,
    /// <summary>일반 상태인데 분노가 남았다 — 0 으로.</summary>
    ClearRage,
}

/// <summary>
/// 절제된 분노(ASN_2_RESTRAINT, 할 일 128 4/5) — 모드 변경 스위치(<see cref="TraitModeIds.AssassinRestraint"/>)의 순수 규칙.
/// R(변신) 사용 불가 · 분노 획득 중지 · 분노 0. 패시브 백어택은 그대로. 수치(일반 Q·강타 피해 ×1.35, 일반 Q·E 쿨 ×0.75)는 정의 에셋 효과.
/// 이미 변신 중에 고르면 즉시 강제 취소하지 않는다 — 종료 요청만 넣고 기존 안전 종료(현재 원자 공격 완료 후, <see cref="AssassinStateModel.ShouldFinishTransform"/>)가
/// 변신을 끝낸 뒤 다음 틱에 분노를 0 으로 만든다. 서버만 쓰고 스냅샷 복제로 클라에 간다.
/// </summary>
public static class AssassinRestraintRules
{
    /// <summary>R 변신 시작 가능 — 절제된 분노면 언제나 불가.</summary>
    public static bool CanBeginTransform(bool restrained, bool canBeginOtherwise) => !restrained && canBeginOtherwise;

    /// <summary>분노 획득(적중 충전·훈련 보정) 가능 — 절제된 분노면 불가.</summary>
    public static bool CanGainRage(bool restrained) => !restrained;

    /// <summary>공용 스킬 HUD 에서 이 슬롯을 숨길까 — 절제된 분노면 R 칸만(할 일 133).</summary>
    public static bool HidesHudSlot(bool restrained, PlayerSkillSlot slot) => restrained && slot == PlayerSkillSlot.Ultimate;

    /// <summary>머리 위 분노 게이지 줄을 보일까 — 절제된 분노면 숨긴다(<see cref="IOverheadTraitGauge.Visible"/>).</summary>
    public static bool ShowsRageGauge(bool restrained) => !restrained;

    /// <summary>이번 틱에 할 일. 종료 요청이 이미 들어가 있으면 기다린다(종료 대기 중 분노는 건드리지 않는다).</summary>
    public static AssassinRestraintAction Decide(bool restrained, in AssassinStateSnapshot state)
    {
        if (!restrained)
            return AssassinRestraintAction.None;

        if (state.transformed)
            return state.releaseRequested ? AssassinRestraintAction.None : AssassinRestraintAction.RequestTransformEnd;

        return state.rage > 0f ? AssassinRestraintAction.ClearRage : AssassinRestraintAction.None;
    }

    /// <summary><paramref name="action"/> 을 스냅샷에 적용한다. 바뀌었으면 true.</summary>
    public static bool Apply(ref AssassinStateSnapshot state, AssassinRestraintAction action)
    {
        switch (action)
        {
            case AssassinRestraintAction.RequestTransformEnd:
                if (!state.transformed || state.releaseRequested)
                    return false;
                state.releaseRequested = true;
                return true;
            case AssassinRestraintAction.ClearRage:
                if (state.transformed || state.rage <= 0f)
                    return false;
                state.rage = 0f;
                return true;
            default:
                return false;
        }
    }
}

/// <summary>
/// 그림자 회귀(ASN_2_RETURN, 할 일 128 5b) — 모드 변경 스위치(<see cref="TraitModeIds.AssassinShadowReturn"/>)의 순수 규칙.
/// 일반·변신 Q 각각을 쓰면 그 칸이 복귀 돌진(<see cref="AssassinShadowReturnSkill"/>, 공용 후속 시전 장치)으로 바뀐다. 창·쿨 시작·강제 복귀는 장치가 한다.
/// 수치: 창 = <see cref="WindowKey"/>, 복귀 피해 비율 = <see cref="DamageRatioKey"/>(정의 에셋 ⑩ 스킬 수치), Q 전체 쿨 ×1.20 = 정의 에셋 쿨 효과.
/// </summary>
public static class AssassinShadowReturnRules
{
    /// <summary>복귀 창(초) 스킬 수치 이름 — 첫 돌진 종료부터, 실제 시간(공격 가속으로 줄지 않음).</summary>
    public const string WindowKey = "ASN_Q_RETURN_WINDOW";
    /// <summary>복귀 피해 비율 스킬 수치 이름 — 복귀 단계 실행 때의 원래 Q 기준 피해(Dref) × 비율.</summary>
    public const string DamageRatioKey = "ASN_Q_RETURN_DAMAGE_RATIO";
    public const float DefaultDamageRatio = 0.6f;

    /// <summary>원래 Q 사용이 복귀 칸을 여는가 — 복귀 스킬이 연결돼 있고 그림자 회귀를 골랐을 때만(미보유 = 기존 Q, 사용 시 쿨 시작).</summary>
    public static bool OpensReturn(bool returnLinked, bool hasShadowReturn) => returnLinked && hasShadowReturn;

    /// <summary>
    /// 복귀 돌진 방향·거리 — 현재 위치 → 저장된 출발점(수평), 거리 = min(남은 거리, 현재 Q 최대 거리). 저장점 도착은 보장하지 않는다(지형 정지는 이동 규칙).
    /// 이미 출발점 위면 거리 0, 방향은 <paramref name="fallbackForward"/>(수평, 없으면 +Z).
    /// </summary>
    public static void Resolve(Vector3 current, Vector3 origin, float maxDistance, Vector3 fallbackForward,
        out Vector3 direction, out float distance)
    {
        Vector3 toOrigin = origin - current;
        toOrigin.y = 0f;
        float remaining = toOrigin.magnitude;
        if (remaining < AssassinTrailRules.MinLength)
        {
            fallbackForward.y = 0f;
            direction = fallbackForward.sqrMagnitude > 1e-6f ? fallbackForward.normalized : Vector3.forward;
            distance = 0f;
            return;
        }

        direction = toOrigin / remaining;
        distance = Mathf.Min(remaining, Mathf.Max(0f, maxDistance));
    }

    /// <summary>복귀 단계 기준 피해 = 그 단계 실행 때 원래 Q 기준 피해 × 비율(반올림 한 번). 이 값이 판정 피해이자 그 단계 추격의 궤적 기준이다.</summary>
    public static int StageDamage(int sourceDref, float ratio) => TraitDerivedAttackRules.Damage(sourceDref, ratio);
}

/// <summary>
/// [서버·각 피어] 그림자 회귀 출발점 — 원래 Q 돌진이 시작될 때 저장하고 복귀 돌진이 한 번 가져간다(가져가면 비움).
/// 일반·변신 Q 가 각자 하나씩 가져 다른 상태 Q 가 과거 저장점을 쓰지 않는다. 복귀 칸이 닫히면(만료·사망·R 전환·씬 이동) 서버가 버린다.
/// </summary>
public sealed class AssassinShadowReturnOrigin
{
    private Vector3 point;

    public bool HasValue { get; private set; }

    public void Save(Vector3 position)
    {
        point = position;
        HasValue = true;
    }

    public bool TryTake(out Vector3 position)
    {
        position = point;
        if (!HasValue)
            return false;

        Discard();
        return true;
    }

    public void Discard()
    {
        HasValue = false;
        point = default;
    }
}

/// <summary>
/// 어쌔신 기본 공격 동작과 특성 사건의 묶음 규칙(할 일 128 2/5 — 돌파의 여세·사냥의 박자).
/// 동작 1개 = 일반 연계 한 타 · 강타 1회 · 변신 4타 묶음 1개 — 동작마다 시전 ID 하나(<see cref="Player.NextServerCastId"/>)를 받아
/// 그 동작의 모든 판정을 같은 ID 로 알린다(사냥의 박자 '동작당 1회' = 효과 거름 oncePerCast).
/// </summary>
public static class AssassinBasicAttackTraitRules
{
    /// <summary>
    /// 돌파의 여세(⑨) 소모 자리 — 그 동작의 첫 원래 피해 판정(1부터 센 판정 번호 = 1)에서만 소모를 묻는다.
    /// 빗나가도 그 판정에서 소모되고, 변신 4타 묶음의 2~4타는 강화되지 않는다.
    /// </summary>
    public static bool ConsumesNextAttackBuff(int hitNumberInAction) => hitNumberInAction == 1;
}

/// <summary>
/// 급소 파괴(ASN_4_NORMAL_E_PUSH, 할 일 128 3/5) — 일반 E 강타 적중 순간의 밀치기 방향. 밀치기 자체(CC)는 공용 <see cref="TraitKnockbackEffect.ApplyCrowdControl"/> 한 곳이 건다.
/// 강타만 거르는 건 효과 칸(attackTypes [Default] · forms [Enhanced]) — 변신 E(SkillE/Alternate)·일반 E 준비는 키가 달라 맞지 않는다. 대상별 1회 = 동작 시전 ID × 대상.
/// </summary>
public static class AssassinStrikePushRules
{
    /// <summary>
    /// 밀치는 방향 = 플레이어 → 대상(수평). 같은 위치(수평 거리 ≈ 0)면 강타 조준 방향 <paramref name="aimDirection"/>. 둘 다 0 이면 0(수신측이 방사형으로 계산).
    /// </summary>
    public static Vector3 Direction(Vector3 attackerPosition, Vector3 targetPosition, Vector3 aimDirection)
    {
        Vector3 toTarget = targetPosition - attackerPosition;
        toTarget.y = 0f;
        if (toTarget.sqrMagnitude > 1e-6f)
            return toTarget.normalized;

        aimDirection.y = 0f;
        return aimDirection.sqrMagnitude > 1e-6f ? aimDirection.normalized : Vector3.zero;
    }
}

/// <summary>
/// 어쌔신 파생 공격(⑧ <see cref="TraitDerivedAttackEffect"/>)의 이름 — 정의 에셋 <c>derivedKey</c> 와 스킬 코드가 같은 값을 쓴다(할 일 128 3/5).
/// 파생 판정 = <see cref="TraitHitFlags.Derived"/>: 피해만(특성 사건·분노·쿨감·CC·간파·다음 공격 강화 없음, <see cref="Player.RaiseServerAttackLanded"/> 금지), VFX 없음.
/// </summary>
public static class AssassinDerivedAttackKeys
{
    /// <summary>추격의 궤적 — Q 돌진 단계가 정상 종료되면 지나온 선분에 한 번.</summary>
    public const string QTrail = "ASN_Q_TRAIL";

    /// <summary>끝없는 칼날 — 기본 공격 4타(변신 묶음 마지막 판정)를 실행하면 같은 범위에 한 번.</summary>
    public const string ComboExtra = "ASN_COMBO_EXTRA";

    /// <summary><paramref name="attacks"/>(<see cref="PlayerTraitEffects.GetDerivedAttacks"/> 결과)에서 이름이 같은 첫 명세. 없으면 false.</summary>
    public static bool TryFind(IReadOnlyList<TraitDerivedAttack> attacks, string key, out TraitDerivedAttack found)
    {
        found = null;
        if (attacks == null)
            return false;

        for (int i = 0; i < attacks.Count; i++)
        {
            if (attacks[i] != null && string.Equals(attacks[i].Key, key, System.StringComparison.Ordinal))
            {
                found = attacks[i];
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// 추격의 궤적(ASN_4_Q_TRAIL, 할 일 128 3/5) — Q 돌진 '단계' 하나가 끝났을 때 궤적을 예약할지와 그 선분.
/// 단계 = 일반·변신 Q 출발 돌진(그림자 회귀 복귀 돌진도 같은 함수로 — 기준 피해만 복귀 배율을 바른 값).
/// 선분 = 출발점 → 출발점 + 방향 × 실제 이동 거리(서버 재구성 경로, 벽 조기 종료 보고로 줄어든 상한까지 — 벽 너머·이동 안 한 구간 제외).
/// </summary>
public static class AssassinTrailRules
{
    /// <summary>이보다 짧게 움직인 단계는 지나온 길이 없다고 본다(출발 전에 끝남 등).</summary>
    public const float MinLength = 0.01f;

    /// <summary>
    /// 그 돌진 단계가 궤적을 남기는가 — 정상 종료(<see cref="SkillEndReason.Completed"/>, 벽 앞 안전 정지 포함)이고 실제로 움직였을 때만.
    /// 강제 중단(사망·피격·대시 취소·안전망 종료)은 없음.
    /// </summary>
    public static bool Schedules(SkillEndReason reason, float traveled) =>
        reason == SkillEndReason.Completed && !float.IsNaN(traveled) && !float.IsInfinity(traveled) && traveled >= MinLength;

    /// <summary>실제 지나온 선분의 끝점(수평 방향 × 거리). 거리는 0 아래로 내려가지 않는다.</summary>
    public static Vector3 SegmentEnd(Vector3 start, Vector3 direction, float traveled)
    {
        direction.y = 0f;
        direction = direction.sqrMagnitude > 1e-6f ? direction.normalized : Vector3.zero;
        return start + direction * Mathf.Max(0f, traveled);
    }

    /// <summary>선분을 원래 Q 경로 폭·높이의 상자로 — 중심은 선분 가운데(높이 절반만 올림), 회전은 선분 방향.</summary>
    public static void Box(Vector3 start, Vector3 end, float width, float height,
        out Vector3 center, out Vector3 halfExtents, out Quaternion rotation)
    {
        Vector3 along = end - start;
        along.y = 0f;
        float length = along.magnitude;
        center = (start + end) * 0.5f + Vector3.up * (Mathf.Max(0f, height) * 0.5f);
        halfExtents = new Vector3(Mathf.Max(0f, width) * 0.5f, Mathf.Max(0f, height) * 0.5f, length * 0.5f);
        rotation = length > 1e-6f ? Quaternion.LookRotation(along / length) : Quaternion.identity;
    }

    /// <summary>궤적 지연 — 끝없는 칼날과 같은 공격 가속기를 탄다(명세 §6): 지연 ÷ 공격속도 배율.</summary>
    public static float Delay(float delay, float attackSpeedRate) => AssassinComboExtraRules.Delay(delay, attackSpeedRate);
}

/// <summary>
/// 끝없는 칼날(ASN_4_COMBO_EXTRA, 할 일 128 3/5) — 어느 판정에서 추가 베기를 예약하나.
/// 일반 연계 4타(마지막 원래 타)의 마지막 판정 · 변신 4타 묶음의 마지막 판정만. 일반 E 강타는 독립 단발이라 세지 않는다.
/// 일반 연계는 콤보 모델이 끊기면(유예 만료·스킬·대시·피격 취소) 1타부터라, 마지막 타에 닿았다 = 1~4타가 이어졌다.
/// </summary>
public static class AssassinComboExtraRules
{
    /// <param name="stepIndex">일반 연계 타 번호(0부터). 변신 묶음·강타는 무시.</param>
    /// <param name="hitNumber">그 동작 안에서 1부터 센 판정 번호.</param>
    public static bool Schedules(AssassinBasicAttackMode mode, int stepIndex, int normalStepCount, int hitNumber, int hitCount)
    {
        if (hitNumber < 1 || hitNumber != Mathf.Max(1, hitCount))
            return false;

        switch (mode)
        {
            case AssassinBasicAttackMode.Normal:
                return normalStepCount > 0 && stepIndex == normalStepCount - 1;
            case AssassinBasicAttackMode.Transformed:
                return true;
            default:
                return false;
        }
    }

    /// <summary>추가 베기 지연 — 공격 가속기(공격속도 배율, 제압 가속 포함)를 탄다: 지연 ÷ 배율.</summary>
    public static float Delay(float delay, float attackSpeedRate) =>
        PlayerAttackSpeedRules.Duration(Mathf.Max(0f, delay), attackSpeedRate);
}

/// <summary>어쌔신 파생 공격 카드 {dmg}(명세 §8) — 원래 공격 1회 기준 피해(Dref) × 파생 비율. Shift 계산식 항은 계수·고정값 × 비율.</summary>
public static class AssassinDerivedTooltipRules
{
    public static SkillTooltipDamage From(int sourceDref, float coefficient, int flatBonus, float ratio)
    {
        ratio = Mathf.Max(0f, ratio);
        int damage = TraitDerivedAttackRules.Damage(sourceDref, ratio);
        float scaledCoefficient = coefficient * ratio;
        int scaledFlat = Mathf.RoundToInt(flatBonus * ratio);
        return new SkillTooltipDamage(damage, damage, scaledCoefficient, scaledCoefficient, scaledFlat, scaledFlat);
    }

    /// <summary>정의의 첫 파생 공격 효과 비율. 없으면 0.</summary>
    public static float RatioOf(TraitDefinition trait)
    {
        if (trait != null && trait.Effects != null)
        {
            foreach (TraitEffect effect in trait.Effects)
            {
                if (effect is TraitDerivedAttackEffect derived)
                    return derived.DamageRatio;
            }
        }

        return 0f;
    }
}

/// <summary>
/// [서버] 어쌔신 파생 공격 예약 줄(할 일 128 3/5) — 지연 뒤 한 번 판정할 것들을 시각 순서로 든다. 순수 C#(EditMode 대상).
/// 사망·씬 종료·비활성에서 <see cref="Clear"/> — 이전 시전의 예약이 남지 않는다.
/// </summary>
public sealed class AssassinDelayedStrikeQueue<T>
{
    private readonly List<(double due, T payload)> entries = new List<(double due, T payload)>();

    public int Count => entries.Count;

    /// <summary><paramref name="now"/> + <paramref name="delay"/>(음수 = 0) 에 낼 것을 넣는다. 같은 시각이면 넣은 순서.</summary>
    public void Schedule(double now, float delay, T payload)
    {
        double due = now + System.Math.Max(0f, delay);
        int index = entries.Count;
        while (index > 0 && entries[index - 1].due > due)
            index--;
        entries.Insert(index, (due, payload));
    }

    /// <summary>시각이 된 가장 이른 것 하나를 꺼낸다. 없으면 false.</summary>
    public bool TryPopDue(double now, out T payload)
    {
        if (entries.Count > 0 && entries[0].due <= now)
        {
            payload = entries[0].payload;
            entries.RemoveAt(0);
            return true;
        }

        payload = default;
        return false;
    }

    public void Clear() => entries.Clear();
}
