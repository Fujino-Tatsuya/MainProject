using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>로비 튜토리얼 단계표 — 할 일 157 PLAN §2.3(명세 상태 22행·상태 ID 유일·종류별 필수 칸·T20 카운트다운).</summary>
public sealed class LobbyTutorialStepTableTests
{
    const string AssetPath = "Assets/9.ScriptableObject/Lobby/LobbyTutorialSteps.asset";

    static readonly string[] ExpectedOrder =
    {
        "L00_LockerVisit", "L10_CentralGather", "L11_CentralIntro", "L20_TrainingGather", "L21_TrainingIntro",
        "T10_Basic", "T20_CombatGather", "T22_Combat", "T30_CounterGather", "T31_Counter", "T32_VulnerableCC",
        "T33_ResetPosition", "T34_Wall", "T40_SuppressKill", "T50_FreePractice", "L30_BriefGather",
        "L31_BriefIntro", "L32_BriefFactory", "L33_BriefRebellion", "L34_SortieIntro", "L40_SortieGather", "L41_Loading",
    };

    static LobbyTutorialStepTable LoadAsset()
    {
        var table = AssetDatabase.LoadAssetAtPath<LobbyTutorialStepTable>(AssetPath);
        Assert.That(table, Is.Not.Null, AssetPath);
        return table;
    }

    static LobbyTutorialStep Row(string id, LobbyTutorialStepKind kind) => new LobbyTutorialStep { stateId = id, kind = kind };

    [Test]
    public void 에셋은_명세_22행이고_검증을_통과한다()
    {
        var table = LoadAsset();
        var errors = new List<string>();

        Assert.That(table.Validate(errors), Is.True, string.Join("\n", errors));
        Assert.That(table.Count, Is.EqualTo(ExpectedOrder.Length));   // 명세 §2 상태 23개 − T21(T20 의 표시 상태) = 22
    }

    [Test]
    public void 명세_순서대로_주요_상태가_있다()
    {
        var table = LoadAsset();
        var last = -1;
        foreach (var id in ExpectedOrder)
        {
            var index = table.IndexOf(id);
            Assert.That(index, Is.GreaterThan(last), id);
            last = index;
        }

        Assert.That(table.IndexOf("T21_CombatCountdown"), Is.EqualTo(-1), "T21 은 T20 의 표시 상태 — 행이 아니다");
        Assert.That(table[0].kind, Is.EqualTo(LobbyTutorialStepKind.LockerVisit));
        Assert.That(table[table.Count - 1].kind, Is.EqualTo(LobbyTutorialStepKind.Loading));
    }

    [Test]
    public void T20_은_3초_카운트다운과_T21_표시_상태를_가진다()
    {
        var table = LoadAsset();
        var t20 = table[table.IndexOf("T20_CombatGather")];

        Assert.That(t20.kind, Is.EqualTo(LobbyTutorialStepKind.Gather));
        Assert.That(t20.holdSeconds, Is.EqualTo(3f));
        Assert.That(t20.countdownStateId, Is.EqualTo("T21_CombatCountdown"));
        Assert.That(t20.countdownObjectiveKey, Is.EqualTo("LOBBY_OBJ_COMBAT"));
    }

    [Test]
    public void 나머지_집결은_유지_0_3초()
    {
        var table = LoadAsset();
        foreach (var step in table.Steps)
        {
            if (step.kind == LobbyTutorialStepKind.Gather && step.stateId != "T20_CombatGather")
                Assert.That(step.holdSeconds, Is.EqualTo(0.3f).Within(1e-4f), step.stateId);
        }
    }

    [Test]
    public void 브리핑_대사_4행만_입력_잠금()
    {
        var table = LoadAsset();
        var locked = new List<string>();
        foreach (var step in table.Steps)
        {
            if (step.locksInput)
                locked.Add(step.stateId);
        }

        Assert.That(locked, Is.EqualTo(new[] { "L31_BriefIntro", "L32_BriefFactory", "L33_BriefRebellion", "L34_SortieIntro" }));
    }

