using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 로컬 플레이어에서 문구 변형용 캐릭터와 {Q} {E} {P} 스킬 이름을 읽는다.
/// 캐릭터는 스폰된 Player 의 NetworkObject.PrefabIdHash 를 스폰 로스터(NetworkManager 의 NetworkLoadingFlowController)
/// 항목 PlayerPrefab 해시와 역조회해 얻는다 — 해시는 모든 피어에서 같아 복제가 필요 없다.
/// 역조회가 실패하면 경고 1회 후 캐릭터 고유 컴포넌트로 폴백한다.
/// </summary>
public static class TutorialLocalPlayerInfo
{
    private static CharacterRoster s_rowsRoster;
    private static TutorialRosterRow[] s_rows;
    private static readonly HashSet<string> s_warned = new HashSet<string>();

    public static TutorialCharacter ResolveCharacter(Player player)
    {
        if (player == null)
            return TutorialCharacter.None;

        uint hash = player.TryGetComponent(out NetworkObject networkObject) ? networkObject.PrefabIdHash : 0;
        TutorialCharacterLookupStatus status =
            TutorialCharacterLookup.Resolve(hash, GetRosterRows(), out int rosterId, out TutorialCharacter character);
        if (status == TutorialCharacterLookupStatus.Resolved)
            return character;

        TutorialCharacter fallback = ResolveByComponent(player);
        WarnOnce(status, hash, rosterId, fallback);
        return TutorialCharacterLookup.Choose(status, character, fallback);
    }

    private static IReadOnlyList<TutorialRosterRow> GetRosterRows()
    {
        NetworkManager manager = NetworkManager.Singleton;
        NetworkLoadingFlowController flow = manager != null ? manager.GetComponent<NetworkLoadingFlowController>() : null;
        CharacterRoster roster = flow != null ? flow.CharacterRoster : null;
        if (roster == null)
            return null;

        // 로스터 프리팹 해시는 에셋 값이라 바뀌지 않는다 — 로스터가 같으면 다시 만들지 않는다.
        if (roster != s_rowsRoster || s_rows == null)
        {
            IReadOnlyList<CharacterRoster.Entry> entries = roster.Entries;
            s_rows = new TutorialRosterRow[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                CharacterRoster.Entry entry = entries[i];
                NetworkObject prefabObject = entry != null && entry.PlayerPrefab != null
                    ? entry.PlayerPrefab.GetComponent<NetworkObject>()
                    : null;
                s_rows[i] = new TutorialRosterRow(
                    prefabObject != null ? prefabObject.PrefabIdHash : 0,
                    entry != null ? entry.TutorialCharacter : TutorialCharacter.None);
            }
            s_rowsRoster = roster;
        }
        return s_rows;
    }

    private static void WarnOnce(TutorialCharacterLookupStatus status, uint hash, int rosterId, TutorialCharacter fallback)
    {
        if (!s_warned.Add($"{status}:{hash}"))
            return;

        Debug.LogWarning(
            $"[TutorialGuide] 캐릭터 로스터 역조회 실패({status}, PrefabIdHash={hash}, rosterId={rosterId}) — " +
            $"컴포넌트 판별로 폴백한다 → {fallback}.");
    }

    // 폴백: 캐릭터 고유 컴포넌트(Player_Gunner = GunnerHeat, Player_Assassin = AssassinState,
    // Player_Paladin = FirstMeleePassive — 2026-10-11 프리팹 확인).
    private static TutorialCharacter ResolveByComponent(Player player)
    {
        if (player.GetComponentInChildren<GunnerHeat>(true) != null)
            return TutorialCharacter.Gunner;
        if (player.GetComponentInChildren<AssassinState>(true) != null)
            return TutorialCharacter.Assassin;
        if (player.GetComponentInChildren<FirstMeleePassive>(true) != null)
            return TutorialCharacter.Paladin;

        return TutorialCharacter.None;
    }

    /// <summary>
    /// 슬롯 툴팁 displayName 을 {Q} {E} {P} 로 넣는다. 이름이 비면 키를 넣지 않는다(자리표시자가 그대로 남는다).
    /// 경로는 SkillCooldownHUD(Q/E = 슬롯 스킬 Data)·PassiveHUD(P = IPlayerPassive → IPassiveTooltipProvider)와 같다.
    /// </summary>
    public static void AddSkillNames(Player player, IDictionary<string, string> args)
    {
        if (player == null || args == null)
            return;

        PlayerSkillController skills = player.GetComponent<PlayerSkillController>();
        if (skills != null)
        {
            Add(args, "Q", skills.GetSkill(PlayerSkillSlot.Main)?.Data);
            Add(args, "E", skills.GetSkill(PlayerSkillSlot.Sub)?.Data);
        }

        ISkillTooltipSource passive = player.GetComponent<IPlayerPassive>() as ISkillTooltipSource;
        if (passive == null)
            passive = player.GetComponentInChildren<IPassiveTooltipProvider>(true)?.PassiveTooltip;
        Add(args, "P", passive);
    }

    private static void Add(IDictionary<string, string> args, string key, ISkillTooltipSource source)
    {
        string name = source != null ? source.Tooltip.DisplayName : null;
        if (!string.IsNullOrEmpty(name))
            args[key] = name;
    }
}
