using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 할 일 47 — 어쌔신 Armature 프리팹에 머리카락 2차 모션이 빠짐없이 붙어 있는가(체인 7×4 본·캡처·구동기·보정 2종).
// 깨지면 Tools/Player/Assassin/머리카락 본 연결 을 다시 돌린다.
public sealed class AssassinHairPrefabTests
{
    private const string ArmaturePath = "Assets/2.Prefabs/Player/Assassin/Assassin_Armature.prefab";
    private const string VariantPath = "Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab";

    private static GameObject LoadArmature()
    {
        GameObject armature = AssetDatabase.LoadAssetAtPath<GameObject>(ArmaturePath);
        Assert.That(armature, Is.Not.Null, ArmaturePath);
        return armature;
    }

    [Test]
    public void SolverIsCapturedWithAllHairBones()
    {
        AssassinHairMotion hair = LoadArmature().GetComponent<AssassinHairMotion>();

        Assert.That(hair, Is.Not.Null, "Armature 루트에 AssassinHairMotion");
        Assert.That(hair.captured, Is.True, "에디터 바인드 포즈 캡처");
        Assert.That(hair.head != null && hair.head.name == "Head", Is.True, "Head 본");
        Assert.That(hair.hips != null && hair.hips.name == "Hips", Is.True, "Hips 본");
        Assert.That(hair.chains, Has.Length.EqualTo(7));
        for (int c = 0; c < 7; c++)
        {
            Assert.That(hair.chains[c].bones, Has.Length.EqualTo(4), $"체인 {c}");
            for (int b = 0; b < 4; b++)
            {
                string expected = $"Hair_Source{c:D2}_{b + 1:D2}";
                Transform bone = hair.chains[c].bones[b];
                Assert.That(bone != null && bone.name == expected, Is.True, expected);
            }
        }
    }

    [Test]
    public void BodyCollisionCapsulesAreCaptured()
    {
        AssassinHairMotion hair = LoadArmature().GetComponent<AssassinHairMotion>();

        // 몸통 2(Chest·Waist) + 좌우 위팔·아래팔·허벅지 6.
        Assert.That(hair.capsules, Has.Length.EqualTo(8));
        foreach (AssassinHairMotion.BodyCapsule capsule in hair.capsules)
            Assert.That(capsule.first != null && capsule.last != null, Is.True, capsule.name);
    }

    [Test]
    public void DriverAndOverlaysArePresent()
    {
        GameObject armature = LoadArmature();

        Assert.That(armature.GetComponent<AssassinHairDriver>(), Is.Not.Null, "구동기");
        Assert.That(armature.GetComponent<AssassinActionHairAnimation>(), Is.Not.Null, "v16 동작별 보정");
        Assert.That(armature.GetComponent<AssassinDiveHairAnimation>(), Is.Not.Null, "v15 회피 보정");
    }

    [Test]
    public void VariantCarriesHairDriver()
    {
        GameObject variant = AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath);

        Assert.That(variant, Is.Not.Null, VariantPath);
        Assert.That(variant.GetComponentInChildren<AssassinHairDriver>(true), Is.Not.Null, "Player_Assassin 중첩 Armature");
    }
}
