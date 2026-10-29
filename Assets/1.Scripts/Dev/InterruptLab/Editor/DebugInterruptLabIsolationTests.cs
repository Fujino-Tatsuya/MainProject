using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 간파 디버그 씬 기능(할 일 115 C6b)이 랩 씬(DebugInterruptLab·InterruptCalibrationLab) 밖에 새지 않는다는 증명.
/// 씬·프리팹은 열지 않고 YAML 텍스트에서 디버그 스크립트 guid 를 찾는다 — 에디터 씬 상태를 건드리지 않는다.
/// 게임 코드 훅은 디버그 컴포넌트가 없을 때(기본 배속 1·구독자 없음) 예전 동작과 같아야 한다.
/// </summary>
public sealed class DebugInterruptLabIsolationTests
{
    const float Eps = 1e-5f;
    const string LabScenePath = "Assets/0.Scenes/Debug/DebugInterruptLab.unity";
    const string CalibrationLabScenePath = "Assets/0.Scenes/Debug/InterruptCalibrationLab.unity"; // 139 예측 보정 랩
    // 특성 디버그 랩(할 일 135)도 배속·쿨 0·F1·허수아비 회복을 재사용한다 — 그 씬도 허용(자기 격리는 DebugTraitLabIsolationTests).
    const string TraitLabScenePath = "Assets/0.Scenes/Debug/DebugTraitLab.unity";
    static readonly string[] LabScenePaths = { LabScenePath, CalibrationLabScenePath, TraitLabScenePath };
    const string LabFolder = "Assets/1.Scripts/Dev/InterruptLab/";

    static readonly System.Type[] DebugComponents =
    {
        typeof(DebugTimeScale),
        typeof(DebugZeroCooldown),
        typeof(DebugCharacterCycle),
        typeof(DebugDummyInterruptRecovery),
        typeof(DebugInterruptTuningPanel), // 132 §17 튜닝 패널
        typeof(DebugDummyInterruptWindowMode), // 139 허수아비 창 모드
        typeof(InterruptCalibrationCapture), // 139 예측 보정 캡처(랩 씬은 단계 5)
        typeof(InterruptCalibrationQuestionPanel), // 139 질문 창·상시 패널(랩 씬은 단계 5)
    };

    static string[] DebugScriptGuids()
    {
        return DebugComponents.Select(t =>
        {
            string path = AssetDatabase.FindAssets($"t:MonoScript {t.Name}")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Single(p => Path.GetFileNameWithoutExtension(p) == t.Name);
            return AssetDatabase.AssetPathToGUID(path);
        }).ToArray();
    }

    static string[] FilesUsingDebugScripts(System.Collections.Generic.IEnumerable<string> assetPaths)
    {
        string[] guids = DebugScriptGuids();
        return assetPaths
            .Where(p => { string text = File.ReadAllText(p); return guids.Any(g => text.Contains("guid: " + g)); })
            .ToArray();
    }

    static string Normalize(string path) => path.Replace('\\', '/');

    // ─── (a) 씬·프리팹·빌드 목록 ───────────────────────────────────────

    [Test]
    public void 디버그_스크립트_guid_를_모두_찾는다()
    {
        string[] guids = DebugScriptGuids();
        Assert.AreEqual(DebugComponents.Length, guids.Distinct().Count());
        Assert.IsTrue(guids.All(g => !string.IsNullOrEmpty(g)));
        // 검색이 실제로 잡는지 — 랩 씬 둘에는 붙어 있어야 한다
        Assert.AreEqual(LabScenePaths.Length, FilesUsingDebugScripts(LabScenePaths).Length);
    }

    [TestCase(LabScenePath)]
    [TestCase(CalibrationLabScenePath)]
    public void 튜닝_패널은_랩_씬에서_배속과_같은_오브젝트에_붙어_있다(string scenePath)
    {
        // 패널은 같은 오브젝트의 DebugTimeScale 을 배속으로 쓴다(132 §17) — 씬 YAML 에서 두 컴포넌트의 m_GameObject 가 같아야 한다.
        string text = File.ReadAllText(scenePath);
        string GameObjectOf(System.Type type)
        {
            string guid = DebugScriptGuids()[System.Array.IndexOf(DebugComponents, type)];
            Match match = Regex.Match(text, @"m_GameObject: \{fileID: (\d+)\}\s+m_Enabled: 1\s+m_EditorHideFlags: 0\s+m_Script: \{fileID: 11500000, guid: " + guid);
            Assert.IsTrue(match.Success, type.Name + " 가 랩 씬에 켜진 채 붙어 있어야 한다");
            return match.Groups[1].Value;
        }

        Assert.AreEqual(GameObjectOf(typeof(DebugTimeScale)), GameObjectOf(typeof(DebugInterruptTuningPanel)));
    }

