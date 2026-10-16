using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 몬스터 발밑 체력바의 지연 감소(잔상) 고정 — 프리팹 연결(보스 HUD 값·색), 플레이어 머리 위 바 무변경(null 안전),
/// 숨김 순간 스냅·숨김 중 첫 타 잔상 유지, 튜토리얼 스피너 잠금(회색) ↔ 본 모드(빨강) 전환 스냅.
/// 잔상 동작 자체는 <see cref="DelayedHealthBar"/>(보스·플레이어 HUD 와 공용).
/// </summary>
public sealed class MonsterOverheadHealthBarDelayedTests
{
    const string NormalPath = "Assets/2.Prefabs/UI/MonsterHealthBar_Normal.prefab";
    const string MidBossPath = "Assets/2.Prefabs/UI/MonsterHealthBar_MidBoss.prefab";
    const string PlayerBarPath = "Assets/2.Prefabs/UI/OverheadHealthBar.prefab";
    const string BossHudPath = "Assets/2.Prefabs/UI/BossHealthHUD.prefab";

    static readonly Color Gray = new Color(0.55f, 0.55f, 0.55f, 1f);
    static readonly Color Red = new Color(0.9f, 0.2f, 0.2f, 1f);

    static SerializedProperty Delayed(GameObject prefab, out SerializedObject so)
    {
        so = new SerializedObject(prefab.GetComponent<UnitOverheadHealthBar>());
        return so.FindProperty("delayed");
    }

    [TestCase(NormalPath)]
    [TestCase(MidBossPath)]
    public void MonsterBarPrefab_DelayedFillBehindHpFill_SameSize(string path)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Assert.That(prefab, Is.Not.Null, path);
        var delayed = Delayed(prefab, out var so);
        var fill = delayed.FindPropertyRelative("delayedFill").objectReferenceValue as Image;
        Assert.That(fill, Is.Not.Null, "잔상 Image 연결 — MidBoss 는 Normal 에서 상속");
        Assert.That(so.FindProperty("trailFill").objectReferenceValue, Is.Null, "구 잔상(trailFill)과 이중으로 그리지 않는다");

