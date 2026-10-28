using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튜토리얼 특성 효과 종류(3/5 에서 효과 클래스 조합으로 바뀐다). 배율 종류는 <see cref="PlayerTraitModifiers"/> 출처로 들어간다(할 일 125 2/5 — 상태이상 아님).
/// </summary>
public enum TraitEffectKind
{
    /// <summary>공격력 전체%(PlayerStats 출처 <see cref="PlayerTraitModifiers"/>). 값 = 배율(1.2 = +20%, 1 미만 = 페널티).</summary>
    AttackDamageMultiplier,

    /// <summary>이동속도 전체%(PlayerStats 출처 <see cref="PlayerTraitModifiers"/>). 값 = 배율(1 미만 = 페널티).</summary>
    MoveSpeedMultiplier,

    /// <summary>
    /// 공격 적중 시 쿨타임 감소(<see cref="Player.ServerAttackLanded"/> → <see cref="PlayerSkillController.ReduceCooldownServer"/>).
    /// 값 = 감소 초. 보조 값 = 발동 공격 종류·대상 슬롯.
    /// </summary>
    CooldownReductionOnHit,
}

/// <summary>
/// 복도에서 고르는 특성 1장(할 일 15 → 125).
/// 영구 ID = 명세 문자열 ID(<c>PAL_1_Q_DAMAGE</c> 등, <see cref="TraitCatalog"/>). 네트워크·재합류 보관은 그 고정 해시(<see cref="IdHash"/>)로 한다.
/// 수치 기여(<see cref="ITraitModifierContributor"/>)는 각 피어가 같은 SO 에서 읽는다 — <see cref="PlayerTraitModifiers"/>.
/// </summary>
[CreateAssetMenu(fileName = "Trait", menuName = "Tutorial/Trait Definition")]
public sealed class TraitDefinition : ScriptableObject, ITraitModifierContributor
{
    [Tooltip("영구 ID — 명세 문자열 ID(예: PAL_1_Q_DAMAGE). 후보표(TraitCatalog)와 맞아야 제시된다. 동기화는 이 ID 의 고정 해시.")]
    [SerializeField] private string id = "";
    [SerializeField] private string displayName = "";
    [TextArea(2, 4)]
    [SerializeField] private string description = "";
    [SerializeField] private Sprite icon;

    [Header("효과")]
    [SerializeField] private TraitEffectKind effectKind = TraitEffectKind.AttackDamageMultiplier;
    [Tooltip("배율 종류 = 배율(1.2 = +20%, 1 미만 = 페널티), 쿨감 종류 = 감소 초.")]
    [SerializeField] private float value = 1.1f;

    [Header("쿨감 보조 값 (CooldownReductionOnHit 전용)")]
    [Tooltip("이 공격 종류가 적중했을 때만 발동한다. Default = 평타.")]
    [SerializeField] private AttackType triggerAttackType = AttackType.Default;
    [Tooltip("쿨타임을 줄일 슬롯. 슬롯의 현재 스킬(대체 스킬 포함)에 적용된다.")]
    [SerializeField] private PlayerSkillSlot cooldownSlot = PlayerSkillSlot.Main;

    public string Id => id;
    /// <summary>영구 ID 의 고정 해시(<see cref="TraitIdHash.Of"/>) — 네트워크·보관 키.</summary>
    public int IdHash => TraitIdHash.Of(id);
    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;
    public TraitEffectKind EffectKind => effectKind;
    public float Value => value;
    public AttackType TriggerAttackType => triggerAttackType;
    public PlayerSkillSlot CooldownSlot => cooldownSlot;

    /// <summary>
    /// 수치 기여(할 일 125 2/5) — 배율 종류는 전체% 로 넣는다(예전 영구 상태이상 <c>AttackDamageModifier</c>·<c>MoveSpeedModifier</c> 와 같은 단계·같은 값).
    /// 상태이상이 아니므로 1 미만도 그대로 적용되고 보스방 디버프 정리에 지워지지 않는다. 쿨감 종류는 이벤트 효과라 수치 기여가 없다.
    /// </summary>
    public void GetModifiers(List<TraitModifier> result)
    {
        switch (effectKind)
        {
            case TraitEffectKind.AttackDamageMultiplier:
                result.Add(TraitModifier.StatTotal(PlayerStatKind.AttackDamage, value - 1f));
                break;
            case TraitEffectKind.MoveSpeedMultiplier:
                result.Add(TraitModifier.StatTotal(PlayerStatKind.MoveSpeed, value - 1f));
                break;
        }
    }

    private void OnValidate()
    {
        if (effectKind != TraitEffectKind.CooldownReductionOnHit && value < 0f)
            Debug.LogWarning($"[Trait] '{name}' 배율 {value} < 0 — 스탯이 0 으로 떨어진다.", this);
    }
}
