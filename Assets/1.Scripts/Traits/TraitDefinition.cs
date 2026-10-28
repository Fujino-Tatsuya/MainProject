using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 복도에서 고르는 특성 1장(할 일 15 → 125).
/// 영구 ID = 명세 문자열 ID(<c>PAL_1_Q_DAMAGE</c> 등, <see cref="TraitCatalog"/>). 네트워크·재합류 보관은 그 고정 해시(<see cref="IdHash"/>)로 한다.
/// 효과 = 재사용 효과 클래스(<see cref="TraitEffect"/>) 조합 + 수치(할 일 125 3/5 — 옛 효과 enum 폐지).
///   수치형 기여(<see cref="ITraitModifierContributor"/>)는 각 피어가 같은 SO 에서 읽는다 — <see cref="PlayerTraitModifiers"/>.
///   이벤트형은 서버 <see cref="TraitDirector"/> 가 획득 때 플레이어 허브(<see cref="PlayerTraitEffects"/>)에 걸고 효과 장부로 되돌린다.
/// 모드 변경 특성은 효과가 없어도 된다 — 스킬이 <see cref="PlayerTraitModifiers.HasTrait"/> 로 묻는다(<see cref="TraitModeIds"/>).
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
    [Tooltip("효과 클래스 목록(할 일 125 3/5). 인스펙터 아래 '효과 추가' 로 넣는다.")]
    [SerializeReference] private List<TraitEffect> effects = new List<TraitEffect>();

    public string Id => id;
    /// <summary>영구 ID 의 고정 해시(<see cref="TraitIdHash.Of"/>) — 네트워크·보관 키.</summary>
    public int IdHash => TraitIdHash.Of(id);
    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;

    /// <summary>효과 목록(직렬화 순서 = 효과 칸 번호 — 장부의 '획득 1회' 키).</summary>
    public IReadOnlyList<TraitEffect> Effects => effects;

    /// <summary>수치 기여 — 각 효과의 기여를 모은다(빈 칸은 건너뜀). 이벤트형 효과는 보통 기여가 없다.</summary>
    public void GetModifiers(List<TraitModifier> result)
    {
        if (effects == null)
            return;

        for (int i = 0; i < effects.Count; i++)
            effects[i]?.GetModifiers(result);
    }

    /// <summary>그 슬롯 쿨에 관여하는 효과가 있는가(전투 HUD 스킬 강화 칸).</summary>
    public bool AffectsCooldownOf(PlayerSkillSlot slot)
    {
        if (effects == null)
            return false;

        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i] != null && effects[i].AffectsCooldownOf(slot))
                return true;
        }

        return false;
    }

    /// <summary>데이터 검증 — 빈 효과 칸·각 효과의 수치 검사(명세 §9). 오류 문장을 덧붙인다.</summary>
    public void Validate(List<string> errors)
    {
        if (effects == null)
            return;

        for (int i = 0; i < effects.Count; i++)
        {
            if (effects[i] == null)
            {
                errors.Add($"'{id}' 효과 칸 {i} 가 비었다");
                continue;
            }

            int before = errors.Count;
            effects[i].Validate(errors);
            for (int e = before; e < errors.Count; e++)
                errors[e] = $"'{id}' 효과 칸 {i}: {errors[e]}";
        }
    }

    private void OnValidate()
    {
        var errors = new List<string>();
        Validate(errors);
        for (int i = 0; i < errors.Count; i++)
            Debug.LogWarning($"[Trait] {errors[i]}", this);
    }
}
