using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 몬스터 발밑 체력바 고정 — 피격 표시, 전투 종료 + 지연 숨김, 재피격 리셋, 사망 즉시 숨김,
/// 등급별 프리팹·발밑 오프셋, 부착 판단(최종 보스 제외·튜토리얼 스피너 자체 규칙 우선·바 2개 방지).
/// </summary>
public sealed class MonsterOverheadHealthBarVisibilityTests
{
    const float Delay = 3f;

    static MonsterOverheadHealthBarVisibility HitAt(float time, MonsterState state)
    {
        var v = new MonsterOverheadHealthBarVisibility();
        v.Tick(0f, 100, MonsterState.Idle, Delay);
        v.Tick(time, 90, state, Delay);
        return v;
    }

    [Test]
    public void Initially_Hidden_EvenInCombat()
    {
        var v = new MonsterOverheadHealthBarVisibility();

        Assert.That(v.Tick(0f, 100, MonsterState.Chase, Delay), Is.False);
        Assert.That(v.Tick(1f, 100, MonsterState.Attack, Delay), Is.False, "맞기 전엔 전투 중이어도 숨김");
    }

    [Test]
    public void Damage_Shows()
    {
        var v = new MonsterOverheadHealthBarVisibility();
        v.Tick(0f, 100, MonsterState.Idle, Delay);

        Assert.That(v.Tick(0.1f, 90, MonsterState.Hit, Delay), Is.True);
        Assert.That(v.IsShown, Is.True);
    }

    [Test]
    public void HealthIncrease_DoesNotShow()
    {
        var v = new MonsterOverheadHealthBarVisibility();
        v.Tick(0f, 50, MonsterState.Idle, Delay);

        Assert.That(v.Tick(0.1f, 100, MonsterState.Return, Delay), Is.False, "회복(리쉬 복귀 Revive)은 피격이 아니다");
    }

    [Test]
    public void StaysWhileInCombat()
    {
        var v = HitAt(1f, MonsterState.Chase);

        Assert.That(v.Tick(10f, 90, MonsterState.Chase, Delay), Is.True);
        Assert.That(v.Tick(20f, 90, MonsterState.Attack, Delay), Is.True, "전투 상태 동안은 시간이 지나도 유지");
    }

    [Test]
    public void CombatEnds_KeepsUntilDelay_ThenHides()
    {
        var v = HitAt(1f, MonsterState.Chase);
        v.Tick(5f, 90, MonsterState.Chase, Delay);       // 마지막 전투 프레임

        Assert.That(v.Tick(5.1f, 90, MonsterState.Idle, Delay), Is.True, "추격 종료 직후 유지");
        Assert.That(v.Tick(7.9f, 90, MonsterState.Return, Delay), Is.True, "3초 전엔 유지");
        Assert.That(v.Tick(8.0f, 90, MonsterState.Idle, Delay), Is.False, "추격 종료 + 3초 → 숨김");
    }

    [Test]
    public void HitOutsideCombat_HidesDelayAfterHit()
    {
        var v = HitAt(1f, MonsterState.Idle);

        Assert.That(v.Tick(3.9f, 90, MonsterState.Idle, Delay), Is.True);
        Assert.That(v.Tick(4.0f, 90, MonsterState.Idle, Delay), Is.False);
    }

    [Test]
    public void ReHit_ResetsTimer()
    {
        var v = HitAt(1f, MonsterState.Idle);

        Assert.That(v.Tick(3.5f, 80, MonsterState.Idle, Delay), Is.True, "재피격");
        Assert.That(v.Tick(6.4f, 80, MonsterState.Idle, Delay), Is.True, "재피격 기준 3초 전이라 유지");
        Assert.That(v.Tick(6.5f, 80, MonsterState.Idle, Delay), Is.False);
    }

    [Test]
    public void ReturnToCombat_AfterHide_StaysHiddenUntilHit()
    {
        var v = HitAt(1f, MonsterState.Idle);
        v.Tick(5f, 90, MonsterState.Idle, Delay);

        Assert.That(v.Tick(6f, 90, MonsterState.Chase, Delay), Is.False, "다시 추격만 해서는 켜지지 않는다");
        Assert.That(v.Tick(6.1f, 85, MonsterState.Hit, Delay), Is.True);
    }

    [Test]
    public void DeadState_HidesImmediately()
    {
        var v = HitAt(1f, MonsterState.Chase);

        Assert.That(v.Tick(1.1f, 10, MonsterState.Dead, Delay), Is.False);
    }

