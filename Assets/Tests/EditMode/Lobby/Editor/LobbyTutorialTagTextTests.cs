using NUnit.Framework;
using UnityEditor;

/// <summary>문구 안 {tag:ID} 표기 — 할 일 157 6b(SO 기본 문구 3행 · 과도기 어댑터의 평문 치환). 색 처리는 158 몫.</summary>
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

    [Test]
    public void 어댑터는_알려진_태그_3개를_평문으로_바꾼다()
    {
        Assert.That(LobbyTutorialGuideAdapter.ToPlainTags("{tag:Knockback} 또는 {tag:Stun}, {tag:Suppress}·{tag:Suppress}"),
            Is.EqualTo("[넉백] 또는 [기절], [제압]·[제압]"));
        Assert.That(LobbyTutorialGuideAdapter.ToPlainTags(Text("LOBBY_OBJ_VULN_CC")),
            Is.EqualTo("취약 상태의 상대에게 [넉백] 또는 [기절] 속성이 있는 스킬을 사용하세요"));
    }

    [Test]
    public void 모르는_태그와_다른_자리표시자는_그대로_둔다()
    {
        Assert.That(LobbyTutorialGuideAdapter.ToPlainTags("{tag:Bleed} {tag:knockback} {n} {Q}"),
            Is.EqualTo("{tag:Bleed} {tag:knockback} {n} {Q}"));
    }

    [Test]
    public void 닫히지_않은_토큰은_그대로_둔다()
    {
        Assert.That(LobbyTutorialGuideAdapter.ToPlainTags("상대의 {tag:Suppress 칸"), Is.EqualTo("상대의 {tag:Suppress 칸"));
        Assert.That(LobbyTutorialGuideAdapter.ToPlainTags("{tag:Stun} 끝 {tag:"), Is.EqualTo("[기절] 끝 {tag:"));
        Assert.That(LobbyTutorialGuideAdapter.ToPlainTags(""), Is.EqualTo(""));
        Assert.That(LobbyTutorialGuideAdapter.ToPlainTags(null), Is.EqualTo(""));
    }
}
