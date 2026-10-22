using System.Collections.Generic;
using UnityEngine;

/// <summary>훈련 리셋 범위 — 단계마다 고른다(명세 v0.2 §6: TRAIN_01/02/03 시작 = 전부, B→C 초기화 = 분노 재지급 없음, 실전 초기화 = T6).</summary>
public readonly struct TrainingResetOptions
{
    public TrainingResetOptions(bool fullHealth, bool resetCooldowns, bool refillDash, bool grantAssassinTransformRage)
    {
        FullHealth = fullHealth;
        ResetCooldowns = resetCooldowns;
        RefillDash = refillDash;
        GrantAssassinTransformRage = grantAssassinTransformRage;
    }

    /// <summary>체력 최대(살아 있는 사람만 — 쓰러짐·사망은 부활 경로 몫).</summary>
    public bool FullHealth { get; }

    /// <summary>Q·E·R·우클릭 쿨타임 0 — 등록 스킬 전부(일반·변신 대체 슬롯 포함).</summary>
    public bool ResetCooldowns { get; }

    /// <summary>대시 충전 가득(서버 장부 + 오너 예측은 진행기 복제 신호로).</summary>
    public bool RefillDash { get; }

    /// <summary>어쌔신 — 분노를 R(변신) 최소량까지 채운다(훈련 보정, 값 = 데이터 <c>AssassinStateData.minTransformRage</c>).</summary>
    public bool GrantAssassinTransformRage { get; }

    /// <summary>훈련 단계 시작(TRAIN_01) — 체력·쿨타임·대시 + 어쌔신 R 최소 분노.</summary>
    public static TrainingResetOptions TrainingStart => new TrainingResetOptions(true, true, true, true);
}

/// <summary>
/// 훈련 리셋 순수 규칙 — Unity·NGO 비의존(EditMode 테스트 대상). 실행은 <see cref="TrainingPlayerReset"/>.
/// </summary>
public static class TrainingResetRules
{
    /// <summary>쿨타임 초기화 대상 슬롯 — 훈련 리셋은 Q·E·R·우클릭 전부(복도1 범위와 같다, <see cref="TutorialCooldownResetScope.Corridor1Includes"/>).</summary>
    public static bool IncludesSlot(PlayerSkillSlot slot) =>
        slot == PlayerSkillSlot.Main || slot == PlayerSkillSlot.Sub
        || slot == PlayerSkillSlot.Ultimate || slot == PlayerSkillSlot.Interrupt;

    /// <summary>체력 최대까지 채울 양. 쓰러짐·사망(0 이하)은 0 — 회복으로 되살리지 않는다.</summary>
    public static int HealAmount(int currentHealth, int maxHealth) =>
        currentHealth <= 0 ? 0 : Mathf.Max(0, maxHealth - currentHealth);

    /// <summary>
    /// 어쌔신 분노 지급량 판정 — 일반 상태이고 지금 분노가 최소 변신량보다 적으면 그 값까지(true). 이미 넘거나 변신 중이면 false(깎지 않는다).
    /// </summary>
    public static bool TryGetRageGrant(float currentRage, float minTransformRage, bool transformed, out float target)
    {
        target = currentRage;
        if (transformed || minTransformRage <= 0f || currentRage >= minTransformRage)
            return false;

        target = minTransformRage;
        return true;
    }
}

/// <summary>
/// [서버] 훈련 리셋 예약 — 리셋 순간 '끝날 때 쿨이 걸리는 행동'(실행 중 스킬·어쌔신 변신·E 강화 대기, <see cref="TutorialCooldownReset.IsBusy"/>) 중이던 사람은
/// 그 행동이 끝나는 순간 쿨타임을 한 번 더 지운다(복도1 예약과 같은 규칙, <see cref="TutorialCooldownResetModel"/>). 리셋마다 다시 걸 수 있다(단계별 재사용).
/// </summary>
public sealed class TrainingResetReservations
{
    readonly HashSet<ulong> _reserved = new HashSet<ulong>();

