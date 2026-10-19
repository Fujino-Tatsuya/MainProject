using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 스폰 프리팹 결정(할 일 48 C3) — 선택 저장소 값 → 로스터 가용 칸 프리팹, 아니면 기본 프리팹.
/// NGO 스폰 자체는 다루지 않는다.
/// </summary>
public sealed class PlayerSpawnServiceTests
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    GameObject _default;
    GameObject _paladin;
    GameObject _gunner;
    CharacterRoster _roster;

    [SetUp]
    public void SetUp()
    {
        ServerCharacterSelectionStore.Clear();
        _default = new GameObject("Default");
        _paladin = new GameObject("Paladin");
        _gunner = new GameObject("Gunner");
        _roster = ScriptableObject.CreateInstance<CharacterRoster>();
        SetEntries(_roster,
            Entry(_paladin, available: true),
            Entry(_gunner, available: false),
            Entry(null, available: true));
    }

    [TearDown]
    public void TearDown()
    {
        ServerCharacterSelectionStore.Clear();
        Object.DestroyImmediate(_default);
        Object.DestroyImmediate(_paladin);
        Object.DestroyImmediate(_gunner);
        Object.DestroyImmediate(_roster);
    }

    static CharacterRoster.Entry Entry(GameObject prefab, bool available)
    {
        var entry = new CharacterRoster.Entry();
        typeof(CharacterRoster.Entry).GetField("playerPrefab", Private).SetValue(entry, prefab);
        typeof(CharacterRoster.Entry).GetField("available", Private).SetValue(entry, available);
        return entry;
    }

    static void SetEntries(CharacterRoster roster, params CharacterRoster.Entry[] entries)
    {
        typeof(CharacterRoster).GetField("entries", Private).SetValue(roster, entries);
    }

    [Test]
    public void 선택_없음이면_기본_프리팹()
    {
        Assert.AreSame(_default, PlayerSpawnService.ResolvePlayerPrefab(1UL, _roster, _default));
    }

    [Test]
    public void 선택한_가용_칸이면_로스터_프리팹()
    {
        ServerCharacterSelectionStore.Set(1UL, 0);
        Assert.AreSame(_paladin, PlayerSpawnService.ResolvePlayerPrefab(1UL, _roster, _default));
    }

    [Test]
    public void 다른_클라_선택은_영향_없음()
    {
        ServerCharacterSelectionStore.Set(2UL, 0);
        Assert.AreSame(_default, PlayerSpawnService.ResolvePlayerPrefab(1UL, _roster, _default));
    }

    [Test]
    public void 비활성_칸이면_기본_프리팹()
    {
        ServerCharacterSelectionStore.Set(1UL, 1);
        Assert.AreSame(_default, PlayerSpawnService.ResolvePlayerPrefab(1UL, _roster, _default));
    }

    [Test]
    public void 프리팹_빈_칸이면_기본_프리팹()
    {
        ServerCharacterSelectionStore.Set(1UL, 2);
        Assert.AreSame(_default, PlayerSpawnService.ResolvePlayerPrefab(1UL, _roster, _default));
    }

    [Test]
    public void 범위_밖_id면_기본_프리팹()
    {
        ServerCharacterSelectionStore.Set(1UL, 3);
        Assert.AreSame(_default, PlayerSpawnService.ResolvePlayerPrefab(1UL, _roster, _default));
        ServerCharacterSelectionStore.Set(1UL, -1);
        Assert.AreSame(_default, PlayerSpawnService.ResolvePlayerPrefab(1UL, _roster, _default));
    }

    [Test]
    public void 로스터_없으면_기본_프리팹()
    {
        ServerCharacterSelectionStore.Set(1UL, 0);
        Assert.AreSame(_default, PlayerSpawnService.ResolvePlayerPrefab(1UL, null, _default));
    }

    [Test]
    public void 기본_프리팹도_없으면_null()
    {
        Assert.IsNull(PlayerSpawnService.ResolvePlayerPrefab(1UL, _roster, null));
    }

    [Test]
    public void 캐릭터_id_직접_지정은_저장소를_보지_않는다()
    {
        ServerCharacterSelectionStore.Set(1UL, 1);
        Assert.AreSame(_paladin, PlayerSpawnService.ResolvePlayerPrefab(0, _roster, _default));
        Assert.AreSame(_default, PlayerSpawnService.ResolvePlayerPrefab(1, _roster, _default));
    }
}
