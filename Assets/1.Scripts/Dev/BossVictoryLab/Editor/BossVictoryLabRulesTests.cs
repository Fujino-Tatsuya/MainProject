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