    [Test]
    public void ZeroHealth_HidesImmediately_BeforeDeadReplicates()
    {
        var v = HitAt(1f, MonsterState.Chase);

        Assert.That(v.Tick(1.1f, 0, MonsterState.Hit, Delay), Is.False);
    }

    [Test]
    public void Reset_HidesAndForgetsHealth()
    {
        var v = HitAt(1f, MonsterState.Chase);
        v.Reset();

        Assert.That(v.IsShown, Is.False);
        Assert.That(v.Tick(2f, 50, MonsterState.Chase, Delay), Is.False, "리셋 직후 첫 값은 기준값 — 피격으로 보지 않는다");
    }

    [TestCase(MonsterState.Chase, true)]
    [TestCase(MonsterState.Attack, true)]
    [TestCase(MonsterState.Hit, true)]
    [TestCase(MonsterState.Groggy, true)]
    [TestCase(MonsterState.Knockback, true)]
    [TestCase(MonsterState.Idle, false)]
    [TestCase(MonsterState.Return, false)]
    [TestCase(MonsterState.Dead, false)]
    public void CombatStates(MonsterState state, bool expected)
    {
        Assert.That(MonsterOverheadHealthBarVisibility.IsCombatState(state), Is.EqualTo(expected));
    }

    [Test]
    public void ForRank_MidBossOnlyPicksMidBossValue()
    {
        Assert.That(MonsterOverheadHealthBarVisibility.ForRank(MonsterRank.MidBoss, "normal", "mid"), Is.EqualTo("mid"));
        Assert.That(MonsterOverheadHealthBarVisibility.ForRank(MonsterRank.Normal, "normal", "mid"), Is.EqualTo("normal"));
        Assert.That(MonsterOverheadHealthBarVisibility.ForRank(MonsterRank.Boss, 1f, 2f), Is.EqualTo(1f), "최종 보스는 붙지 않지만 고르면 일반값");
    }

    [Test]
    public void FootScreenOffset_PointsDown()
    {
        Assert.That(MonsterOverheadHealthBarVisibility.FootScreenOffset(50f), Is.EqualTo(new Vector2(0f, -50f)));
        Assert.That(MonsterOverheadHealthBarVisibility.FootScreenOffset(-10f), Is.EqualTo(Vector2.zero), "음수는 0 — 발 위로 올라가지 않는다");
    }

    [Test]
    public void FootAnchor_SitsBelowFeetOnScreen()
    {
        // 발밑 기준점(높이 0) + 아래 오프셋 → 몬스터 위치 화면 좌표보다 아래.
        Vector3 anchor = OverheadHealthBarScreenPlacement.AnchorWorld(new Vector3(3f, 0f, 2f), 0f);
        Assert.That(anchor, Is.EqualTo(new Vector3(3f, 0f, 2f)));

        bool visible = OverheadHealthBarScreenPlacement.TryGetCanvasPosition(
            new Vector3(960f, 540f, 10f), new Vector2(1920f, 1080f), 1f,
            MonsterOverheadHealthBarVisibility.FootScreenOffset(100f), new Vector2(60f, 10f), out Vector2 pos);
        Assert.That(visible, Is.True);
        Assert.That(pos, Is.EqualTo(new Vector2(960f, 440f)));
    }

    [Test]
    public void Settings_PicksPrefabAndOffsetByRank()
    {
        var settings = ScriptableObject.CreateInstance<MonsterOverheadHealthBarSettings>();
        var normalGo = new GameObject("normal", typeof(UnitOverheadHealthBar));
        var midGo = new GameObject("mid", typeof(UnitOverheadHealthBar));
        try
        {
            var so = new SerializedObject(settings);
            so.FindProperty("normalBarPrefab").objectReferenceValue = normalGo.GetComponent<UnitOverheadHealthBar>();
            so.FindProperty("midBossBarPrefab").objectReferenceValue = midGo.GetComponent<UnitOverheadHealthBar>();
            so.FindProperty("footScreenOffset").floatValue = 40f;
            so.FindProperty("midBossFootScreenOffset").floatValue = 90f;
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(settings.BarPrefab(MonsterRank.Normal), Is.SameAs(normalGo.GetComponent<UnitOverheadHealthBar>()));
            Assert.That(settings.BarPrefab(MonsterRank.MidBoss), Is.SameAs(midGo.GetComponent<UnitOverheadHealthBar>()));
            Assert.That(settings.FootScreenOffset(MonsterRank.Normal), Is.EqualTo(40f));
            Assert.That(settings.FootScreenOffset(MonsterRank.MidBoss), Is.EqualTo(90f));
        }
        finally
        {
            Object.DestroyImmediate(normalGo);
            Object.DestroyImmediate(midGo);
            Object.DestroyImmediate(settings);
        }
    }