    [Test]
    public void T10_체크_항목은_6개()
    {
        var table = LoadAsset();
        var t10 = table[table.IndexOf("T10_Basic")];

        Assert.That(t10.activity, Is.EqualTo(LobbyTutorialActivity.BasicCheck));
        Assert.That(t10.checklistKeys.Length, Is.EqualTo(LobbyWeaponCheckRules.DisplayOrder.Length));
        Assert.That(t10.progressMode, Is.EqualTo(LobbyTutorialProgressMode.PartyComplete));
    }

    [Test]
    public void T10_체크_문구_키는_PLAN_6_LOBBY_CHECK_를_항목_값_순서로()
    {
        var table = LoadAsset();
        var t10 = table[table.IndexOf("T10_Basic")];

        Assert.That(t10.checklistKeys, Is.EqualTo(new[]
        {
            "LOBBY_CHECK_BASIC_ATTACK", "LOBBY_CHECK_Q", "LOBBY_CHECK_E", "LOBBY_CHECK_R", "LOBBY_CHECK_RMB", "LOBBY_CHECK_DASH",
        }));
        Assert.That(t10.progressKey, Is.EqualTo("LOBBY_PROGRESS_PARTY_DONE"), "하단 '훈련 완료 n/N'");
    }

    [Test]
    public void 태그_검사는_기본_꺼짐이고_T32_T34_에_태그가_있다()
    {
        var table = LoadAsset();

        Assert.That(table.EnforceRequiredSkillTags, Is.False);
        Assert.That(table[table.IndexOf("T32_VulnerableCC")].requiredSkillTags,
            Is.EquivalentTo(new[] { SkillTooltipTag.Knockback, SkillTooltipTag.Stun }));
        Assert.That(table[table.IndexOf("T34_Wall")].requiredSkillTags, Is.EquivalentTo(new[] { SkillTooltipTag.Knockback }));
        Assert.That(table[table.IndexOf("T34_Wall")].targetWallId, Is.EqualTo("WALL_TRAIN_WEST"));
    }

    [Test]
    public void 검증기_빈표()
    {
        var errors = new List<string>();
        Assert.That(LobbyTutorialStepTable.Validate(new LobbyTutorialStep[0], errors), Is.False);
    }

    [Test]
    public void 검증기_상태ID_중복과_빈값()
    {
        var errors = new List<string>();
        var rows = new[] { Row("A", LobbyTutorialStepKind.LockerVisit), Row("A", LobbyTutorialStepKind.Loading), Row("", LobbyTutorialStepKind.Loading) };

        Assert.That(LobbyTutorialStepTable.Validate(rows, errors), Is.False);
        Assert.That(errors.Count, Is.EqualTo(2));
    }

    [Test]
    public void 검증기_종류별_필수_칸()
    {
        var errors = new List<string>();
        var rows = new[]
        {
            Row("L", LobbyTutorialStepKind.LockerVisit),
            Row("G", LobbyTutorialStepKind.Gather),         // gatherPoint 없음
            Row("D", LobbyTutorialStepKind.Dialogue),       // dialogueId 없음
            Row("A", LobbyTutorialStepKind.Activity),       // activity 없음
            Row("L2", LobbyTutorialStepKind.LockerVisit),   // 첫 행 아님
        };

        Assert.That(LobbyTutorialStepTable.Validate(rows, errors), Is.False);
        Assert.That(errors.Count, Is.EqualTo(4), string.Join("\n", errors));
    }

    [Test]
    public void 검증기_카운트다운_표시는_Gather_행만()
    {
        var errors = new List<string>();
        var bad = Row("D", LobbyTutorialStepKind.Dialogue);
        bad.dialogueId = "X";
        bad.countdownStateId = "T21";
        bad.countdownObjectiveKey = "K";

        Assert.That(LobbyTutorialStepTable.Validate(new[] { bad }, errors), Is.False);
    }

    [Test]
    public void IndexOf_없는_ID_는_음수()
    {
        var table = ScriptableObject.CreateInstance<LobbyTutorialStepTable>();
        try
        {
            table.EditorSetSteps(new[] { Row("A", LobbyTutorialStepKind.LockerVisit) });
            Assert.That(table.IndexOf("A"), Is.EqualTo(0));
            Assert.That(table.IndexOf("B"), Is.EqualTo(-1));
            Assert.That(table.IndexOf(null), Is.EqualTo(-1));
        }
        finally
        {
            Object.DestroyImmediate(table);
        }
    }
}
