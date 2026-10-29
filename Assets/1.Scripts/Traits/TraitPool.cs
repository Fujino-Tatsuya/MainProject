using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 특성 정의 풀(할 일 15 → 125). 런타임 식별은 정의 ID 의 고정 해시(<see cref="TraitIdHash"/>)다 — 배열 순서는 식별에 쓰지 않는다.
/// 후보는 명세 고정표(<see cref="TraitCatalog"/>)가 정하고, 여기는 "어느 ID 의 정의가 들어 있나"만 알린다.
/// 서버·클라 풀이 다르면 접속 때 목록 버전(<see cref="TraitCatalogIndex.Version"/>) 불일치 오류가 난다.
/// 정의 목록 = 공용 칸(<c>traits</c>) 뒤에 캐릭터 풀(<see cref="TraitCharacterPool"/> — 팔라딘·거너·어쌔신 순)을 이은 것(할 일 125 5/5).
/// 캐릭터 레인은 이 에셋이 아니라 자기 캐릭터 풀 에셋에 정의를 넣는다.
/// </summary>
[CreateAssetMenu(fileName = "TraitPool", menuName = "Tutorial/Trait Pool")]
public sealed class TraitPool : ScriptableObject
{
    [Tooltip("공용 칸 — 캐릭터 풀에 속하지 않는 정의(지금은 비움). 새 특성은 캐릭터 풀에 넣는다.")]
    [SerializeField] private TraitDefinition[] traits = Array.Empty<TraitDefinition>();
    [Tooltip("캐릭터별 정의 목록(팔라딘·거너·어쌔신 순). 순서가 목록 버전에 들어간다.")]
    [SerializeField] private TraitCharacterPool[] characterPools = Array.Empty<TraitCharacterPool>();
    [Tooltip("한 번에 제시하는 장 수. 남은 후보가 이보다 적으면 남은 만큼만 제시한다.")]
    [Min(1)]
    [SerializeField] private int offerCount = 3;

    [NonSerialized] private TraitCatalogIndex catalog;
    [NonSerialized] private List<TraitDefinition> all;
    [NonSerialized] private int builtStamp = -1;

    private static int stamp;

    public int Count => All.Count;
    public int OfferCount => Mathf.Max(1, offerCount);
    public IReadOnlyList<TraitDefinition> Traits => All;
    public IReadOnlyList<TraitCharacterPool> CharacterPools => characterPools ?? Array.Empty<TraitCharacterPool>();

    /// <summary>풀 순서 정의 ID 로 만든 조회표(해시·고정 후보·버전·검증). 처음 읽을 때 만들고 에셋이 바뀌면 다시 만든다.</summary>
    public TraitCatalogIndex Catalog
    {
        get
        {
            Refresh();
            return catalog ??= BuildCatalog();
        }
    }

    /// <summary>풀 칸의 특성. 범위 밖·빈 칸이면 null.</summary>
    public TraitDefinition Get(int index)
    {
        IReadOnlyList<TraitDefinition> list = All;
        return index >= 0 && index < list.Count ? list[index] : null;
    }

    /// <summary>ID 해시의 특성. 없거나 검증에 걸린 정의면 null.</summary>
    public TraitDefinition GetByHash(int traitHash)
    {
        return Catalog.TryGetPoolSlot(traitHash, out int slot) ? Get(slot) : null;
    }

    /// <summary>캐릭터 풀 연결 검증 — 빈 칸·같은 캐릭터 둘·각 풀 검증(<see cref="TraitCharacterPool.Validate"/>).</summary>
    public void Validate(List<string> errors)
    {
        var seen = new HashSet<TutorialCharacter>();
        IReadOnlyList<TraitCharacterPool> pools = CharacterPools;
        for (int i = 0; i < pools.Count; i++)
        {
            if (pools[i] == null)
            {
                errors.Add($"{name}: 캐릭터 풀 {i}번 칸이 비었다.");
                continue;
            }

            if (!seen.Add(pools[i].Character))
                errors.Add($"{name}: {pools[i].Character} 캐릭터 풀이 두 번 들어 있다.");
            pools[i].Validate(errors);
        }
    }

    /// <summary>캐릭터 풀 에셋이 바뀌면 모든 풀의 이은 목록·조회표를 다시 만든다.</summary>
    internal static void InvalidateAll() => stamp++;

    private IReadOnlyList<TraitDefinition> All
    {
        get
        {
            Refresh();
            return all ??= BuildAll();
        }
    }

    private void Refresh()
    {
        if (builtStamp == stamp)
            return;

        builtStamp = stamp;
        all = null;
        catalog = null;
    }

    private List<TraitDefinition> BuildAll()
    {
        var result = new List<TraitDefinition>();
        if (traits != null)
            result.AddRange(traits);

        IReadOnlyList<TraitCharacterPool> pools = CharacterPools;
        for (int i = 0; i < pools.Count; i++)
        {
            if (pools[i] != null)
                result.AddRange(pools[i].Traits);
        }

        return result;
    }

    private TraitCatalogIndex BuildCatalog()
    {
        IReadOnlyList<TraitDefinition> list = All;
        var ids = new string[list.Count];
        for (int i = 0; i < ids.Length; i++)
            ids[i] = list[i] != null ? list[i].Id ?? string.Empty : null;

        return new TraitCatalogIndex(ids);
    }

    private void OnEnable() => InvalidateAll();

    private void OnValidate() => InvalidateAll();
}
