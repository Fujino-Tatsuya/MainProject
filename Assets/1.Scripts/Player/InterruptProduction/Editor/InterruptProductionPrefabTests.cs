using System;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 간파 연출 디렉터 프리팹 배치(할 일 118 C2) — 슬로우 프로필이 디렉터로 옮겨져 값이 그대로이고,
/// GlobalTimeScale 에 같은 경로가 남지 않아(데이터 테이블 바인딩 모호 방지) 옛 스위치 타입·Missing Script 가 없다.
/// </summary>
public sealed class InterruptProductionPrefabTests
{
    private const string NetworkManagerPrefabPath = "Assets/2.Prefabs/Network/NetworkManager.prefab";

    private static GameObject LoadPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkManagerPrefabPath);
        Assert.IsNotNull(prefab, NetworkManagerPrefabPath);
        return prefab;
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

    [Test]
    public void 디렉터_프로필_값이_132_강_약_처음_값이다()
    {
        var director = LoadPrefab().GetComponent<InterruptProductionDirector>();
        Assert.IsNotNull(director, "InterruptProductionDirector 없음");
        var so = new SerializedObject(director);
        // 강 = 옛 시도 자리(interruptProfile), 약 = 새 칸 — 나머지 칸은 옛 시도 값 그대로(할 일 132)
        AssertProfile(so.FindProperty("interruptProfile"), 0.15f, 0f, 0.02f, 0.05f, 0.5f, 0.25f, 0.1f);
        AssertProfile(so.FindProperty("interruptWeakProfile"), 0.5f, 0f, 0.02f, 0.05f, 0.2f, 0.25f, 0.1f);
        Assert.AreEqual(0.7f, so.FindProperty("interruptZoomFactor").floatValue, 1e-6f);
        Assert.AreEqual(0.93f, so.FindProperty("interruptWeakZoomFactor").floatValue, 1e-6f);
        AssertProfile(so.FindProperty("interruptSuccessProfile"), 0.25f, 0.05f, 0.02f, 0.05f, 1.2f, 0.4f, 0.1f);
    }

    [Test]
    public void 새_디렉터_기본값도_같다()
    {
        var go = new GameObject("Director");
        try
        {
            var so = new SerializedObject(go.AddComponent<InterruptProductionDirector>());
            AssertProfile(so.FindProperty("interruptProfile"), 0.15f, 0f, 0.02f, 0.05f, 0.5f, 0.25f, 0.1f);
            AssertProfile(so.FindProperty("interruptWeakProfile"), 0.5f, 0f, 0.02f, 0.05f, 0.2f, 0.25f, 0.1f);
            AssertProfile(so.FindProperty("interruptSuccessProfile"), 0.25f, 0.05f, 0.02f, 0.05f, 1.2f, 0.4f, 0.1f);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
        }
    }

    [Test]
    public void GlobalTimeScale_에는_프로필_경로가_없다()
    {
        var timeScale = LoadPrefab().GetComponent<GlobalTimeScale>();
        Assert.IsNotNull(timeScale);
        var so = new SerializedObject(timeScale);
        Assert.IsNull(so.FindProperty("interruptProfile"), "같은 대상에 같은 경로가 둘이면 데이터 테이블이 모호 오류");
        Assert.IsNull(so.FindProperty("interruptSuccessProfile"));
        Assert.IsNull(so.FindProperty("interruptWeakProfile"));
    }

    [Test]
    public void 테이블_시트는_디렉터만_SlowMotion_0번()
    {
        var sheet = (DataTableSheetAttribute)Attribute.GetCustomAttribute(typeof(InterruptProductionDirector), typeof(DataTableSheetAttribute));
        Assert.IsNotNull(sheet);
        Assert.AreEqual("SlowMotion", sheet.Sheet);
        Assert.AreEqual(0, sheet.Order);
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
