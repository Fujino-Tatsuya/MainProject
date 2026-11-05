using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 할 일 221 — 건틀렛 스매시 예고 원을 미리 구운 sprite 대신 C3 판(EnemyC3Plate, 원 09)으로 그린다.
/// 그림(구운 sprite 와 같은 CPU Circle("09"))은 EnemyC3PlateShaderTests 의 Plate_09_* 픽셀 비교가 맡고,
/// 여기서는 붙이기 경로 — 크기·평면·스프라이트 끔·FadeInHoldEffect 알파 전달·풀 재사용을 확인한다.
/// </summary>
public sealed class EnemyC3SmashPlateTests
{
    static readonly int ColorId = Shader.PropertyToID("_Color");

    static GameObject NewSmash()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DecalFloorBlendLabSceneBuilder.SmashWarningPath);
        Assume.That(prefab, Is.Not.Null, DecalFloorBlendLabSceneBuilder.SmashWarningPath);
        return Object.Instantiate(prefab);
    }

    static float PlateAlpha(EnemyC3SmashPlate p)
    {
        var block = new MaterialPropertyBlock();
        p.Plate.GetPropertyBlock(block);
        return block.GetColor(ColorId).a;
    }

    [Test]
    public void ApplySmash_DrawsThePlateWhereTheBakedSpriteWas()
    {
        var go = NewSmash();
        try
        {
            var sr = go.GetComponentInChildren<SpriteRenderer>(true);
            Sprite source = sr.sprite;
            Vector3 scale = sr.transform.localScale;
            EnemyC3Visuals.ApplySmash(go);

            var p = sr.GetComponent<EnemyC3SmashPlate>();
            Assert.IsNotNull(p, "plate attached to the sprite");
            Assert.IsFalse(sr.enabled, "VFX sprite hidden");
            Assert.AreSame(source, sr.sprite, "VFX asset sprite untouched");
            Assert.AreEqual(scale, sr.transform.localScale, "transform scale untouched");
            Assert.AreSame(EnemyC3Visuals.PlateMaterial, p.Plate.sharedMaterial);
            Assert.IsNotNull(p.Plate.GetComponentInParent<NoHitFlash>(), "excluded from hit flash");

            // 예전 구운 sprite 폭 = 원본 sprite 폭 × OuterScale("09"), 중심 피벗.
            float legacy = source.bounds.size.x * EnemyC3Visuals.OuterScale("09");
            Transform t = p.Plate.transform;
            Assert.AreEqual(legacy, t.localScale.x, legacy * 1e-4f);
            Assert.AreEqual(legacy, t.localScale.z, legacy * 1e-4f);
            Assert.AreEqual(Vector3.zero, t.localPosition);
            // 판 평면(로컬 XZ)이 스프라이트 평면(로컬 XY)에 눕는다 — 판 법선 = 스프라이트 법선(±).
            float facing = Mathf.Abs(Vector3.Dot(t.up, sr.transform.forward));
            Assert.AreEqual(1f, facing, 1e-4f, "plate lies in the sprite plane");
            // 월드 크기 = 예전 sprite 월드 크기.
            float world = legacy * sr.transform.lossyScale.x;
            Assert.AreEqual(world, p.Plate.bounds.size.x, world * 1e-3f);
            Assert.AreEqual(sr.color.a, PlateAlpha(p), 1e-5f, "starts at the sprite alpha");
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void FadeInHoldEffect_DrivesThePlateAlpha()
    {
        var go = NewSmash();
        try
        {
            EnemyC3Visuals.ApplySmash(go);
            var fade = go.GetComponentInChildren<FadeInHoldEffect>(true);
            Assume.That(fade, Is.Not.Null, "FadeInHoldEffect on the smash VFX");
            var p = go.GetComponentInChildren<EnemyC3SmashPlate>(true);
            var apply = typeof(FadeInHoldEffect).GetMethod("ApplyAlpha", BindingFlags.Instance | BindingFlags.NonPublic);

            apply.Invoke(fade, new object[] { 0.42f });
            Assert.AreEqual(0.42f, PlateAlpha(p), 1e-5f, "alpha forwarded");
            fade.ResetForPool();
            float start = go.GetComponentInChildren<SpriteRenderer>(true).color.a;
            Assert.AreEqual(start, PlateAlpha(p), 1e-5f, "pool reset forwarded");

            // 풀 재사용 — 다시 붙여도 판 하나, 알파 전달 그대로.
            EnemyC3Visuals.ApplySmash(go);
            Assert.AreEqual(1, go.GetComponentsInChildren<EnemyC3SmashPlate>(true).Length);
            apply.Invoke(fade, new object[] { 0.7f });
            Assert.AreEqual(0.7f, PlateAlpha(p), 1e-5f);
        }
        finally { Object.DestroyImmediate(go); }
    }

    [Test]
    public void BakedSpriteAndBakeMenuAreGone()
    {
        Assert.IsNull(Resources.Load<Sprite>("Telegraph/EnemyC3_Smash09"), "baked smash sprite retired (221)");
        Assert.IsNull(System.Type.GetType("EnemyC3SmashSpriteBaker, Assembly-CSharp-Editor"));
    }
}
