using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 보스 처치 연출 백대쉬·마커 옵션(PLAN-boss-victory-glitch "255 후속 3") — 설정 SO 기본값이 보스 등장 밀림 값과 같은지,
/// 방향 보정 순수 함수(<see cref="BossVictoryBackDash.Direction"/>), 마커 옵션 기본값(즉시 컷·팀원만 = 지금 동작).
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictoryBackDash).
/// </summary>
public sealed class BossVictoryBackDashTests
{
    const string DirectorPrefab = "Assets/2.Prefabs/Managers/BossEncounterDirector.prefab";
    const float Tolerance = 1e-4f;

    static BossVictoryProfile NewSettings() => ScriptableObject.CreateInstance<BossVictoryProfile>();

    [Test]
    public void 백대쉬_기본_거리_시간은_보스_등장_밀림_값과_같다()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DirectorPrefab);
        Assert.IsNotNull(prefab, DirectorPrefab);
        var director = prefab.GetComponent<BossEncounterDirector>();
        Assert.IsNotNull(director, "BossEncounterDirector 컴포넌트");
        var s = NewSettings();
        try
        {
            Assert.AreEqual(director.IntroSkidDistance, s.backDashDistance, Tolerance, "거리");
            Assert.AreEqual(director.IntroSkidDuration, s.backDashDuration, Tolerance, "시간");
        }
        finally { Object.DestroyImmediate(s); }
    }

    [Test]
    public void 백대쉬_기본_곡선은_보스_등장_밀림_곡선과_같다()
    {
        var s = NewSettings();
        try
        {
            Assert.AreEqual(BossVictoryCameraCuts.Ease.EaseOut, s.backDashEase);
            Assert.AreEqual(0f, s.backDashAngle, Tolerance);
            for (int i = 0; i <= 20; i++)
            {
                float u = i / 20f;
                Assert.AreEqual(BossIntroFormation.SkidEase(u), BossVictoryCameraCuts.Evaluate(s.backDashEase, u), Tolerance, $"u={u}");
            }
        }
        finally { Object.DestroyImmediate(s); }
    }

    [Test]
    public void 마커_옵션_기본값은_지금_동작_즉시_컷_팀원만()
    {
        var s = ScriptableObject.CreateInstance<BossVictoryGlitchSettings>();
        try
        {
            Assert.AreEqual(0f, s.focusKillerBlendSeconds, "0 = 즉시 컷");
            Assert.AreEqual(BossVictoryGlitchSettings.HideOthersTarget.Teammates, s.hideOthersTarget);
        }
        finally { Object.DestroyImmediate(s); }
    }

    [Test]
    public void 방향_보정_0도는_보스_반대쪽_수평_단위벡터()
    {
        Vector3 d = BossVictoryBackDash.Direction(new Vector3(3f, 1f, 0f), new Vector3(0f, 5f, 0f), Vector3.forward, 0f);
        Assert.AreEqual(1f, d.x, Tolerance);
        Assert.AreEqual(0f, d.y, Tolerance);
        Assert.AreEqual(0f, d.z, Tolerance);
    }

    [Test]
    public void 방향_보정_양수는_위에서_봐_시계_방향()
    {
        // 반대쪽 = +Z, +90도 → +X(Unity Y축 회전 = 위에서 봐 시계 방향).
        Vector3 d = BossVictoryBackDash.Direction(new Vector3(0f, 0f, 2f), Vector3.zero, Vector3.forward, 90f);
        Assert.AreEqual(1f, d.x, Tolerance);
        Assert.AreEqual(0f, d.z, Tolerance);
        Vector3 l = BossVictoryBackDash.Direction(new Vector3(0f, 0f, 2f), Vector3.zero, Vector3.forward, -90f);
        Assert.AreEqual(-1f, l.x, Tolerance);
    }

    [Test]
    public void 겹치면_처치자_뒤쪽_그것도_없으면_월드_뒤()
    {
        Vector3 back = BossVictoryBackDash.Direction(Vector3.one, Vector3.one, new Vector3(1f, 0.5f, 0f), 0f);
        Assert.AreEqual(-1f, back.x, Tolerance);
        Assert.AreEqual(0f, back.y, Tolerance);
        Vector3 none = BossVictoryBackDash.Direction(Vector3.one, Vector3.one, Vector3.up, 0f);
        Assert.AreEqual(Vector3.back.z, none.z, Tolerance);
        Assert.AreEqual(0f, none.x, Tolerance);
    }
}
