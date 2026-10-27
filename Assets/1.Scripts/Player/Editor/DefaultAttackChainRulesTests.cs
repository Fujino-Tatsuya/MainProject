using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 122 — 팔라딘(가붕이) 평타 홀드 순환: 스텝 End 의 다음 타 규칙 + 팔라딘 데이터·클립이 4타 → 1타 순환을 받치는지
/// + 공속(제압 가속)이 올라도 되감긴 1타가 같은 클립 시각(창 열림 앞)으로 들어가는지.
/// 어쌔신은 AssassinComboWindowRulesTests, 거너는 별도 구현이라 여기서 다루지 않는다.
/// </summary>
public class DefaultAttackChainRulesTests
{
    const string PaladinPrefabPath = "Assets/2.Prefabs/Player/Paladin/Player_Paladin.prefab";
    const string PaladinDataPath = "Assets/9.ScriptableObject/Player/Garen/PlayerDefaultAttackData.asset";

    [Test]
    public void Loop_LastStepQueued_WrapsToFirst()
    {
        Assert.That(DefaultAttackChainRules.TryResolveNextStep(3, 4, DefaultAttackChainPolicy.Loop, true, out int next), Is.True);
        Assert.That(next, Is.EqualTo(0));
    }

    [Test]
    public void Once_LastStepQueued_Ends()
    {
        Assert.That(DefaultAttackChainRules.TryResolveNextStep(3, 4, DefaultAttackChainPolicy.Once, true, out _), Is.False);
    }

    [Test]
    public void MiddleStepQueued_AdvancesForBothPolicies()
    {
        foreach (DefaultAttackChainPolicy policy in new[] { DefaultAttackChainPolicy.Once, DefaultAttackChainPolicy.Loop })
        {
            Assert.That(DefaultAttackChainRules.TryResolveNextStep(1, 4, policy, true, out int next), Is.True, policy.ToString());
            Assert.That(next, Is.EqualTo(2), policy.ToString());
        }
    }

    [Test]
    public void NotQueued_AlwaysEnds()
    {
        Assert.That(DefaultAttackChainRules.TryResolveNextStep(0, 4, DefaultAttackChainPolicy.Loop, false, out _), Is.False);
        Assert.That(DefaultAttackChainRules.TryResolveNextStep(3, 4, DefaultAttackChainPolicy.Loop, false, out _), Is.False);
    }

    [Test]
    public void NoSteps_Ends()
    {
        Assert.That(DefaultAttackChainRules.TryResolveNextStep(0, 0, DefaultAttackChainPolicy.Loop, true, out _), Is.False);
    }

    [Test]
    public void LoopHold_TenSteps_CyclesFourThenBackToFirst()
    {
        // 홀드 = 매 스텝 창 안에서 예약된다(PlayerBasicAttackInputRules.ShouldRequestNextMeleeAttack).
        int index = 0;
        int[] expected = { 1, 2, 3, 0, 1, 2, 3, 0, 1, 2 };
        for (int i = 0; i < expected.Length; i++)
        {
            bool queued = PlayerBasicAttackInputRules.ShouldRequestNextMeleeAttack(true, false, false);
            Assert.That(DefaultAttackChainRules.TryResolveNextStep(index, 4, DefaultAttackChainPolicy.Loop, queued, out index),
                Is.True, $"{i + 1}번째 End");
            Assert.That(index, Is.EqualTo(expected[i]), $"{i + 1}번째 End");
        }
    }