        var hp = (Image)so.FindProperty("hpFill").objectReferenceValue;
        Assert.That(fill.transform.parent, Is.SameAs(hp.transform.parent));
        Assert.That(fill.transform.GetSiblingIndex(), Is.LessThan(hp.transform.GetSiblingIndex()), "채움 바 뒤");
        var a = (RectTransform)fill.transform;
        var b = (RectTransform)hp.transform;
        Assert.That(a.anchorMin, Is.EqualTo(b.anchorMin));
        Assert.That(a.anchorMax, Is.EqualTo(b.anchorMax));
        Assert.That(a.sizeDelta, Is.EqualTo(b.sizeDelta));
        Assert.That(a.anchoredPosition, Is.EqualTo(b.anchoredPosition));
        Assert.That(fill.type, Is.EqualTo(Image.Type.Filled));
        Assert.That(fill.fillMethod, Is.EqualTo(hp.fillMethod));
        Assert.That(fill.fillOrigin, Is.EqualTo(hp.fillOrigin));
    }

    [TestCase(NormalPath)]
    [TestCase(MidBossPath)]
    public void MonsterBarPrefab_MatchesBossHudDelayedValuesAndColor(string path)
    {
        var bossHud = AssetDatabase.LoadAssetAtPath<GameObject>(BossHudPath);
        Assert.That(bossHud, Is.Not.Null);
        var bossSo = new SerializedObject(bossHud.GetComponentInChildren<BossHealthHUD>(true));
        var boss = bossSo.FindProperty("delayed");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var mine = Delayed(prefab, out _);

        foreach (var name in new[] { "holdSeconds", "drainRatePerSecond" })
            Assert.That(mine.FindPropertyRelative(name).floatValue, Is.EqualTo(boss.FindPropertyRelative(name).floatValue), name);
        Assert.That(mine.FindPropertyRelative("maxHeldHits").intValue, Is.EqualTo(boss.FindPropertyRelative("maxHeldHits").intValue));
        Assert.That(mine.FindPropertyRelative("resetHoldOnDamage").boolValue, Is.EqualTo(boss.FindPropertyRelative("resetHoldOnDamage").boolValue));

        var bossFill = (Image)boss.FindPropertyRelative("delayedFill").objectReferenceValue;
        var myFill = (Image)mine.FindPropertyRelative("delayedFill").objectReferenceValue;
        Assert.That(myFill.color, Is.EqualTo(bossFill.color), "잔상 색 = 보스 HUD 잔상 색");
    }

    [Test]
    public void PlayerOverheadBar_HasNoDelayedFill_KeepsOldTrail()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerBarPath);
        Assert.That(prefab, Is.Not.Null);
        var delayed = Delayed(prefab, out var so);
        Assert.That(delayed.FindPropertyRelative("delayedFill").objectReferenceValue, Is.Null, "플레이어 머리 위 바는 지연 감소 바를 쓰지 않는다");
        Assert.That(so.FindProperty("trailFill").objectReferenceValue, Is.Not.Null, "플레이어 바의 기존 잔상은 그대로");
    }

    [Test]
    public void DelayedHealthBar_WithoutFill_IsNoOp()
    {
        var bar = new DelayedHealthBar();
        Assert.That(bar.HasFill, Is.False);
        Assert.DoesNotThrow(() =>
        {
            bar.Bind(100);
            bar.OnHpChanged(100, 40);
            bar.Tick(0.1f, 40, 100);
        });
    }

    [Test]
    public void Snap_OnHide_NotOnShow()
    {
        Assert.That(OverheadDelayedBarRules.ShouldSnap(true, false, Red, Red), Is.True, "숨김 순간");
        Assert.That(OverheadDelayedBarRules.ShouldSnap(false, true, Red, Red), Is.False, "표시 순간엔 스냅하지 않는다 — 첫 타 잔상 유지");
        Assert.That(OverheadDelayedBarRules.ShouldSnap(false, false, Red, Red), Is.False);
        Assert.That(OverheadDelayedBarRules.ShouldSnap(true, true, Red, Red), Is.False);
    }

    [Test]
    public void Snap_OnFillColorChangeWhileShown()
    {
        Assert.That(OverheadDelayedBarRules.ShouldSnap(true, true, Gray, Red), Is.True, "연습(회색) → 본 모드(빨강)");
        Assert.That(OverheadDelayedBarRules.ShouldSnap(true, true, Red, Gray), Is.True);
        Assert.That(OverheadDelayedBarRules.ShouldSnap(false, true, Gray, Red), Is.False, "숨김 → 표시는 색이 달라도 스냅 안 함(숨김 순간 이미 스냅)");
    }

    // 아래는 UnitOverheadHealthBar.TickDelayed 와 같은 순서(스냅 판단 → Tick)로 DelayedHealthBar 를 돌린다.
    sealed class Harness : System.IDisposable
    {
        readonly GameObject go;
        public readonly Image Fill;
        public readonly DelayedHealthBar Bar = new DelayedHealthBar();
        bool shown;
        Color color;
        public int Hp;
        public int MaxHp = 100;

        public Harness(int hp)
        {
            go = new GameObject("fill", typeof(RectTransform), typeof(Image));
            Fill = go.GetComponent<Image>();
            typeof(DelayedHealthBar).GetField("delayedFill", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Bar, Fill);
            typeof(DelayedHealthBar).GetField("resetHoldOnDamage", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Bar, false);
            Hp = hp;
            Bar.Bind(hp);
        }

        public void SetHp(int next)
        {
            int prev = Hp;
            Hp = next;
            Bar.OnHpChanged(prev, next);   // Unit.ClientHpChanged
        }

        public void Frame(bool show, Color fillColor, float dt = 0.05f)
        {
            if (OverheadDelayedBarRules.ShouldSnap(shown, show, color, fillColor))
                Bar.Bind(Hp);
            shown = show;
            color = fillColor;
            Bar.Tick(dt, Hp, MaxHp);
        }

        public void Dispose() => Object.DestroyImmediate(go);
    }

    [Test]
    public void FirstHitWhileHidden_KeepsTrail()
    {
        using var h = new Harness(100);
        h.Frame(false, Red);
        h.SetHp(60);
        h.Frame(true, Red);   // 피격으로 표시가 켜진 프레임
        Assert.That(h.Fill.fillAmount, Is.EqualTo(1f).Within(1e-4f), "옛 체력에 머문다");
    }

    [Test]
    public void HideThenReshow_DoesNotDropFromOldValue()
    {
        using var h = new Harness(100);
        h.Frame(true, Red);
        h.SetHp(60);
        h.Frame(true, Red);
        Assert.That(h.Fill.fillAmount, Is.GreaterThan(0.9f), "잔상 유지 중");

        h.Frame(false, Red);  // 숨김(전투 종료 + 지연)
        Assert.That(h.Fill.fillAmount, Is.EqualTo(0.6f).Within(1e-4f), "숨김 순간 현재 값");

        h.Frame(true, Red);   // 다시 표시
        Assert.That(h.Fill.fillAmount, Is.EqualTo(0.6f).Within(1e-4f), "옛 값에서 튀어 내려오지 않는다");
    }

    [Test]
    public void SpinnerLocked_NoHpChange_NoTrail_ThenRealModeSnaps()
    {
        using var h = new Harness(100);
        // 연습 중: 피해 0 → 체력 복제값 그대로 → ClientHpChanged 없음. 잔상 = 체력.
        for (int i = 0; i < 10; i++)
            h.Frame(true, Gray);
        Assert.That(h.Fill.fillAmount, Is.EqualTo(1f).Within(1e-4f));

        // 잔상이 남은 상태에서 모드 전환(색 변경)되면 끊는다.
        h.SetHp(70);
        h.Frame(true, Gray);
        Assert.That(h.Fill.fillAmount, Is.GreaterThan(0.9f));
        h.Frame(true, Red);
        Assert.That(h.Fill.fillAmount, Is.EqualTo(0.7f).Within(1e-4f), "회색 → 빨강 전환 시 잔상 초기화");

        // 본 모드 피격은 정상 잔상.
        h.SetHp(40);
        h.Frame(true, Red);
        Assert.That(h.Fill.fillAmount, Is.EqualTo(0.7f).Within(1e-4f));
    }

    [Test]
    public void Death_BindsToZero()
    {
        using var h = new Harness(100);
        h.Frame(true, Red);
        h.SetHp(0);
        h.Frame(false, Red);
        Assert.That(h.Fill.fillAmount, Is.EqualTo(0f));
    }
}