    [Test]
    public void 보정_랩_씬은_허수아비_하나에_창_모드를_달고_캡처와_질문_패널이_그것을_가리킨다()
    {
        // 139 단계 5 — 캡처·패널은 창 모드를 비우면 씬에서 찾는다(허수아비 하나 전제). 씬에서는 명시 연결하고 허수아비를 하나만 둔다.
        string[] blocks = Regex.Split(File.ReadAllText(CalibrationLabScenePath), @"(?m)^--- !u!");
        string[] BlocksOf(System.Type type)
        {
            string guid = DebugScriptGuids()[System.Array.IndexOf(DebugComponents, type)];
            return blocks.Where(b => b.Contains("m_Script: {fileID: 11500000, guid: " + guid)).ToArray();
        }
        string FileIdOf(string block) => Regex.Match(block, @"^114 &(\d+)").Groups[1].Value;
        string RefOf(string block, string field) => Regex.Match(block, @"\n  " + field + @": \{fileID: (\d+)\}").Groups[1].Value;

        Assert.AreEqual(1, BlocksOf(typeof(DebugDummyInterruptRecovery)).Length, "허수아비는 하나");
        string[] modes = BlocksOf(typeof(DebugDummyInterruptWindowMode));
        string[] captures = BlocksOf(typeof(InterruptCalibrationCapture));
        string[] panels = BlocksOf(typeof(InterruptCalibrationQuestionPanel));
        Assert.AreEqual(1, modes.Length);
        Assert.AreEqual(1, captures.Length);
        Assert.AreEqual(1, panels.Length);
        Assert.IsTrue(Regex.IsMatch(modes[0], @"\n  mode: 0\b"), "기본 = 무기한");

        string modeId = FileIdOf(modes[0]);
        Assert.IsNotEmpty(modeId);
        Assert.AreEqual(modeId, RefOf(captures[0], "windowMode"));
        Assert.AreEqual(modeId, RefOf(panels[0], "windowMode"));
        Assert.AreEqual(FileIdOf(captures[0]), RefOf(panels[0], "capture"));
    }

    [Test]
    public void 빌드_목록에_디버그_씬이_없다()
    {
        string[] buildScenes = EditorBuildSettings.scenes.Select(s => Normalize(s.path)).ToArray();
        foreach (string lab in LabScenePaths)
            CollectionAssert.DoesNotContain(buildScenes, lab);
        Assert.IsFalse(buildScenes.Any(p => p.Contains("DebugInterruptLab") || p.Contains("InterruptCalibrationLab")));
    }

    [Test]
    public void 빌드_목록_씬에_디버그_컴포넌트가_없다()
    {
        string[] buildScenes = EditorBuildSettings.scenes
            .Select(s => Normalize(s.path))
            .Where(File.Exists)
            .ToArray();
        Assert.IsNotEmpty(buildScenes);
        CollectionAssert.IsEmpty(FilesUsingDebugScripts(buildScenes));
    }

    [Test]
    public void 디버그_씬_밖의_모든_씬에_디버그_컴포넌트가_없다()
    {
        string[] scenes = Directory.GetFiles("Assets", "*.unity", SearchOption.AllDirectories)
            .Select(Normalize)
            .Where(p => !LabScenePaths.Contains(p))
            .ToArray();
        Assert.IsTrue(scenes.Any(p => p.StartsWith("Assets/0.Scenes/")));
        CollectionAssert.IsEmpty(FilesUsingDebugScripts(scenes));
    }

    [Test]
    public void 모든_프리팹에_디버그_컴포넌트가_없다()
    {
        string[] prefabs = Directory.GetFiles("Assets", "*.prefab", SearchOption.AllDirectories)
            .Select(Normalize)
            .ToArray();
        Assert.IsNotEmpty(prefabs);
        CollectionAssert.IsEmpty(FilesUsingDebugScripts(prefabs));
    }

