using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 특성 정의 풀(할 일 15 → 125). 런타임 식별은 정의 ID 의 고정 해시(<see cref="TraitIdHash"/>)다 — 배열 순서는 식별에 쓰지 않는다.
/// 후보는 명세 고정표(<see cref="TraitCatalog"/>)가 정하고, 여기는 "어느 ID 의 정의가 들어 있나"만 알린다.
/// 서버·클라 풀이 다르면 접속 때 목록 버전(<see cref="TraitCatalogIndex.Version"/>) 불일치 오류가 난다.
/// </summary>
[CreateAssetMenu(fileName = "TraitPool", menuName = "Tutorial/Trait Pool")]
public sealed class TraitPool : ScriptableObject
{
    [SerializeField] private TraitDefinition[] traits = Array.Empty<TraitDefinition>();
    [Tooltip("한 번에 제시하는 장 수. 남은 후보가 이보다 적으면 남은 만큼만 제시한다.")]
    [Min(1)]
    [SerializeField] private int offerCount = 3;

    [NonSerialized] private TraitCatalogIndex catalog;

    public int Count => traits != null ? traits.Length : 0;
    public int OfferCount => Mathf.Max(1, offerCount);
    public IReadOnlyList<TraitDefinition> Traits => traits ?? Array.Empty<TraitDefinition>();

    /// <summary>풀 순서 정의 ID 로 만든 조회표(해시·고정 후보·버전·검증). 처음 읽을 때 만들고 에셋이 바뀌면 다시 만든다.</summary>
    public TraitCatalogIndex Catalog => catalog ??= BuildCatalog();

    /// <summary>풀 칸의 특성. 범위 밖·빈 칸이면 null.</summary>
    public TraitDefinition Get(int index)
    {
        return traits != null && index >= 0 && index < traits.Length ? traits[index] : null;
    }

    /// <summary>ID 해시의 특성. 없거나 검증에 걸린 정의면 null.</summary>
    public TraitDefinition GetByHash(int traitHash)
    {
        return Catalog.TryGetPoolSlot(traitHash, out int slot) ? Get(slot) : null;
    }

    private TraitCatalogIndex BuildCatalog()
    {
        var ids = new string[Count];
        for (int i = 0; i < ids.Length; i++)
            ids[i] = traits[i] != null ? traits[i].Id ?? string.Empty : null;

        return new TraitCatalogIndex(ids);
    }

    private void OnEnable() => catalog = null;

    private void OnValidate() => catalog = null;
}