    public int Count => _reserved.Count;

    public bool IsReserved(ulong clientId) => _reserved.Contains(clientId);

    /// <summary>리셋 순간 진행 중 행동이 있었으면 예약.</summary>
    public void OnReset(ulong clientId, bool busy)
    {
        if (busy)
            _reserved.Add(clientId);
        else
            _reserved.Remove(clientId);
    }

    /// <summary>예약이 있고 행동이 끝났으면 true(한 번 더 초기화) — 예약은 그때 지운다.</summary>
    public bool Tick(ulong clientId, bool busy)
    {
        if (busy || !_reserved.Contains(clientId))
            return false;

        _reserved.Remove(clientId);
        return true;
    }

    public void Forget(ulong clientId) => _reserved.Remove(clientId);

    public void Clear() => _reserved.Clear();

    /// <summary>예약된 사람 목록 복사(순회 중 Tick 으로 지우려고).</summary>
    public void CopyTo(List<ulong> result)
    {
        result.Clear();
        result.AddRange(_reserved);
    }
}

/// <summary>
/// [서버] 훈련 리셋 실행(공용 — TRAIN_01 시작, 이후 TRAIN_02·03 시작·B→C 초기화·출격 실전 초기화에서 재사용).
/// 쿨타임은 <see cref="TutorialCooldownReset.ResetSkillsServer"/>(쿨 장부 공개 API — 존1·복도1 동작 그대로), 대시는
/// <see cref="PlayerDashController.ServerResetChargeToFull"/>(오너 예측 짝은 호출자가 복제 신호로 — <see cref="LobbyTrainingDirector"/>).
/// 자원을 강제로 비우거나 변신을 끝내지 않는다(실전 초기화는 T6).
/// </summary>
public static class TrainingPlayerReset
{
    /// <summary>[서버] 한 사람 리셋. 그 순간 진행 중 행동이 있었는가(예약 판단용)를 돌려준다.</summary>
    public static bool ServerApply(Player player, TrainingResetOptions options, out string summary)
    {
        summary = string.Empty;
        if (player == null || !player.IsServer)
            return false;

        var busy = TutorialCooldownReset.IsBusy(player);
        var healed = 0;
        var skills = 0;
        var rage = string.Empty;

        if (options.FullHealth)
        {
            healed = TrainingResetRules.HealAmount(player.CurrentHealth, player.MaxHp);
            if (healed > 0)
                player.HealHp(healed, false);   // 훈련 보정 — 결과 통계 회복에 넣지 않는다
        }

        if (options.ResetCooldowns)
            skills = TutorialCooldownReset.ResetSkillsServer(player, TrainingResetRules.IncludesSlot);

        if (options.RefillDash)
            player.GetComponent<PlayerDashController>()?.ServerResetChargeToFull();

        if (options.GrantAssassinTransformRage)
        {
            var assassin = player.GetComponent<AssassinState>();
            if (assassin != null
                && TrainingResetRules.TryGetRageGrant(assassin.Rage, assassin.MinTransformRage, assassin.IsTransformed, out var target)
                && assassin.ServerRaiseRageTo(target))
                rage = $", 분노 → {target:0.#}";
        }

        summary = $"체력 +{healed}, 스킬 쿨 {skills}개" + (options.RefillDash ? ", 대시 가득" : "") + rage + (busy ? ", 진행 중 행동 — 끝나면 쿨 한 번 더(예약)" : "");
        return busy;
    }

    /// <summary>[서버] 예약 실행 — 진행 중 행동이 끝난 사람의 쿨타임만 다시 지운다. 지운 스킬 수.</summary>
    public static int ServerApplyReservedCooldowns(Player player) =>
        player != null && player.IsServer ? TutorialCooldownReset.ResetSkillsServer(player, TrainingResetRules.IncludesSlot) : 0;
}
