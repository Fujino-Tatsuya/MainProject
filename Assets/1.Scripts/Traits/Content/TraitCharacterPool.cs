using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 캐릭터 하나의 특성 정의 목록(할 일 125 5/5) — 캐릭터 레인이 자기 에셋(<c>9.ScriptableObject/Traits/&lt;캐릭터&gt;/TraitPool_&lt;캐릭터&gt;.asset</c>)에만 정의를 넣어
/// 세 레인이 같은 풀 에셋을 고치지 않게 한다. 공용 <see cref="TraitPool"/> 이 이 에셋들을 정해진 순서로 잇는다(목록 버전 = 이은 순서).
/// </summary>
[CreateAssetMenu(fileName = "TraitPool_Character", menuName = "Traits/Character Trait Pool")]
public sealed class TraitCharacterPool : ScriptableObject
{
    [SerializeField] private TutorialCharacter character;
    [Tooltip("이 캐릭터의 특성 정의(명세 순서 권장 — 순서가 바뀌면 목록 버전이 바뀐다). 정의 타입은 캐릭터 하위 타입이어야 한다.")]
    [SerializeField] private TraitDefinition[] traits = Array.Empty<TraitDefinition>();

    public TutorialCharacter Character => character;
    public IReadOnlyList<TraitDefinition> Traits => traits ?? Array.Empty<TraitDefinition>();

    /// <summary>검증 — 빈 칸·다른 캐릭터 정의·명세 고정표의 다른 캐릭터 ID. 오류 문장을 덧붙인다.</summary>
    public void Validate(List<string> errors)
    {
        IReadOnlyList<TraitDefinition> list = Traits;
        for (int i = 0; i < list.Count; i++)
        {
            TraitDefinition trait = list[i];
            if (trait == null)
            {
                errors.Add($"{name}: {i}번 칸이 비었다.");
                continue;
            }

            if (trait.Character != character)
                errors.Add($"{name}: '{trait.name}' 는 {trait.Character} 정의 타입이다({character} 풀).");

            if (TraitCatalog.TryGetSlot(trait.Id, out TraitCatalog.Slot slot) && slot.Character != character)
                errors.Add($"{name}: '{trait.Id}' 는 명세상 {slot.Character} 특성이다.");
        }
    }

    private void OnValidate() => TraitPool.InvalidateAll();
}
