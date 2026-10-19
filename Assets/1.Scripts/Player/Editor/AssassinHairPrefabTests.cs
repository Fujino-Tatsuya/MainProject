using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 할 일 47 — 어쌔신 Armature 프리팹에 머리카락 2차 모션이 빠짐없이 붙어 있는가(체인 7×4 본·캡처·구동기·보정 2종).
// 깨지면 Tools/Player/Assassin/머리카락 본 연결 을 다시 돌린다.
public sealed class AssassinHairPrefabTests
{
    private const string ArmaturePath = "Assets/2.Prefabs/Player/Assassin/Assassin_Armature.prefab";
    private const string VariantPath = "Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab";
    private const string ModelPath = "Assets/50.Art/Char/assassin/Assassin.fbx";

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

    // v18(10-17) — 납품 v13 의 길이 차원 값 × FBX 임포트 배율(×1.25)로 캡처했는가(AssassinHairAuthoring).
    // 몸만 커지고 충돌 반경이 그대로면 머리카락이 팔·몸 속으로 더 들어간다(팔꿈치 비침 재발). 배율이 바뀌면 메뉴를 다시 돌린다.
    [Test]
    public void SolverLengthsFollowRigScale()
    {
        GameObject armature = LoadArmature();
        AssassinHairMotion hair = armature.GetComponent<AssassinHairMotion>();
        float scale = ((ModelImporter)AssetImporter.GetAtPath(ModelPath)).globalScale;
        const float tolerance = 1e-5f;

        Assert.That(hair.hairRadius, Is.EqualTo(.018f * scale).Within(tolerance), "hairRadius");
        Assert.That(hair.gravitySupport, Is.EqualTo(.65f * scale).Within(tolerance), "gravitySupport");
        Assert.That(hair.maximumDynamicSpeed, Is.EqualTo(2f * scale).Within(tolerance), "maximumDynamicSpeed");
        Assert.That(hair.neighbourOffsetLimit, Is.EqualTo(.09f * scale).Within(tolerance), "neighbourOffsetLimit");
        foreach (AssassinHairMotion.BodyCapsule capsule in hair.capsules)
            Assert.That(capsule.radius, Is.EqualTo(DeliveredRadius(capsule.name) * scale).Within(tolerance), capsule.name);
        for (int c = 0; c < hair.chains.Length; c++)
            Assert.That(hair.chains[c].tipLocal.magnitude, Is.EqualTo(.15f * scale).Within(1e-4f), $"체인 {c} 끝 마디");
        // 임포트 배율이 실제 리그 크기와 맞는가(크기를 FBX 에 구우면 어긋난다) — 납품 v13 리그의 Hair_Source00_01 → 00_02 길이 대비.
        Assert.That(hair.chains[0].bones[1].localPosition.magnitude / .15705195f, Is.EqualTo(scale).Within(.01f * scale), "리그 실측 배율");
    }

    // 바인드 겹침 허용치가 지금 반경으로 잰 값인가 — 반경만 키우고 다시 재지 않으면 서 있기만 해도 가닥이 밀려난다.
    [Test]
    public void BindAllowancesMatchCapsuleRadii()
    {
        GameObject contents = PrefabUtility.LoadPrefabContents(ArmaturePath);
        try
        {
            AssassinHairMotion hair = contents.GetComponent<AssassinHairMotion>();
            var saved = new float[hair.chains.Length][];
            for (int c = 0; c < saved.Length; c++)
            {
                Assert.That(hair.chains[c].bindAllowanceReference, Is.Not.Null, $"체인 {c} 허용치");
                saved[c] = (float[])hair.chains[c].bindAllowanceReference.Clone();
            }

            Assert.That(AssassinHairAuthoring.RecomputeBindAllowances(hair), Is.True, "재계산");
            for (int c = 0; c < saved.Length; c++)
            {
                float[] recomputed = hair.chains[c].bindAllowanceReference;
                Assert.That(recomputed, Has.Length.EqualTo(saved[c].Length), $"체인 {c}");
                for (int i = 0; i < saved[c].Length; i++)
                    Assert.That(saved[c][i], Is.EqualTo(recomputed[i]).Within(1e-6f), $"체인 {c} 마디 {i / hair.capsules.Length} · {hair.capsules[i % hair.capsules.Length].name}");
            }
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // 납품 CaptureBindPose 의 캡슐 반경(배율 1).
    private static float DeliveredRadius(string capsule) =>
        capsule == "Chest" ? .105f
        : capsule == "Waist" ? .095f
        : capsule.EndsWith("upper arm") ? .060f
        : capsule.EndsWith("forearm") ? .042f
        : capsule.EndsWith("thigh") ? .065f
        : float.NaN;

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
