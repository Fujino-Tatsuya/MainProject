using NUnit.Framework;

/// <summary>로비 결과창 등급 도장 규칙(할 일 223) — 6분 이하 S, 9분 이하 A, 12분 이하 B, 그 위 C(경계 포함·정수 초).</summary>
public sealed class LobbyResultGradeTests
{
    [Test]
    public void Evaluate_DefaultBoundaries_InclusiveByMinute()
    {
        Assert.AreEqual(LobbyResultGradeRank.S, LobbyResultGrade.Evaluate(0f));
        Assert.AreEqual(LobbyResultGradeRank.S, LobbyResultGrade.Evaluate(230f));   // 4분보다 빠름
        Assert.AreEqual(LobbyResultGradeRank.S, LobbyResultGrade.Evaluate(360f));   // 06:00
        Assert.AreEqual(LobbyResultGradeRank.S, LobbyResultGrade.Evaluate(360.9f)); // 화면 "06:00" — 초 아래 버림
        Assert.AreEqual(LobbyResultGradeRank.A, LobbyResultGrade.Evaluate(361f));
        Assert.AreEqual(LobbyResultGradeRank.A, LobbyResultGrade.Evaluate(540f));
        Assert.AreEqual(LobbyResultGradeRank.B, LobbyResultGrade.Evaluate(541f));
        Assert.AreEqual(LobbyResultGradeRank.B, LobbyResultGrade.Evaluate(720f));
        Assert.AreEqual(LobbyResultGradeRank.C, LobbyResultGrade.Evaluate(721f));
        Assert.AreEqual(LobbyResultGradeRank.C, LobbyResultGrade.Evaluate(5000f));
        Assert.AreEqual(LobbyResultGradeRank.S, LobbyResultGrade.Evaluate(-3f));    // 음수 = 0
    }

    [Test]
    public void Evaluate_CustomBoundaries_AndLetters()
    {
        Assert.AreEqual(LobbyResultGradeRank.S, LobbyResultGrade.Evaluate(300f, 5f, 8f, 10f));
        Assert.AreEqual(LobbyResultGradeRank.A, LobbyResultGrade.Evaluate(301f, 5f, 8f, 10f));
        Assert.AreEqual(LobbyResultGradeRank.C, LobbyResultGrade.Evaluate(601f, 5f, 8f, 10f));

        Assert.AreEqual("S", LobbyResultGrade.Letter(LobbyResultGradeRank.S));
        Assert.AreEqual("A", LobbyResultGrade.Letter(LobbyResultGradeRank.A));
        Assert.AreEqual("B", LobbyResultGrade.Letter(LobbyResultGradeRank.B));
        Assert.AreEqual("C", LobbyResultGrade.Letter(LobbyResultGradeRank.C));
    }
}
