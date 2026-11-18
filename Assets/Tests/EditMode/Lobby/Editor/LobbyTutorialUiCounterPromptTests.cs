using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>할 일 303 — 간파 훈련 봇 머리 우상단 "우클릭!" 표시 규칙·문구·설정 에셋.</summary>
public sealed class LobbyTutorialUiCounterPromptTests
{
    [TestCase(LobbyTutorialActivity.Counter)]
    [TestCase(LobbyTutorialActivity.VulnerableCc)]
    [TestCase(LobbyTutorialActivity.ResetPosition)]
    [TestCase(LobbyTutorialActivity.Wall)]
    [TestCase(LobbyTutorialActivity.SuppressKill)]
    public void 훈련_단계에서_창이_열리면_표시(LobbyTutorialActivity activity)
    {
        Assert.That(LobbyCounterPromptRules.WindowActive(activity, true), Is.True);
        Assert.That(LobbyCounterPromptRules.WindowActive(activity, false), Is.False, "취약·제압 = 창이 안 열림");
    }

    [TestCase(LobbyTutorialActivity.None)]
    [TestCase(LobbyTutorialActivity.Combat)]
    [TestCase(LobbyTutorialActivity.FreePractice)]
    public void 훈련_밖에서는_창이_열려도_표시_안_함(LobbyTutorialActivity activity)
    {
        Assert.That(LobbyCounterPromptRules.WindowActive(activity, true), Is.False);
    }

    [TestCase(LobbyTutorialActivity.VulnerableCc, TrainingSparringStage.VulnerableCc)]
    [TestCase(LobbyTutorialActivity.Wall, TrainingSparringStage.Wall)]
    public void T32_T34_는_넉백_차례에_창이_없어도_표시하고_반응_중엔_숨긴다(LobbyTutorialActivity activity, TrainingSparringStage stage)
    {
        Assert.That(LobbyCounterPromptRules.PromptActive(activity, stage, false, false), Is.True);
        Assert.That(LobbyCounterPromptRules.PromptActive(activity, stage, false, true), Is.False, "밀림·기절·복구 대기");
    }

    [TestCase(LobbyTutorialActivity.ResetPosition, TrainingSparringStage.Resetting)]
    [TestCase(LobbyTutorialActivity.ResetPosition, TrainingSparringStage.VulnerableCc)]
    [TestCase(LobbyTutorialActivity.Wall, TrainingSparringStage.Resetting)]
    [TestCase(LobbyTutorialActivity.SuppressKill, TrainingSparringStage.Wall)]
    [TestCase(LobbyTutorialActivity.Counter, TrainingSparringStage.Counter)]
    [TestCase(LobbyTutorialActivity.SuppressKill, TrainingSparringStage.SuppressKill)]
    public void 넉백_차례가_아니면_창이_닫힌_동안_표시_없음(LobbyTutorialActivity activity, TrainingSparringStage stage)
    {
        Assert.That(LobbyCounterPromptRules.PromptActive(activity, stage, false, false), Is.False);
    }

    [TestCase(LobbyTutorialActivity.Counter, TrainingSparringStage.Counter)]
    [TestCase(LobbyTutorialActivity.SuppressKill, TrainingSparringStage.SuppressKill)]
    public void T31_T40_은_창이_열리면_표시(LobbyTutorialActivity activity, TrainingSparringStage stage)
    {
        Assert.That(LobbyCounterPromptRules.PromptActive(activity, stage, true, false), Is.True);
    }

    [Test]
    public void T34_빗나감_뒤_다시_켜지면_펄스도_다시()
    {
        var pushed = LobbyCounterPromptRules.PromptActive(LobbyTutorialActivity.Wall, TrainingSparringStage.Wall, false, true);
        var restored = LobbyCounterPromptRules.PromptActive(LobbyTutorialActivity.Wall, TrainingSparringStage.Wall, false, false);
        Assert.That(LobbyCounterPromptRules.PulseStarts(pushed, restored), Is.True);
    }

    [Test]
    public void 간파_암전_중엔_숨기고_풀려도_펄스는_다시_시작하지_않는다()
    {
        Assert.That(LobbyCounterPromptRules.Visible(true, true), Is.False, "A5 숨김");
        Assert.That(LobbyCounterPromptRules.Visible(true, false), Is.True);
        Assert.That(LobbyCounterPromptRules.Visible(false, false), Is.False);

        Assert.That(LobbyCounterPromptRules.PulseStarts(false, true), Is.True, "창이 열리는 순간");
        Assert.That(LobbyCounterPromptRules.PulseStarts(true, true), Is.False, "창이 열린 채 A5 만 풀림");
        Assert.That(LobbyCounterPromptRules.PulseStarts(true, false), Is.False);
    }

    [Test]
    public void 펄스는_1에서_0으로_줄어든다()
    {
        Assert.That(LobbyCounterPromptRules.Pulse(0f, 0.4f), Is.EqualTo(1f));
        Assert.That(LobbyCounterPromptRules.Pulse(0.2f, 0.4f), Is.EqualTo(0.25f).Within(1e-5f));
        Assert.That(LobbyCounterPromptRules.Pulse(0.4f, 0.4f), Is.EqualTo(0f));
        Assert.That(LobbyCounterPromptRules.Pulse(1f, 0.4f), Is.EqualTo(0f));
        Assert.That(LobbyCounterPromptRules.Pulse(0f, 0f), Is.EqualTo(0f), "길이 0 = 펄스 없음");
    }

    [Test]
    public void 문구는_한국어_우클릭_영어_RMB()
    {
        Assert.That(LobbyTutorialUiStrings.CounterPrompt.Ko, Is.EqualTo("우클릭!"));
        Assert.That(LobbyTutorialUiStrings.CounterPromptFallback(GameLanguage.Ko), Is.EqualTo("우클릭!"));
        Assert.That(LobbyTutorialUiStrings.CounterPromptFallback(GameLanguage.En), Is.EqualTo("RMB!"));
        Assert.That(LobbyTutorialUiStrings.TryGet(LobbyTutorialUiStrings.CounterPrompt.Key, out _), Is.True, "Export 목록에 있다");
    }

    [Test]
    public void 설정_에셋이_Resources_에_있고_이미지가_연결돼_있다()
    {
        var settings = Resources.Load<LobbyCounterPromptSettings>(LobbyCounterPromptSettings.ResourcePath);
        Assert.That(settings, Is.Not.Null);
        Assert.That(settings.MouseIcon, Is.Not.Null, "50.Art Input_RMB — SVN 업데이트 확인");
        Assert.That(settings.RightButtonGlow, Is.Not.Null);
        Assert.That(settings.Font, Is.Not.Null);

        // 빛 스프라이트는 아이콘보다 사방 20px(원본 기준) 크고 중심이 같아야 버튼 모양이 겹친다.
        Vector2 icon = settings.MouseIcon.bounds.size;
        Vector2 glow = settings.RightButtonGlow.bounds.size;
        Assert.That(glow.x / icon.x, Is.EqualTo(100f / 60f).Within(1e-3f));
        Assert.That(glow.y / icon.y, Is.EqualTo(122f / 82f).Within(1e-3f));
        Assert.That(AssetDatabase.GetAssetPath(settings.MouseIcon), Does.EndWith("Input_RMB.png"));
    }
}
