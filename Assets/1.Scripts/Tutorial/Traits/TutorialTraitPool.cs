using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튜토리얼 특성 후보 풀(할 일 15). 배열 인덱스가 특성의 런타임 id다 — 순서를 바꾸면 진행 중 세션의 동기화가 어긋나므로
/// 세션 도중 에셋을 고치지 말 것. 기획서가 오면 정의 에셋을 추가·교체하고 여기에 넣는다.
/// </summary>
[CreateAssetMenu(fileName = "TutorialTraitPool", menuName = "Tutorial/Trait Pool")]
public sealed class TutorialTraitPool : ScriptableObject
{
    [SerializeField] private TutorialTraitDefinition[] traits = Array.Empty<TutorialTraitDefinition>();
    [Tooltip("한 번에 제시하는 장 수. 남은 후보가 이보다 적으면 남은 만큼만 제시한다.")]
    [Min(1)]
    [SerializeField] private int offerCount = 3;

    public int Count => traits != null ? traits.Length : 0;
    public int OfferCount => Mathf.Max(1, offerCount);
    public IReadOnlyList<TutorialTraitDefinition> Traits => traits ?? Array.Empty<TutorialTraitDefinition>();

    /// <summary>인덱스의 특성. 범위 밖·빈 칸이면 null.</summary>
    public TutorialTraitDefinition Get(int index)
    {
        return traits != null && index >= 0 && index < traits.Length ? traits[index] : null;
    }
}