    // ─── (b) 기본 배속 1 = 기존 규칙 ───────────────────────────────────

    [TestCase(1f)]
    [TestCase(0.75f)]
    [TestCase(0.5f)]
    [TestCase(0.25f)]
    [TestCase(0.125f)]
    [TestCase(0.0625f)]
    [TestCase(0.01f)]
    public void 기본_배속_1이면_최종_배율은_슬로우_배율_그대로(float slowScale)
    {
        Assert.AreEqual(slowScale, GlobalTimeScale.ComposeScale(slowScale, 1f), Eps);
    }

    [TestCase(true, ExpectedResult = true)]
    [TestCase(false, ExpectedResult = false)]
    public bool 기본_배속_1이면_레이어는_슬로우_진행_중일_때만_켜진다(bool slowActive)
    {
        return GlobalTimeScale.IsLayerActive(slowActive, 1f);
    }

    // ─── (c) 기본값 ────────────────────────────────────────────────────

    [Test]
    public void 게임_NetworkManager_프리팹의_GlobalTimeScale_기본_배속은_1()
    {
        // [RequireComponent(NetworkManager)] 라 AddComponent 하면 NetworkManager.Reset 훅·씬 더럽힘이 따라온다 —
        // 열지 않은 프리팹 에셋의 컴포넌트(직렬화 안 되는 필드 = 초기값)로 본다. Awake 는 돌지 않는다.
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2.Prefabs/Network/NetworkManager.prefab");
        Assert.IsNotNull(prefab);
        var timeScale = prefab.GetComponent<GlobalTimeScale>();
        Assert.IsNotNull(timeScale);
        Assert.AreEqual(1f, timeScale.BaseScale, Eps);
    }

    // ─── (d) 성공 이벤트 구독자 ────────────────────────────────────────

    [Test]
    public void 간파_성공_로컬_이벤트는_디버그_폴더_밖에서_구독하지_않는다()
    {
        var subscribe = new Regex(@"\bLocalSucceeded\s*\+=");
        string[] subscribers = Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories)
            .Select(Normalize)
            .Where(p => !p.StartsWith(LabFolder))
            .Where(p => subscribe.IsMatch(File.ReadAllText(p)))
            .ToArray();
        CollectionAssert.IsEmpty(subscribers);

        // 검색이 실제로 잡는지 — 디버그 배속은 구독한다
        Assert.IsTrue(subscribe.IsMatch(File.ReadAllText(LabFolder + "DebugTimeScale.cs")));
    }

    // ─── (e) 허수아비 창 게이트(139) ───────────────────────────────────

    [Test]
    public void 허수아비_창_게이트는_디버그_폴더_밖에서_걸지_않는다()
    {
        var assign = new Regex(@"\bServerWindowGate\s*=(?!=)");
        string[] assigners = Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories)
            .Select(Normalize)
            .Where(p => !p.StartsWith(LabFolder))
            .Where(p => assign.IsMatch(File.ReadAllText(p)))
            .ToArray();
        CollectionAssert.IsEmpty(assigners);

        // 검색이 실제로 잡는지 — 랩 창 모드는 건다
        Assert.IsTrue(assign.IsMatch(File.ReadAllText(LabFolder + "DebugDummyInterruptWindowMode.cs")));
    }

    // ─── (f) 예측 보정 계측 훅(139 단계 3) ─────────────────────────────

    [Test]
    public void 예측_보정_계측_훅은_디버그_폴더_밖에서_구독하지_않는다()
    {
        var subscribe = new Regex(@"\bCalibration(OwnerPredicted|ServerPredicted|OwnerRepredicted|ServerRepredicted|ServerJudged)\s*\+=");
        string[] subscribers = Directory.GetFiles("Assets", "*.cs", SearchOption.AllDirectories)
            .Select(Normalize)
            .Where(p => !p.StartsWith(LabFolder))
            .Where(p => subscribe.IsMatch(File.ReadAllText(p)))
            .ToArray();
        CollectionAssert.IsEmpty(subscribers);

        // 검색이 실제로 잡는지 — 캡처기는 다섯 다 구독한다(재예측 둘 = 할 일 139-2)
        string capture = File.ReadAllText(LabFolder + "Calibration/InterruptCalibrationCapture.cs");
        Assert.AreEqual(5, subscribe.Matches(capture).Count);
    }
}
