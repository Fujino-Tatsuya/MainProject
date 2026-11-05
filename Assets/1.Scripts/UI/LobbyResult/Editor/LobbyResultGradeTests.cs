using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>로비 결과창 등급 도장 규칙(할 일 223) — 6분 이하 S, 9분 이하 A, 12분 이하 B, 그 위 C(경계 포함·정수 초), 클리어 판에만 도장.</summary>
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

    [Test]
    public void ShowsStamp_OnlyWhenCleared()
    {
        Assert.IsTrue(LobbyResultGrade.ShowsStamp(SessionOutcome.Cleared));
        Assert.IsFalse(LobbyResultGrade.ShowsStamp(SessionOutcome.Wiped));   // 전멸(패배)
        Assert.IsFalse(LobbyResultGrade.ShowsStamp(SessionOutcome.Aborted)); // 중단
    }

    [Test]
    public void Prefab_GradeStamp_IsCleanRedCircle_WithSeoulAlrimLetter()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyResultPanelAuthoring.PanelPath);
        Assert.IsNotNull(prefab);
        Transform stamp = prefab.transform.Find("Content/Panel/GradeStamp");
        Assert.IsNotNull(stamp, "GradeStamp 없음");

        Image image = stamp.GetComponent<Image>();
        Assert.IsNotNull(image.sprite);
        Assert.AreEqual("Assets/2.Prefabs/UI/Textures/ResultGradeCircle.png", AssetDatabase.GetAssetPath(image.sprite)); // git 흰 원(도장 스프라이트·Knob 아님)
        Assert.AreEqual(205f / 255f, image.color.r, 0.01f);
        Assert.AreEqual(32f / 255f, image.color.g, 0.01f);
        Assert.AreEqual(36f / 255f, image.color.b, 0.01f);
        Assert.AreEqual(-8f, Mathf.DeltaAngle(0f, stamp.localEulerAngles.z), 0.01f);

        TMP_Text letter = stamp.GetComponentInChildren<TMP_Text>(true);
        Assert.IsNotNull(letter);
        StringAssert.Contains("SeoulAlrimTTF-Medium", letter.font.name);
    }
}
