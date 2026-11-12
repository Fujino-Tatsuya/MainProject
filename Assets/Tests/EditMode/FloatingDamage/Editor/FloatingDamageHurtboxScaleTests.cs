using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>판정(Hurtbox) 크기 배율(할 일 268) — clamp(sqrt(대상 ÷ 기준), 0.8, 1.6), 크기 = 경계 상자 대각선.</summary>
public sealed class FloatingDamageHurtboxScaleTests
{
    private const string SettingsPath = "Assets/9.ScriptableObject/UI/FloatingDamageSettings.asset";

    // Player.prefab 캡슐 반경 0.2·높이 1.6 / 일반 몬스터 반경 0.5·높이 2 / 23호 반경 1.7·높이 5.78
    static readonly float Player = FloatingDamageHurtboxScalePolicy.SizeOf(new Vector3(0.4f, 1.6f, 0.4f));
    static readonly float Mob = FloatingDamageHurtboxScalePolicy.SizeOf(new Vector3(1f, 2f, 1f));
    static readonly float Boss = FloatingDamageHurtboxScalePolicy.SizeOf(new Vector3(3.4f, 5.78f, 3.4f));

    [Test]
    public void 기준과_같으면_1()
    {
        Assert.AreEqual(1f, FloatingDamageHurtboxScalePolicy.Multiplier(Player, Player, 0.8f, 1.6f), 1e-5f);
    }

    [Test]
    public void 제곱근으로_완만하게_키운다()
    {
        float m = FloatingDamageHurtboxScalePolicy.Multiplier(Mob, Player, 0.8f, 1.6f);
        Assert.AreEqual(Mathf.Sqrt(Mob / Player), m, 1e-5f);
        Assert.That(m, Is.InRange(1.15f, 1.25f), "일반 몬스터 ≈ 1.2");
    }

    [Test]
    public void 최소_최대로_자른다()
    {
        Assert.AreEqual(1.6f, FloatingDamageHurtboxScalePolicy.Multiplier(Boss, Player, 0.8f, 1.6f), 1e-5f, "보스 = 최대");
        Assert.AreEqual(0.8f, FloatingDamageHurtboxScalePolicy.Multiplier(0.1f, Player, 0.8f, 1.6f), 1e-5f, "아주 작은 몸 = 최소");
        Assert.AreEqual(1.6f, FloatingDamageHurtboxScalePolicy.Multiplier(Boss, Player, 1.6f, 0.8f), 1e-5f, "최소·최대 뒤바뀌어도");
    }

    [Test]
    public void 크기_모름이나_기준_0은_1()
    {
        Assert.AreEqual(1f, FloatingDamageHurtboxScalePolicy.Multiplier(0f, Player, 0.8f, 1.6f));
        Assert.AreEqual(1f, FloatingDamageHurtboxScalePolicy.Multiplier(Boss, 0f, 0.8f, 1.6f));
    }

    [Test]
    public void 에셋_기본값()
    {
        var settings = AssetDatabase.LoadAssetAtPath<FloatingDamageSettings>(SettingsPath);
        Assert.IsNotNull(settings, SettingsPath);
        Assert.AreEqual(1.7f, settings.HurtboxReferenceSize, 1e-5f);
        Assert.AreEqual(0.8f, settings.HurtboxScaleMin, 1e-5f);
        Assert.AreEqual(1.6f, settings.HurtboxScaleMax, 1e-5f);
    }

    [Test]
    public void 측정_자식_Unit_판정은_뺀다()
    {
        var root = new GameObject("Boss");
        var child = new GameObject("Wells");
        try
        {
            var boss = root.AddComponent<Unit>();
            var bossBody = new GameObject("BossHurt");
            bossBody.transform.SetParent(root.transform, false);
            var bossCapsule = bossBody.AddComponent<CapsuleCollider>();
            bossCapsule.radius = 0.5f;
            bossCapsule.height = 2f;
            SetOwner(bossBody.AddComponent<Hurtbox>(), boss);

            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = new Vector3(10f, 0f, 0f);
            var wells = child.AddComponent<Unit>();
            var wellsBox = child.AddComponent<BoxCollider>();
            wellsBox.size = Vector3.one * 4f;
            SetOwner(child.AddComponent<Hurtbox>(), wells);
            Physics.SyncTransforms();

            float size = FloatingDamageHurtboxMeasure.Measure(boss);
            Assert.AreEqual(FloatingDamageHurtboxScalePolicy.SizeOf(new Vector3(1f, 2f, 1f)), size, 1e-3f);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void SetOwner(Hurtbox hurtbox, Unit owner)
    {
        var so = new SerializedObject(hurtbox);
        so.FindProperty("ownerUnit").objectReferenceValue = owner;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