    [Test]
    public void PaladinPrefab_UsesLoopData()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PaladinPrefabPath);
        Assert.That(prefab, Is.Not.Null, PaladinPrefabPath);
        var controller = prefab.GetComponent<DefaultAttackController>();
        Assert.That(controller, Is.Not.Null);

        var attackData = new SerializedObject(controller).FindProperty("attackData").objectReferenceValue as DefaultAttackData;
        Assert.That(attackData, Is.Not.Null, "Player_Paladin 의 attackData — Awake ApplyData 가 chainPolicy 를 이 SO 값으로 덮는다");
        Assert.That(AssetDatabase.GetAssetPath(attackData), Is.EqualTo(PaladinDataPath));
        Assert.That(attackData.ChainPolicy, Is.EqualTo(DefaultAttackChainPolicy.Loop));
    }

    [Test]
    public void PaladinSteps_OpenComboWindowBeforeEnd()
    {
        // 홀드 순환은 매 스텝(마지막 포함) ComboWindowOpen 이 End 보다 앞서야 예약된다.
        var data = AssetDatabase.LoadAssetAtPath<DefaultAttackData>(PaladinDataPath);
        Assert.That(data, Is.Not.Null);
        Assert.That(data.AttackSteps.Length, Is.EqualTo(4));

        string function = nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent);
        for (int i = 0; i < data.AttackSteps.Length; i++)
        {
            AnimationClip clip = data.AttackSteps[i].Clip;
            Assert.That(clip, Is.Not.Null, $"{i + 1}타 클립");

            float open = -1f, end = -1f;
            foreach (AnimationEvent e in AnimationUtility.GetAnimationEvents(clip))
            {
                if (e.functionName != function)
                    continue;
                if (e.intParameter == (int)DefaultAttackAnimationEventType.ComboWindowOpen && open < 0f)
                    open = e.time;
                if (e.intParameter == (int)DefaultAttackAnimationEventType.End && end < 0f)
                    end = e.time;
            }

            Assert.That(open, Is.GreaterThanOrEqualTo(0f), $"{i + 1}타 '{clip.name}' ComboWindowOpen 없음");
            Assert.That(end, Is.GreaterThan(open), $"{i + 1}타 '{clip.name}' End 가 창 열림보다 늦어야 한다");
        }

        // 1타 루프백 진입 시각이 창 열림보다 앞서야 순환한 1타에서도 창이 열린다.
        AnimationClip first = data.AttackSteps[0].Clip;
        foreach (AnimationEvent e in AnimationUtility.GetAnimationEvents(first))
        {
            if (e.functionName == function && e.intParameter == (int)DefaultAttackAnimationEventType.ComboWindowOpen)
                Assert.That(data.AttackSteps[0].LoopBackEntryTime, Is.LessThan(e.time), "1타 loopBackEntryTime < ComboWindowOpen");
        }
    }

    // ─── 제압 가속 중 순환 끊김(할 일 122 후속) ─────────────────────────────
    // 가붕이 콤보 상태는 Speed Multiplier = AttackSpeed 파라미터라, CrossFadeInFixedTime 의 오프셋(초)은 상태 시간이다 —
    // 클립 시각으로는 오프셋 × 속도가 된다. 되감기 진입 시각(클립 타임라인)을 그대로 넘기면 가속(1.2 × 2 = 2.4) 때
    // 0.107 → 0.258 로 밀려 1타 창 열림(0.23)을 건너뛰고, 되감긴 1타에서 예약이 안 돼 체인이 끝난다.

    const string GarenControllerPath = "Assets/4.Animations/Player/Garen/GarenAnimatorController.controller";

    [TestCase(1f)]
    [TestCase(1.2f)]
    [TestCase(2.4f)]
    [TestCase(4f)]
    [TestCase(0.5f)]
    public void LoopBackOffset_EntersSameClipTimeAtAnySpeed(float attackSpeed)
    {
        const float clipTime = 0.10732772f;
        float offset = DefaultAttackChainRules.LoopBackFixedTimeOffset(clipTime, attackSpeed);
        Assert.That(offset * attackSpeed, Is.EqualTo(clipTime).Within(1e-5f), "들어가는 클립 시각 = 오프셋 × 상태 속도");
    }

    [Test]
    public void PaladinLoopBack_UnderSuppressHaste_EntersBeforeComboWindowOpen()
    {
        var data = AssetDatabase.LoadAssetAtPath<DefaultAttackData>(PaladinDataPath);
        Assert.That(data, Is.Not.Null);
        DefaultAttackStep first = data.AttackSteps[0];
        float open = FirstEventTime(first.Clip, DefaultAttackAnimationEventType.ComboWindowOpen);
        Assert.That(open, Is.GreaterThanOrEqualTo(0f), "1타 ComboWindowOpen 없음");

        foreach (float rate in new[] { 1f, HasteAttackSpeedMultiplier(), 3f })
        {
            float attackSpeed = Mathf.Max(data.AttackSpeed, 0.1f) * rate;
            float entered = DefaultAttackChainRules.LoopBackFixedTimeOffset(first.LoopBackEntryTime, attackSpeed) * attackSpeed;
            Assert.That(entered, Is.LessThan(open), $"공속 배율 ×{rate}(상태 속도 {attackSpeed}) — 되감긴 1타 진입 {entered} < 창 열림 {open}");
        }
    }

    // 엔진 전제까지 고정 — 실제 Garen 컨트롤러에 4타 → 1타 되감기 CrossFade 를 걸고 1타의 클립 시각을 잰다.
    [TestCase(1.2f)]
    [TestCase(2.4f)]
    public void GarenController_LoopBackCrossFade_EntersLoopBackClipTime(float attackSpeed)
    {
        var data = AssetDatabase.LoadAssetAtPath<DefaultAttackData>(PaladinDataPath);
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(GarenControllerPath);
        Assert.That(data, Is.Not.Null);
        Assert.That(controller, Is.Not.Null, GarenControllerPath);

        DefaultAttackStep first = data.AttackSteps[0];
        var go = new GameObject("DefaultAttackChainRulesTests_Animator") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            var animator = go.AddComponent<Animator>();
            animator.fireEvents = false;
            animator.runtimeAnimatorController = controller;
            animator.Rebind();
            animator.Update(0f);
            animator.SetFloat("AttackSpeed", attackSpeed);
            animator.Play("Default_Attack3", 0, 0f);
            animator.Update(0f);
            animator.Update(0.1f);

            animator.CrossFadeInFixedTime(Animator.StringToHash("Default_Attack0"), 0.05f, -1,
                DefaultAttackChainRules.LoopBackFixedTimeOffset(first.LoopBackEntryTime, attackSpeed));
            animator.Update(0f);

            Assert.That(animator.IsInTransition(0), Is.True);
            AnimatorStateInfo next = animator.GetNextAnimatorStateInfo(0);
            Assert.That(next.shortNameHash, Is.EqualTo(Animator.StringToHash("Default_Attack0")));
            Assert.That(next.normalizedTime * first.Clip.length, Is.EqualTo(first.LoopBackEntryTime).Within(1e-3f),
                $"상태 속도 {attackSpeed} — 1타 진입 클립 시각");
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    static float HasteAttackSpeedMultiplier()
    {
        var haste = Resources.Load<SuppressHasteData>(SuppressHasteData.ResourcesPath);
        return haste != null ? haste.Values.AttackSpeedMultiplier : SuppressHasteValues.Default.AttackSpeedMultiplier;
    }

    static float FirstEventTime(AnimationClip clip, DefaultAttackAnimationEventType type)
    {
        foreach (AnimationEvent e in AnimationUtility.GetAnimationEvents(clip))
        {
            if (e.functionName == nameof(PlayerAnimationEventRelay.HandleDefaultAttackEvent) && e.intParameter == (int)type)
                return e.time;
        }

        return -1f;
    }
}
