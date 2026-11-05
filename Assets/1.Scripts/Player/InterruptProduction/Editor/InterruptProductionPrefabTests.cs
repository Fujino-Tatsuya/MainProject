using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 간파 연출 디렉터 프리팹 배치(할 일 118 C2) → 슬로우 3단계 SO 이관(194 후속 2).
/// 슬로우 프로필·줌이 SO(<see cref="InterruptSlowMotionSettings"/>)로 옮겨져 디렉터가 SO 의 맞는 칸을 고르고(코드 기본값 = 이관 당시 값, 에셋 값은 튜닝값),
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

    // 에셋 값은 튜닝값이라 숫자를 단언하지 않는다(11-03 사용자 지시 중립값 — 배율 1·시간 0·줌 1).
    // 대신 저장된 값이 정리(Clamped*) 범위 안이라 타임라인·줌에 그대로 들어간다는 것만 본다.
    private static void AssertProfileInRange(SlowMotionProfile profile, string name)
    {
        Assert.IsNotNull(profile, name);
        Assert.AreEqual(profile.ClampedScale, profile.scale, 1e-6f, name + ".scale");
        Assert.AreEqual(profile.ClampedHitStopDuration, profile.hitStopDuration, 1e-6f, name + ".hitStopDuration");
        Assert.AreEqual(profile.ClampedHitStopScale, profile.hitStopScale, 1e-6f, name + ".hitStopScale");
        Assert.AreEqual(profile.ClampedEnterDuration, profile.enterDuration, 1e-6f, name + ".enterDuration");
        Assert.AreEqual(profile.ClampedHoldDuration, profile.holdDuration, 1e-6f, name + ".holdDuration");
        Assert.AreEqual(profile.ClampedExitDuration, profile.exitDuration, 1e-6f, name + ".exitDuration");
        Assert.AreEqual(profile.ClampedFailExitDuration, profile.failExitDuration, 1e-6f, name + ".failExitDuration");
    }

    [Test]
    public void 슬로우_SO_값이_정리_범위_안이다()
    {
        InterruptSlowMotionSettings settings = LoadSettings();
        AssertProfileInRange(settings.InterruptDashProfile, "interruptDashProfile");
        AssertProfileInRange(settings.InterruptDashWeakProfile, "interruptDashWeakProfile");
        AssertProfileInRange(settings.InterruptProfile, "interruptProfile");
        AssertProfileInRange(settings.InterruptWeakProfile, "interruptWeakProfile");
        AssertProfileInRange(settings.InterruptSuccessProfile, "interruptSuccessProfile");
        AssertProfileInRange(settings.InterruptSuppressSuccessProfile, "interruptSuppressSuccessProfile"); // 할 일 219
        Assert.AreEqual(SlowMotionCameraZoom.ClampZoomFactor(settings.InterruptZoomFactor), settings.InterruptZoomFactor, 1e-6f);
        Assert.AreEqual(SlowMotionCameraZoom.ClampZoomFactor(settings.InterruptWeakZoomFactor), settings.InterruptWeakZoomFactor, 1e-6f);
        Assert.AreEqual(SlowMotionCameraZoom.ClampZoomFactor(settings.SlowMotionZoomFactor), settings.SlowMotionZoomFactor, 1e-6f);
    }

    // 코드 기본값(새 SO)은 이관 당시 값(= xlsx SlowMotion 시트 값) 그대로다 — 에셋 튜닝과 별개.
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

    // 디렉터 프리팹이 시도·성공마다 SO 의 맞는 칸을 고른다(대쉬 시도 → 대쉬 강·약 칸, 근접 → 근접 강·약 칸). 값은 SO 튜닝값이라 단언하지 않는다.
    [Test]
    public void 프리팹_디렉터가_슬로우_SO_의_맞는_프로필_칸을_고른다()
    {
        var director = LoadPrefab().GetComponent<InterruptProductionDirector>();
        InterruptSlowMotionSettings settings = director.SlowMotion;
        Assert.AreSame(settings.InterruptDashProfile,
            InterruptProductionRules.SelectAttemptProfile(SlowMotionPurpose.Attempt, InterruptVariant.Dash, director), "대쉬 강");
        Assert.AreSame(settings.InterruptDashWeakProfile,
            InterruptProductionRules.SelectAttemptProfile(SlowMotionPurpose.AttemptWeak, InterruptVariant.Dash, director), "대쉬 약");
        Assert.AreSame(settings.InterruptProfile,
            InterruptProductionRules.SelectAttemptProfile(SlowMotionPurpose.Attempt, InterruptVariant.Normal, director), "근접 강");
        Assert.AreSame(settings.InterruptWeakProfile,
            InterruptProductionRules.SelectAttemptProfile(SlowMotionPurpose.AttemptWeak, InterruptVariant.Normal, director), "근접 약");
        Assert.AreSame(settings.InterruptSuccessProfile, director.InterruptSuccessProfile, "성공");
        Assert.AreSame(settings.InterruptSuppressSuccessProfile, director.InterruptSuppressSuccessProfile, "제압 성공(할 일 219)");
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