    [Test]
    public void SettingsAsset_HasBothPrefabsAndDefaults()
    {
        var settings = Resources.Load<MonsterOverheadHealthBarSettings>(MonsterOverheadHealthBarSettings.ResourcePath);
        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.BarPrefab(MonsterRank.Normal), Is.Not.Null, "MonsterHealthBar_Normal");
        Assert.That(settings.BarPrefab(MonsterRank.MidBoss), Is.Not.Null, "MonsterHealthBar_MidBoss");
        Assert.That(settings.BarPrefab(MonsterRank.MidBoss), Is.Not.SameAs(settings.BarPrefab(MonsterRank.Normal)));
        Assert.That(settings.HideDelaySeconds, Is.EqualTo(3f));
    }

    [Test]
    public void MonsterBarPrefabs_MidBossIsThicker()
    {
        var normal = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2.Prefabs/UI/MonsterHealthBar_Normal.prefab");
        var mid = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2.Prefabs/UI/MonsterHealthBar_MidBoss.prefab");
        Assert.That(normal, Is.Not.Null);
        Assert.That(mid, Is.Not.Null);
        float normalH = ((RectTransform)normal.transform).sizeDelta.y;
        float midH = ((RectTransform)mid.transform).sizeDelta.y;
        Assert.That(midH, Is.GreaterThan(normalH));
    }

    [Test]
    public void TutorialSpinnerPrefab_HasOwnRuleButNoBakedBar()
    {
        var spinner = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2.Prefabs/Monster/Tutorial/SpinnerBot_Tutorial.prefab");
        Assert.That(spinner, Is.Not.Null);
        Assert.That(spinner.GetComponent<TutorialSpinnerHealthBarRule>(), Is.Not.Null);
        Assert.That(spinner.GetComponentInChildren<UnitOverheadHealthBar>(true), Is.Null, "바는 런타임 부착 — 프리팹에 박혀 있으면 2개가 된다");
        Assert.That(spinner.GetComponent<MonsterBase>().Rank, Is.EqualTo(MonsterRank.MidBoss), "중간 보스 바를 쓴다");
    }

    [Test]
    public void DecideInstall_NormalAndMidBoss_AttachWithRule()
    {
        Assert.That(Decide(MonsterRank.Normal), Is.EqualTo(MonsterHealthBarInstall.AttachWithRule));
        Assert.That(Decide(MonsterRank.MidBoss), Is.EqualTo(MonsterHealthBarInstall.AttachWithRule));
    }

    [Test]
    public void DecideInstall_ExcludesFinalBoss()
    {
        Assert.That(Decide(MonsterRank.Boss), Is.EqualTo(MonsterHealthBarInstall.Skip));
        Assert.That(Decide(MonsterRank.Normal, bossHud: true), Is.EqualTo(MonsterHealthBarInstall.Skip), "BossHudTarget");
        Assert.That(Decide(MonsterRank.Boss, otherRule: true), Is.EqualTo(MonsterHealthBarInstall.Skip), "자체 규칙이 있어도 최종 보스는 제외");
    }

    [Test]
    public void DecideInstall_TutorialSpinner_OwnRuleWins()
    {
        Assert.That(Decide(MonsterRank.MidBoss, otherRule: true), Is.EqualTo(MonsterHealthBarInstall.AttachBarOnly),
            "튜토리얼 스피너봇: 바만 붙이고 표시·색·라벨은 TutorialSpinnerHealthBarRule");
    }

    [Test]
    public void DecideInstall_NeverTwoBars()
    {
        Assert.That(Decide(MonsterRank.Normal, bar: true), Is.EqualTo(MonsterHealthBarInstall.Skip), "프리팹에 바가 이미 있음");
        Assert.That(Decide(MonsterRank.MidBoss, bar: true, otherRule: true), Is.EqualTo(MonsterHealthBarInstall.Skip),
            "자체 규칙 몬스터 재스폰(풀) — 전에 붙인 바가 남아 있다");
        Assert.That(Decide(MonsterRank.Normal, thisRule: true, bar: true), Is.EqualTo(MonsterHealthBarInstall.ResetExisting),
            "이 규칙 재스폰 — 리셋만");
    }

    static MonsterHealthBarInstall Decide(MonsterRank rank, bool bossHud = false, bool thisRule = false, bool bar = false, bool otherRule = false)
        => MonsterOverheadHealthBarVisibility.DecideInstall(rank, bossHud, thisRule, bar, otherRule);
}
