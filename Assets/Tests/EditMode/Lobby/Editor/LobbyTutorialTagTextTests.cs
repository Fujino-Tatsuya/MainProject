using NUnit.Framework;
using UnityEditor;

/// <summary>문구 안 {tag:ID} 표기 — 할 일 157 6b(SO 기본 문구 3행). 색 처리는 158 몫(<see cref="LobbyTutorialUiRules.ReplaceTags"/>, 과도기 어댑터는 158 병합 때 삭제).</summary>
public sealed class LobbyTutorialTagTextTests
{
    static TutorialGuideTextTable LoadTable()
    {
        var table = AssetDatabase.LoadAssetAtPath<TutorialGuideTextTable>(TutorialGuideTextTable.AssetPath);
        Assert.That(table, Is.Not.Null, TutorialGuideTextTable.AssetPath);
        return table;
    }

    static string Text(string id)
    {
        Assert.That(LoadTable().TryGetRow(id, out var row), Is.True, id);
        return row.Text;
    }

    [Test]
    public void SO_기본_문구_3행은_tag_표기를_쓴다()
    {
        Assert.That(Text("LOBBY_OBJ_VULN_CC"), Is.EqualTo("취약 상태의 상대에게 {tag:Knockback} 또는 {tag:Stun} 속성이 있는 스킬을 사용하세요"));
        Assert.That(Text("LOBBY_OBJ_WALL"), Is.EqualTo("{tag:Knockback} 속성이 있는 스킬로 상대를 표시된 벽에 충돌시키세요"));
        Assert.That(Text("LOBBY_BODY_SUPPRESS"),
            Is.EqualTo("간파 또는 벽과 충돌시켜 상대의 {tag:Suppress} 칸을 모두 소진시키면 상대는 {tag:Suppress} 상태에 돌입하며, 캐릭터의 행동이 빨라집니다."));
    }
}
