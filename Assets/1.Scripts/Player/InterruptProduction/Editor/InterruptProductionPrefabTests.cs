using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 간파 연출 디렉터 프리팹 배치(할 일 118 C2) → 슬로우 3단계 SO 이관(194 후속 2).
/// 슬로우 프로필·줌이 SO(<see cref="InterruptSlowMotionSettings"/>)로 옮겨져 값이 옛 프리팹 값 그대로이고(동작 변화 0),
/// 디렉터·GlobalTimeScale·CameraTargetSwitcher 에 같은 경로가 남지 않아(데이터 테이블 바인딩 모호 방지) 옛 스위치 타입·Missing Script 가 없다.
/// </summary>
public sealed class InterruptProductionPrefabTests
{
    private const string NetworkManagerPrefabPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";
    private const string CameraSwitcherPrefabPath = "Assets/2.Prefabs/Camera/CameraSwitcher.prefab";
    private const string SlowMotionSettingsPath = "Assets/5.VFX/Interrupt/HitLight/V3/InterruptSlowMotionSettings.asset";

    private static readonly string[] MovedFields =
    {
        "interruptProfile", "interruptWeakProfile", "interruptZoomFactor", "interruptWeakZoomFactor", "interruptSuccessProfile",
    };

    private static GameObject LoadPrefab(string path = NetworkManagerPrefabPath)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.IsNotNull(prefab, path);
        return prefab;
    }

    private static InterruptSlowMotionSettings LoadSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<InterruptSlowMotionSettings>(SlowMotionSettingsPath);
        Assert.IsNotNull(settings, SlowMotionSettingsPath);
        return settings;
    }

    private static void AssertProfile(SerializedProperty profile, float scale, float hitStopDuration, float hitStopScale,
        float enterDuration, float holdDuration, float exitDuration, float failExitDuration)
    {
        Assert.IsNotNull(profile);
        string path = profile.propertyPath;
        Assert.AreEqual(scale, profile.FindPropertyRelative("scale").floatValue, 1e-6f, path + ".scale");
        Assert.AreEqual(hitStopDuration, profile.FindPropertyRelative("hitStopDuration").floatValue, 1e-6f, path + ".hitStopDuration");
        Assert.AreEqual(hitStopScale, profile.FindPropertyRelative("hitStopScale").floatValue, 1e-6f, path + ".hitStopScale");
        Assert.AreEqual(enterDuration, profile.FindPropertyRelative("enterDuration").floatValue, 1e-6f, path + ".enterDuration");
        Assert.AreEqual(holdDuration, profile.FindPropertyRelative("holdDuration").floatValue, 1e-6f, path + ".holdDuration");
        Assert.AreEqual(exitDuration, profile.FindPropertyRelative("exitDuration").floatValue, 1e-6f, path + ".exitDuration");
        Assert.AreEqual(failExitDuration, profile.FindPropertyRelative("failExitDuration").floatValue, 1e-6f, path + ".failExitDuration");
    }

    // 이관 당시 NetworkManager.prefab·CameraSwitcher.prefab 값(= xlsx SlowMotion 시트 값). 대쉬 = 강·약 복사.
    private static void AssertMigratedValues(SerializedObject so)
    {
        AssertProfile(so.FindProperty("interruptDashProfile"), 0.15f, 0f, 0.02f, 0.05f, 0.5f, 0.25f, 0.1f);
        AssertProfile(so.FindProperty("interruptDashWeakProfile"), 0.5f, 0f, 0.02f, 0.05f, 0.2f, 0.25f, 0.1f);
        AssertProfile(so.FindProperty("interruptProfile"), 0.15f, 0f, 0.02f, 0.05f, 0.5f, 0.25f, 0.1f);
        AssertProfile(so.FindProperty("interruptWeakProfile"), 0.5f, 0f, 0.02f, 0.05f, 0.2f, 0.25f, 0.1f);
        Assert.AreEqual(0.7f, so.FindProperty("interruptZoomFactor").floatValue, 1e-6f);
        Assert.AreEqual(0.93f, so.FindProperty("interruptWeakZoomFactor").floatValue, 1e-6f);
        AssertProfile(so.FindProperty("interruptSuccessProfile"), 0.25f, 0.05f, 0.02f, 0.05f, 1.2f, 0.4f, 0.1f);
        Assert.AreEqual(0.85f, so.FindProperty("slowMotionZoomFactor").floatValue, 1e-6f);
    }

    [Test]
    public void 슬로우_SO_값이_옛_프리팹_값이다()
    {
        AssertMigratedValues(new SerializedObject(LoadSettings()));
    }

    [Test]
    public void 새_슬로우_SO_기본값도_같다()
    {
        var settings = ScriptableObject.CreateInstance<InterruptSlowMotionSettings>();
        try
        {
            AssertMigratedValues(new SerializedObject(settings));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(settings);
        }
    }

    [Test]
    public void 디렉터와_카메라가_같은_슬로우_SO_를_참조한다()
    {
        InterruptSlowMotionSettings settings = LoadSettings();
        var director = LoadPrefab().GetComponent<InterruptProductionDirector>();
        Assert.IsNotNull(director, "InterruptProductionDirector 없음");
        Assert.AreSame(settings, new SerializedObject(director).FindProperty("slowMotionSettings").objectReferenceValue);
        Assert.AreSame(settings, director.SlowMotion);

        var camera = LoadPrefab(CameraSwitcherPrefabPath).GetComponent<CameraTargetSwitcher>();
        Assert.IsNotNull(camera, "CameraTargetSwitcher 없음");
        Assert.AreSame(settings, new SerializedObject(camera).FindProperty("slowMotionSettings").objectReferenceValue);
    }

    // 동작 변화 0 — 디렉터 프리팹이 고르는 시도·성공 프로필 값이 이관 전과 같다(대쉬 시도 = 근접 강·약과 같은 값).
    [Test]
    public void 프리팹_디렉터가_고르는_프로필_값이_이관_전과_같다()
    {
        var director = LoadPrefab().GetComponent<InterruptProductionDirector>();
        foreach (InterruptVariant variant in new[] { InterruptVariant.Normal, InterruptVariant.Dash })
        {
            SlowMotionProfile strong = InterruptProductionRules.SelectAttemptProfile(SlowMotionPurpose.Attempt, variant, director);
            SlowMotionProfile weak = InterruptProductionRules.SelectAttemptProfile(SlowMotionPurpose.AttemptWeak, variant, director);
            Assert.AreEqual(0.15f, strong.scale, 1e-6f, variant + " 강 scale");
            Assert.AreEqual(0.5f, strong.holdDuration, 1e-6f, variant + " 강 hold");
            Assert.AreEqual(0.5f, weak.scale, 1e-6f, variant + " 약 scale");
            Assert.AreEqual(0.2f, weak.holdDuration, 1e-6f, variant + " 약 hold");
        }

        Assert.AreSame(director.SlowMotion.InterruptDashProfile,
            InterruptProductionRules.SelectAttemptProfile(SlowMotionPurpose.Attempt, InterruptVariant.Dash, director));
        Assert.AreSame(director.SlowMotion.InterruptProfile,
            InterruptProductionRules.SelectAttemptProfile(SlowMotionPurpose.Attempt, InterruptVariant.Normal, director));
        Assert.AreEqual(1.2f, director.InterruptSuccessProfile.holdDuration, 1e-6f);
        Assert.AreEqual(0.05f, director.InterruptSuccessProfile.hitStopDuration, 1e-6f);
    }

    [Test]
    public void 옛_자리에는_슬로우_경로가_없다()
    {
        var prefab = LoadPrefab();
        var director = new SerializedObject(prefab.GetComponent<InterruptProductionDirector>());
        var timeScale = new SerializedObject(prefab.GetComponent<GlobalTimeScale>());
        foreach (string field in MovedFields)
        {
            Assert.IsNull(director.FindProperty(field), "디렉터 " + field + " — 같은 이름이 남으면 데이터 테이블이 모호");
            Assert.IsNull(timeScale.FindProperty(field), "GlobalTimeScale " + field);
        }

        var camera = new SerializedObject(LoadPrefab(CameraSwitcherPrefabPath).GetComponent<CameraTargetSwitcher>());
        Assert.IsNull(camera.FindProperty("slowMotionZoomFactor"));
    }

    [Test]
    public void 테이블_시트는_슬로우_SO_만_SlowMotion_0번()
    {
        var sheet = (DataTableSheetAttribute)Attribute.GetCustomAttribute(typeof(InterruptSlowMotionSettings), typeof(DataTableSheetAttribute));
        Assert.IsNotNull(sheet);
        Assert.AreEqual("SlowMotion", sheet.Sheet);
        Assert.AreEqual(0, sheet.Order);
        // 디렉터·카메라는 테이블 칸이 없어 시트에서 빠진다(194 후속 2).
        Assert.IsNull(Attribute.GetCustomAttribute(typeof(InterruptProductionDirector), typeof(DataTableSheetAttribute)));
        Assert.IsNull(Attribute.GetCustomAttribute(typeof(CameraTargetSwitcher), typeof(DataTableSheetAttribute)));
        Assert.IsNull(Attribute.GetCustomAttribute(typeof(GlobalTimeScale), typeof(DataTableSheetAttribute)));
    }

    [Test]
    public void 옛_스위치_타입이_없고_Missing_Script_0()
    {
        Assert.IsNull(Type.GetType("InterruptPresentationToggles, Assembly-CSharp"));
        Assert.IsNull(Type.GetType("InterruptPresentationKind, Assembly-CSharp"));
        Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(LoadPrefab()));
    }
}
