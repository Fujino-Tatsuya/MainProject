using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// 튜토리얼·23호 레이드 자막 문구(<see cref="TutorialGuideTextTable"/> 행)를 Export 때 Strings 시트에 모은다(할 일 86 T6a).
/// key = <c>Tutorial.Guide.&lt;id&gt;</c>(<see cref="TutorialGuideTextSource.KeyOf"/>), ko = SO 문구, <c>#설명</c> = 쓰는 곳, en 은 빈 칸.
/// 문구가 빈 행(아직 안 채운 자리)은 내보내지 않는다 — ko 빈 칸은 검증 오류다.
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

        foreach (TutorialGuideTextRow row in table.Rows)
        {
            if (row == null || string.IsNullOrEmpty(row.Id) || string.IsNullOrWhiteSpace(row.Text))
                continue;

            string where = row.Id.StartsWith(RaidPrefix, System.StringComparison.Ordinal)
                ? "TutorialGuideTexts · 23호 레이드 자막"
                : "TutorialGuideTexts · 튜토리얼 안내";
            keys.Add(new StringKeyInfo(TutorialGuideTextSource.KeyOf(row.Id), where, row.Text));
        }

        return keys;
    }
}
