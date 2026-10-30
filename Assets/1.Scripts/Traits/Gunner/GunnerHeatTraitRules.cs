using System.Collections.Generic;
using UnityEngine;

/// <summary>거너 2차 과열 모드(할 일 127 3/4). 2차는 하나만 고르므로 동시에 둘 이상이 켜지지 않는다.</summary>
public enum GunnerHeatMode : byte
{
    Normal,
    /// <summary>한계 돌파 — 과열 단계 피해표 교체·기본 공격 과열 증가 ×1.35.</summary>
    Overdrive,
    /// <summary>축열 포격 — Q 첫 발이 과열을 모두 소모하고 소모 비율만큼 Q 피해 보너스.</summary>
    HeatShot,
    /// <summary>정온 운용 — 과열 로직 전체 비활성(증가·단계·과열 상태·냉각), 기본 공격 피해 감소.</summary>
    Cool,
}

/// <summary>
/// 거너 2차 과열 모드 질의(할 일 127 3/4). 모드는 특성 스위치(<see cref="PlayerTraitModifiers.HasTrait"/> · <see cref="TraitModeIds"/>)로 정하고,
/// 수치는 정의 GUN_2_OVERDRIVE·GUN_2_HEAT_SHOT·GUN_2_COOL_MODE 의 공용 스킬 수치 효과(<see cref="TraitSkillValueEffect"/>)가 기여한 ⑩ 스킬 수치를 읽는다.
/// 서버 판정·클라 표시·카드 툴팁이 같은 값을 쓴다 — 판정식(기본 공격·Q·R)과 툴팁 최소~최대가 이 한 곳을 지난다.
/// 런타임은 <see cref="From(PlayerTraitModifiers)"/>, 테스트·카드 합성은 <see cref="From(TraitModifierSet)"/>.
/// </summary>
public readonly struct GunnerHeatTraits
{
    public const int MaxStageIndex = 3;

    private static readonly string[] BasicStageKeys = StageKeys(GunnerTraitRules.BasicStageDamagePrefix);
    private static readonly string[] SkillStageKeys = StageKeys(GunnerTraitRules.SkillStageDamagePrefix);

    private readonly PlayerTraitModifiers player;
    private readonly TraitModifierSet set;

    public readonly GunnerHeatMode Mode;

    private GunnerHeatTraits(GunnerHeatMode mode, PlayerTraitModifiers player, TraitModifierSet set)
    {
        Mode = mode;
        this.player = player;
        this.set = set;
    }

    /// <summary>특성 없음(원래 동작).</summary>
    public static GunnerHeatTraits None => default;

    public static GunnerHeatTraits From(PlayerTraitModifiers modifiers)
    {
        if (modifiers == null)
            return None;

        GunnerHeatMode mode = modifiers.HasTrait(TraitModeIds.GunnerCoolMode) ? GunnerHeatMode.Cool
            : modifiers.HasTrait(TraitModeIds.GunnerOverdrive) ? GunnerHeatMode.Overdrive
            : modifiers.HasTrait(TraitModeIds.GunnerHeatShot) ? GunnerHeatMode.HeatShot
            : GunnerHeatMode.Normal;
        return new GunnerHeatTraits(mode, modifiers, null);
    }

    public static GunnerHeatTraits From(TraitModifierSet modifiers)
    {
        if (modifiers == null)
            return None;

        IReadOnlyList<int> hashes = modifiers.TraitHashes;
        GunnerHeatMode mode = Has(hashes, TraitModeIds.GunnerCoolMode) ? GunnerHeatMode.Cool
            : Has(hashes, TraitModeIds.GunnerOverdrive) ? GunnerHeatMode.Overdrive
            : Has(hashes, TraitModeIds.GunnerHeatShot) ? GunnerHeatMode.HeatShot
            : GunnerHeatMode.Normal;
        return new GunnerHeatTraits(mode, null, modifiers);
    }

    /// <summary>과열 로직이 켜져 있는가. 정온 운용이면 false — 증가·단계·과열 상태·냉각·과열 UI 가 모두 꺼진다.</summary>
    public bool HeatEnabled => Mode != GunnerHeatMode.Cool;

    /// <summary>Q 첫 발이 과열을 모두 소모하는가(축열 포격).</summary>
    public bool SpendsHeatOnQ => Mode == GunnerHeatMode.HeatShot;

    /// <summary>도달할 수 있는 최고 단계 — 정온 운용은 0(툴팁 상한에 불가능한 단계를 넣지 않는다).</summary>
    public int MaxStage => HeatEnabled ? MaxStageIndex : 0;

    /// <summary>판정·표시에 쓰는 단계 — 0~<see cref="MaxStage"/> 로 자른다.</summary>
    public int EffectiveStage(int stage) => Mathf.Clamp(stage, 0, MaxStage);

    /// <summary>기본 공격 1발 실제 과열 증가량 — 한계 돌파면 데이터 값 × (1 + 증가율). 최대치 제한은 <see cref="GunnerHeatModel.AddShot"/>.</summary>
    public float HeatPerShot(float dataHeatPerShot) =>
        Mode == GunnerHeatMode.Overdrive ? Resolve(GunnerTraitRules.BasicHeatPerShot, dataHeatPerShot) : dataHeatPerShot;

    /// <summary>
    /// 기본 공격 피해 상태 배율 = 과열 단계 배율 × 정온 감소. 한계 돌파는 단계 배율을 교체한다(데이터 표에 또 곱하지 않음),
    /// 정온 운용은 0단계 값 × (1 − 감소율)(출력 강화 ×1.2 는 Dref 에 이미 있어 1.02).
    /// </summary>
    public float BasicShotScale(int stage, GunnerHeatData data)
    {
        int s = EffectiveStage(stage);
        float dataScale = data != null ? data.StageDamageMultiplier(s) : 1f;
        float stageScale = Mode == GunnerHeatMode.Overdrive ? Resolve(BasicStageKeys[s], dataScale) : dataScale;
        return stageScale * BasicModeScale;
    }

    /// <summary>정온 운용 기본 공격 피해 배율(1 − 감소율). 그 밖엔 1.</summary>
    public float BasicModeScale =>
        Mode == GunnerHeatMode.Cool ? Mathf.Max(0f, 1f - Resolve(GunnerTraitRules.BasicCoolDamagePenalty, 0f)) : 1f;

    /// <summary>
    /// Q·R 피해 단계 배율. <paramref name="dataScaleAtEffectiveStage"/> = 그 스킬 데이터 표의 <see cref="EffectiveStage"/> 칸 값.
    /// 한계 돌파면 교체 표, 아니면 데이터 값 그대로. Q 보호막 단계 배율은 여기를 지나지 않는다(원래 표 유지).
    /// </summary>
    public float SkillStageScale(int stage, float dataScaleAtEffectiveStage) =>
        Mode == GunnerHeatMode.Overdrive ? Resolve(SkillStageKeys[EffectiveStage(stage)], dataScaleAtEffectiveStage) : dataScaleAtEffectiveStage;

    /// <summary>축열 포격 Q 피해 배율 = 1 + 최대 보너스 × 소모 비율(0~1). 다른 모드는 1.</summary>
    public float HeatShotScale(float spentRatio) =>
        SpendsHeatOnQ ? 1f + Mathf.Max(0f, Resolve(GunnerTraitRules.QHeatSpentDamageBonus, 0f)) * Mathf.Clamp01(spentRatio) : 1f;

    /// <summary>축열 포격 최대 배율(소모 비율 1) — 툴팁 상한.</summary>
    public float MaxHeatShotScale => HeatShotScale(1f);

    private float Resolve(string key, float baseValue) =>
        player != null ? player.ResolveSkillValue(key, baseValue)
        : set != null ? set.ResolveSkillValue(key, baseValue)
        : baseValue;

    private static bool Has(IReadOnlyList<int> hashes, string id)
    {
        int hash = TraitIdHash.Of(id);
        for (int i = 0; hashes != null && i < hashes.Count; i++)
        {
            if (hashes[i] == hash)
                return true;
        }

        return false;
    }

    private static string[] StageKeys(string prefix)
    {
        var keys = new string[MaxStageIndex + 1];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = prefix + i;
        return keys;
    }
}

