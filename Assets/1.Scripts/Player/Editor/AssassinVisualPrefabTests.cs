using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

// PLAN-assassin §9 (v18 비주얼, 10-17) — 어쌔신이 팔라딘·거너와 같은 Flat Kit Variant 를 쓰고, 키 ×1.25(FBX 임포트 배율)에 맞춰
// 단검만 루트 배율을 받았는가. 판정 박스는 몸 크기와 무관하게 그대로다 — Armature 루트에 배율을 걸면 판정이 같이 커진다.
// 깨지면 Tools/Rendering/Flat Kit/플레이어 Variant 구성 을 다시 돌리고, 단검 배율은 Assassin_Armature 의 단검 루트를 확인한다.
public sealed class AssassinVisualPrefabTests
{
    private const string ArmaturePath = "Assets/2.Prefabs/Player/Assassin/Assassin_Armature.prefab";
    private const string VariantPath = "Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab";
    private const string ModelPath = "Assets/50.Art/Char/assassin/Assassin.fbx";
    private const string BodyMaterialPath = "Assets/3.Materials/FlatKit/Player/Assassin/FK_Assassin_Body.mat";
    private const string ParentMaterialPath = "Assets/3.Materials/FlatKit/Player/FK_Paladin_Toon.mat";
    private const string BodyTexturePath = "Assets/50.Art/Char/assassin/Textures/Assassin_BaseColor.png";
    private const string ControllerPath = "Assets/4.Animations/Player/Assassin/AssassinAnimatorController.controller";
    private const int ExpectedSlots = 5; // HeadNeck 2(Head·Face) · SourceBody 1 · 단검 L/R 1씩
    private static readonly string[] DaggerNames = { "Dagger_Weapon_L", "Dagger_Weapon_R" };

    private static Transform LoadArmature(string prefabPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        Assert.That(prefab, Is.Not.Null, prefabPath);
        // Armature 루트 = 머리카락 솔버가 붙은 곳(Player_Assassin 에서는 중첩 프리팹).
        AssassinHairMotion hair = prefab.GetComponentInChildren<AssassinHairMotion>(true);
        Assert.That(hair, Is.Not.Null, $"{prefabPath} 의 Armature");
        return hair.transform;
    }

    private static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
            if (child.name == name)
                return child;
        return null;
    }

    [Test]
    public void BodyMaterialIsFlatKitVariantOfPaladin()
    {
        Material body = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);

        Material parent = AssetDatabase.LoadAssetAtPath<Material>(ParentMaterialPath);
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BodyTexturePath);

        Assert.That(body, Is.Not.Null, BodyMaterialPath);
        Assert.That(parent, Is.Not.Null, ParentMaterialPath);
        Assert.That(texture, Is.Not.Null, BodyTexturePath);
        Assert.That(body.parent, Is.EqualTo(parent), "부모 = FK_Paladin_Toon");
        Assert.That(body.GetTexture("_BaseMap"), Is.EqualTo(texture), "_BaseMap = Assassin_BaseColor");
    }

    [TestCase(ArmaturePath)]
    [TestCase(VariantPath)]
    public void BodyAndDaggerSlotsUseFlatKitBody(string prefabPath)
    {
        Transform armature = LoadArmature(prefabPath);
        Material body = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPath);

        int slots = 0;
        foreach (SkinnedMeshRenderer renderer in armature.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            slots += AssertSlots(renderer, body);
        foreach (string name in DaggerNames)
        {
            Transform dagger = FindDeep(armature, name);
            Assert.That(dagger, Is.Not.Null, name);
            foreach (Renderer renderer in dagger.GetComponentsInChildren<Renderer>(true))
                slots += AssertSlots(renderer, body);
        }
        Assert.That(slots, Is.EqualTo(ExpectedSlots), "머리·몸·단검 슬롯 수");
    }

    private static int AssertSlots(Renderer renderer, Material body)
    {
        Material[] materials = renderer.sharedMaterials;
        for (int i = 0; i < materials.Length; i++)
            Assert.That(materials[i], Is.EqualTo(body), $"{renderer.name}[{i}]");
        return materials.Length;
    }

    // 몸은 임포트 배율로 커지고 본 lossyScale 은 1 이라, 손에 붙인 단검은 같은 배율을 루트에 직접 받는다.
    [TestCase(ArmaturePath)]
    [TestCase(VariantPath)]
    public void DaggerRootsCarryBodyImportScale(string prefabPath)
    {
        float importScale = ((ModelImporter)AssetImporter.GetAtPath(ModelPath)).globalScale;
        Transform armature = LoadArmature(prefabPath);

        foreach (string name in DaggerNames)
        {
            Transform dagger = FindDeep(armature, name);
            Assert.That(dagger, Is.Not.Null, name);
            Assert.That(Vector3.Distance(dagger.localScale, Vector3.one * importScale), Is.LessThan(1e-4f), $"{name} 배율 {dagger.localScale}");
        }
    }

    // 보폭이 임포트 배율만큼 길어진다 — 달리기 루프 재생 속도 = 1 / 배율이라야 발이 미끄러지지 않는다(배율 1 에서 자연 속도 ≈ 이동 5 m/s).
    [Test]
    public void RunLoopSpeedCompensatesImportScale()
    {
        float importScale = ((ModelImporter)AssetImporter.GetAtPath(ModelPath)).globalScale;
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        Assert.That(controller, Is.Not.Null, ControllerPath);

        AnimatorState runLoop = null;
        foreach (ChildAnimatorState child in controller.layers[0].stateMachine.states)
            if (child.state.name == "Run_Loop")
                runLoop = child.state;
        Assert.That(runLoop, Is.Not.Null, "Run_Loop 상태");
        Assert.That(runLoop.speed, Is.EqualTo(1f / importScale).Within(1e-4f), "Run_Loop 재생 속도");
    }

    [Test]
    public void ArmatureRootAndHitboxesStayUnscaled()
    {
        Transform armature = LoadArmature(ArmaturePath);

        Assert.That(armature.localScale, Is.EqualTo(Vector3.one), "Armature 루트 배율 1 — 판정 박스가 루트의 자식이다");
        AssertHitbox(armature, "DefaultAttack", new Vector3(2f, 2f, 1.7f), new Vector3(0f, 1f, .85f));
        AssertHitbox(armature, "InterruptAttack", new Vector3(1.4f, 1.8f, 1.4f), new Vector3(0f, 1f, .9f));
        Assert.That(LoadArmature(VariantPath).localScale, Is.EqualTo(Vector3.one), "Player_Assassin 의 Armature 배율 1");
    }

    private static void AssertHitbox(Transform armature, string name, Vector3 size, Vector3 center)
    {
        Transform anchor = FindDeep(armature, name);
        Assert.That(anchor, Is.Not.Null, name);
        Assert.That(anchor.localScale, Is.EqualTo(Vector3.one), $"{name} 배율");
        BoxCollider box = anchor.GetComponent<BoxCollider>();
        Assert.That(box, Is.Not.Null, $"{name} BoxCollider");
        Assert.That(Vector3.Distance(box.size, size), Is.LessThan(1e-4f), $"{name} 크기 {box.size}");
        Assert.That(Vector3.Distance(box.center, center), Is.LessThan(1e-4f), $"{name} 중심 {box.center}");
    }
}
