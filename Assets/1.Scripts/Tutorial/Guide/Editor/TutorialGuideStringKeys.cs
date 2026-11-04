using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 튜토리얼·23호 레이드 자막 문구(<see cref="TutorialGuideTextTable"/> 행)를 Export 때 Strings 시트에 모은다(할 일 86 T6a).
/// key = <c>Tutorial.Guide.&lt;id&gt;</c>(<see cref="TutorialGuideTextSource.KeyOf"/>), ko = SO 문구, <c>#설명</c> = 쓰는 곳, en 은 빈 칸.
/// 문구가 빈 행(아직 안 채운 자리)은 내보내지 않는다 — ko 빈 칸은 검증 오류다.
/// 로비 튜토리얼 UI 가 등록하는 키(<see cref="LobbyTutorialUiStrings.All"/> — <c>Tutorial.Guide.LOBBY_TITLE_*</c> 등)는 그쪽이 원본이라 건너뛴다(할 일 214 —
/// 같은 키가 두 번 등록되면 Export 가 등록 순서대로 처음 것만 남겨 <c>#설명</c> 이 "TutorialGuideTexts · 튜토리얼 안내" 로 바뀌었다). SO 행은 그대로 둔다(폴백 문구).
/// </summary>
[InitializeOnLoad]
internal static class TutorialGuideStringKeys
{
    private const string RaidPrefix = "BOSS_RAID_";

    static TutorialGuideStringKeys()
    {
        StringKeyRegistry.Register(Collect);
    }

    public static IEnumerable<StringKeyInfo> Collect() =>
        Collect(AssetDatabase.LoadAssetAtPath<TutorialGuideTextTable>(TutorialGuideTextTable.AssetPath));

    public static List<StringKeyInfo> Collect(TutorialGuideTextTable table)
    {
        var keys = new List<StringKeyInfo>();
        if (table == null)
            return keys;

        var lobbyOwned = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (UiString s in LobbyTutorialUiStrings.All)
            lobbyOwned.Add(s.Key);

        foreach (TutorialGuideTextRow row in table.Rows)
        {
            if (row == null || string.IsNullOrEmpty(row.Id) || string.IsNullOrWhiteSpace(row.Text))
                continue;
            if (lobbyOwned.Contains(TutorialGuideTextSource.KeyOf(row.Id)))
                continue;

            string where = row.Id.StartsWith(RaidPrefix, System.StringComparison.Ordinal)
                ? "TutorialGuideTexts · 23호 레이드 자막"
                : LobbyRadioIds.IsLobbyRadio(row.Id)
                    ? "TutorialGuideTexts · 로비 무전"
                    : LobbyObjectiveIds.IsLobbyObjective(row.Id)
                        ? "TutorialGuideTexts · 로비 목표"
                        : "TutorialGuideTexts · 튜토리얼 안내";
            keys.Add(new StringKeyInfo(TutorialGuideTextSource.KeyOf(row.Id), where, row.Text));
        }

        return keys;
    }
}
