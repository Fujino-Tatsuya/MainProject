using NUnit.Framework;
using UnityEngine;

// 할 일 276 — 로비 상단 플레이어 슬롯 A안 상태 규칙(순수 함수).
public sealed class LobbyPlayerSlotViewTests
{
    [TestCase(false, false, LobbySlotState.Disconnected)]
    [TestCase(false, true, LobbySlotState.Disconnected)]
    [TestCase(true, false, LobbySlotState.Waiting)]
    [TestCase(true, true, LobbySlotState.Ready)]
    public void StateOf_MapsConnectedAndReady(bool connected, bool ready, LobbySlotState expected)
    {
        Assert.That(LobbyPlayerSlotView.StateOf(connected, ready), Is.EqualTo(expected));
    }

    // 준비 완료 = 브리핑 끝(출격 집결) + 출격 구역 안 + 쓰러지지 않음. 출격 확정 뒤는 모두 준비 완료.
    [TestCase(false, false, false, false, false, TestName = "브리핑 중 = 준비 중")]
    [TestCase(false, false, true, false, false, TestName = "브리핑 중 구역 안이어도 준비 중")]
    [TestCase(false, true, false, false, false, TestName = "출격 집결 중 구역 밖 = 준비 중")]
    [TestCase(false, true, true, false, true, TestName = "출격 집결 중 구역 안 = 준비 완료")]
    [TestCase(false, true, true, true, false, TestName = "구역 안이어도 쓰러짐 = 준비 중")]
    [TestCase(true, false, false, true, true, TestName = "출격 확정 뒤 = 준비 완료")]
    public void SlotReadyRules(bool launchCommitted, bool sortieGatherActive, bool inZone, bool incapacitated, bool expected)
    {
        Assert.That(LobbySlotReadyRules.IsReady(launchCommitted, sortieGatherActive, inZone, incapacitated), Is.EqualTo(expected));
    }

    [Test]
    public void StatusString_KeysAndKoreanFallbacks()
    {
        Assert.That(LobbyPlayerSlotView.StatusString(LobbySlotState.Ready).Ko, Is.EqualTo("준비 완료"));
        Assert.That(LobbyPlayerSlotView.StatusString(LobbySlotState.Waiting).Ko, Is.EqualTo("준비 중"));
        Assert.That(LobbyPlayerSlotView.StatusString(LobbySlotState.Disconnected).Ko, Is.EqualTo("미접속"));
        Assert.That(LobbyUiStrings.All, Has.Member(LobbyUiStrings.SlotEmptyName));
    }

    // assembly.json 아이콘 위치 − 캡슐 위치(0.402, -131.4225), 크기 = 디자인 sizeDelta
    [Test]
    public void StatusIconLayout_MatchesAssembly()
    {
        var capsule = new Vector2(0.402f, -131.4225f);
        var check = LobbyPlayerSlotView.StatusIconLayout(LobbySlotState.Ready);
        Assert.That(Vector2.Distance(check.position + capsule, new Vector2(-46.098f, -130.4225f)), Is.LessThan(0.6f));
        Assert.That(check.size, Is.EqualTo(new Vector2(22f, 19f)));
        var dot = LobbyPlayerSlotView.StatusIconLayout(LobbySlotState.Waiting);
        Assert.That(Vector2.Distance(dot.position + capsule, new Vector2(-47.598f, -131.4225f)), Is.LessThan(0.6f));
        Assert.That(dot.size, Is.EqualTo(new Vector2(11f, 11f)));
        var dash = LobbyPlayerSlotView.StatusIconLayout(LobbySlotState.Disconnected);
        Assert.That(dash.size, Is.EqualTo(new Vector2(15f, 7f)));
    }

    // 283 후속 — 슬롯 패널만 화면 크기에 맞춰 배율(Expand 규칙). 캔버스 크기는 캔버스 단위.
    [TestCase(1920f, 1080f, 1f)]
    [TestCase(1280f, 720f, 0.6666667f)]
    [TestCase(3840f, 2160f, 2f)]
    [TestCase(1920f, 1200f, 1f)]
    [TestCase(2560f, 1080f, 1f)]
    [TestCase(3440f, 1440f, 1.3333334f)]
    [TestCase(1024f, 768f, 0.5333334f)]
    public void ScreenFitScale_Factor_IsExpand(float w, float h, float expected)
    {
        Assert.That(ScreenFitScale.Factor(new Vector2(w, h), new Vector2(1920f, 1080f)), Is.EqualTo(expected).Within(1e-4f));
    }

    [Test]
    public void ScreenFitScale_Factor_ZeroSizeIsOne()
    {
        Assert.That(ScreenFitScale.Factor(Vector2.zero, new Vector2(1920f, 1080f)), Is.EqualTo(1f));
    }

    [Test]
    public void LobbyScene_SlotPanel_HasScreenFitScale()
    {
        const string path = "Assets/0.Scenes/MainFlow/3.LobbyScene.unity";
        var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
        bool openedHere = !scene.isLoaded;
        if (openedHere) scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
        try
        {
            LobbyUIController controller = null;
            foreach (var root in scene.GetRootGameObjects())
                if ((controller = root.GetComponentInChildren<LobbyUIController>(true)) != null) break;
            Assert.That(controller, Is.Not.Null);
            var fit = controller.GetComponent<ScreenFitScale>();
            Assert.That(fit, Is.Not.Null, "Panel_PlayerSlots 에 ScreenFitScale(저작 메뉴가 붙임)");
            Assert.That(fit.ReferenceResolution, Is.EqualTo(new Vector2(1920f, 1080f)));
            var rect = (RectTransform)controller.transform;
            Assert.That(rect.localScale.x, Is.EqualTo(LobbyPlayerSlotsAuthoring.RootScale).Within(1e-5f), "씬 값 = 1920×1080 기준 스케일");
            Assert.That(rect.anchorMin, Is.EqualTo(new Vector2(0.5f, 1f)));
        }
        finally
        {
            if (openedHere) UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
        }
    }
}
