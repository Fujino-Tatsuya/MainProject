using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 반복 디버그 랩(할 일 244)의 순수 규칙(<see cref="BossVictoryLabRules"/>)과 씬 격리.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryLab).
/// </summary>
public sealed class BossVictoryLabRulesTests
{
    const string LabScenePath = "Assets/0.Scenes/Debug/DebugBossVictory.unity";

    [Test]
    public void 단계_이름은_단계_시각_경계를_따른다()
    {
        StringAssert.StartsWith("추가 연출", BossVictoryLabRules.StageName(-0.01f, false));
        Assert.AreEqual("처치 순간", BossVictoryLabRules.StageName(0f, false));
        StringAssert.Contains("카메라 하강", BossVictoryLabRules.StageName(0.5f, false));
        StringAssert.Contains("주황", BossVictoryLabRules.StageName(0.75f, false));
        StringAssert.Contains("정지 구도", BossVictoryLabRules.StageName(1.99f, false));
        StringAssert.Contains("상승", BossVictoryLabRules.StageName(2f, false));
        StringAssert.Contains("결과 대기", BossVictoryLabRules.StageName(3.2f, false));
        StringAssert.Contains("차단", BossVictoryLabRules.StageName(3.2f, true));
    }

    [Test]
    public void 막타_각도_0은_화면_아래_90은_시계_방향()
    {
        Vector3 boss = new Vector3(1f, 0.7f, 2f);
        Vector3 south = Vector3.back;
        Vector3 p0 = BossVictoryLabRules.KillerPosition(boss, south, 0f, 3f, 0.5f);
        Assert.That(Vector3.Distance(p0, new Vector3(1f, 0.5f, -1f)), Is.LessThan(1e-4f));
        Vector3 p90 = BossVictoryLabRules.KillerPosition(boss, south, 90f, 3f, 0.5f);
        Assert.That(Vector3.Distance(p90, new Vector3(-2f, 0.5f, 2f)), Is.LessThan(1e-4f));   // 위에서 볼 때 남 → 서(시계 방향)
    }

    [Test]
    public void 막타는_보스를_보고_팀원은_막타_기준_로컬_오프셋()
    {
        Vector3 killer = Vector3.zero, boss = new Vector3(0f, 2f, 5f);
        Quaternion face = BossVictoryLabRules.KillerFacing(killer, boss, 0f);
        Assert.That(Vector3.Angle(face * Vector3.forward, Vector3.forward), Is.LessThan(0.01f));
        Vector3 mate = BossVictoryLabRules.TeammatePosition(killer, face, new Vector3(-2.5f, 9f, -1.5f));
        Assert.That(Vector3.Distance(mate, new Vector3(-2.5f, 0f, -1.5f)), Is.LessThan(1e-4f));   // y 는 막타 높이
    }

    [Test]
    public void 점프_배속은_남은_시간을_한_프레임에_먹되_상한이_있고_지나면_0()
    {
        Assert.AreEqual(BossVictoryLabRules.SeekMaxRate, BossVictoryLabRules.SeekRate(3f, 0f, 1f / 60f));
        Assert.That(BossVictoryLabRules.SeekRate(1f, 0.99f, 0.02f), Is.EqualTo(0.5f).Within(1e-4f));
        Assert.AreEqual(0f, BossVictoryLabRules.SeekRate(1f, 1f, 0.02f));
        Assert.AreEqual(0f, BossVictoryLabRules.SeekRate(1f, 1.5f, 0.02f));
    }

    [Test]
    public void 시계_밀기는_실제_시간과_게임_시간의_차이()
    {
        Assert.That(BossVictoryLabRules.ClockShift(0.02f, 0.02f), Is.EqualTo(0d).Within(1e-9));
        Assert.That(BossVictoryLabRules.ClockShift(0.02f, 0f), Is.EqualTo(0.02d).Within(1e-6));      // 일시정지 — 처치 시각이 같이 밀려 단계 시각이 멈춘다
        Assert.That(BossVictoryLabRules.ClockShift(0.02f, 0.16f), Is.EqualTo(-0.14d).Within(1e-6));  // ×8 빨리 감기
    }

    [Test]
    public void 랩_씬이_있고_빌드_목록에는_없다()
    {
        Assert.IsTrue(File.Exists(LabScenePath));
        string guid = AssetDatabase.AssetPathToGUID("Assets/1.Scripts/Dev/BossVictoryLab/DebugBossVictoryLab.cs");
        Assert.IsFalse(string.IsNullOrEmpty(guid));
        StringAssert.Contains("guid: " + guid, File.ReadAllText(LabScenePath));
        string[] buildScenes = EditorBuildSettings.scenes.Select(s => s.path.Replace('\\', '/')).ToArray();
        CollectionAssert.DoesNotContain(buildScenes, LabScenePath);
    }
}