/// <summary>
/// 거너 Q 시전 스냅샷(할 일 127 3/4, 명세 §5) — 시전 시작 순간 과열 단계 + 첫 발 직전 실제 열 소모 비율(축열 포격).
/// 원래 발 전체와 잔류 포격이 같은 값을 쓴다. 집속 연사(4/4)는 첫 발에서만 <see cref="CommitFirstShot"/> 하고 후속 발은 <see cref="ShotDamage"/> 만 부른다.
/// 충전 취소·발사 실패 = 커밋 없음 → 열 소모 없음. 빗나간 발사도 커밋(소모)한다 — 소모는 적중과 무관하다.
/// </summary>
public sealed class GunnerQCastSnapshot
{
    public int StoredStage { get; private set; }
    public bool FirstShotCommitted { get; private set; }
    /// <summary>첫 발 직전 소모한 열 비율(0~1). 축열 포격이 아니면 0.</summary>
    public float HeatSpentRatio { get; private set; }
    /// <summary>축열 포격 피해 배율(1 + 보너스 × 소모 비율). 커밋 전·다른 모드는 1.</summary>
    public float HeatShotScale { get; private set; } = 1f;

    /// <summary>새 시전 — 시작 순간 단계 저장, 소모 정보 비움.</summary>
    public void Begin(int storedStage)
    {
        StoredStage = storedStage;
        FirstShotCommitted = false;
        HeatSpentRatio = 0f;
        HeatShotScale = 1f;
    }

    /// <summary>
    /// 첫 발 커밋 — <paramref name="spentRatio"/> 는 호출자가 같은 서버 호출 안에서 열을 0 으로 만들며 얻은 실제 소모 비율
    /// (<see cref="GunnerHeat.ServerSpendAll"/>). 이미 커밋했으면 아무것도 하지 않고 false.
    /// </summary>
    public bool CommitFirstShot(in GunnerHeatTraits traits, float spentRatio)
    {
        if (FirstShotCommitted)
            return false;

        FirstShotCommitted = true;
        HeatSpentRatio = traits.SpendsHeatOnQ ? Mathf.Clamp01(spentRatio) : 0f;
        HeatShotScale = traits.HeatShotScale(HeatSpentRatio);
        return true;
    }

    /// <summary>원래 발 단일 대상 피해 = round(Dref × 충전 배율 × 저장 단계 배율 × 축열 배율). 배율 중간에 자르지 않는다(명세 §5).</summary>
    public int ShotDamage(int dref, float chargeScale, float stageScale) =>
        Mathf.Max(0, Mathf.RoundToInt(dref * chargeScale * stageScale * HeatShotScale));
}
