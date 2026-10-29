using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 어쌔신 평타 판정 상자(할 일 144) — 조준 방향 배치·앵커 오프셋·프리팹 배선, 그리고 기본 상자가 예전 코드 부채꼴을 다 덮는지.
/// </summary>
public class AssassinStrikeBoxTests
{
    private const float Eps = 1e-4f;
    private const string PrefabPath = "Assets/2.Prefabs/Player/Assassin/Player_Assassin.prefab";

    [Test]
    public void 상자는_조준_방향으로_돌고_몸_회전과_무관하다()
    {
        var center = new Vector3(0f, 1f, 0.75f);
        var size = new Vector3(2.12f, 2f, 1.5f);
        AssassinStrikeBox right = AssassinStrikeBox.Place(new Vector3(5f, 0f, 5f), Vector3.right, Matrix4x4.identity, center, size);

        Assert.AreEqual(new Vector3(5.75f, 1f, 5f).x, right.Center.x, Eps);
        Assert.IsTrue(right.Contains(new Vector3(6.4f, 1f, 5f)), "오른쪽 앞 1.4m");
        Assert.IsFalse(right.Contains(new Vector3(3.6f, 1f, 5f)), "뒤쪽은 밖");
        Assert.IsFalse(right.Contains(new Vector3(6.6f, 1f, 5f)), "사거리 1.5 밖");
    }

    [Test]
    public void 앵커_노드_위치는_정면_기준으로_실린다()
    {
        // 앵커를 Armature 기준 앞 0.5m 에 둔 세팅 — 조준이 -X 면 월드 -X 로 0.5m 더 나간다.
        Matrix4x4 anchorToFrame = Matrix4x4.TRS(new Vector3(0f, 0f, 0.5f), Quaternion.identity, Vector3.one);
        AssassinStrikeBox box = AssassinStrikeBox.Place(Vector3.zero, Vector3.left, anchorToFrame,
            new Vector3(0f, 1f, 0.5f), new Vector3(1f, 2f, 1f));
        Assert.AreEqual(-1f, box.Center.x, Eps);
        Assert.AreEqual(0f, box.Center.z, Eps);
    }

    // 기본 상자 크기 근거 — 예전 부채꼴(반경 r, 각도 θ ≤ 180)을 감싸는 가장 작은 정면 정렬 상자: 앞 0~r, 폭 2r·sin(θ/2).
    [TestCase(1.5f, 90f, 2.12f)]
    [TestCase(1.5f, 100f, 2.30f)]
    [TestCase(1.6f, 100f, 2.45f)]
    [TestCase(1.7f, 120f, 2.94f)]
    [TestCase(1.8f, 120f, 3.12f)]
    public void 기본_상자는_예전_부채꼴을_다_덮는다(float range, float angle, float width)
    {
        AssassinStrikeBox box = AssassinStrikeBox.Place(Vector3.zero, Vector3.forward, Matrix4x4.identity,
            new Vector3(0f, 1f, range * 0.5f), new Vector3(width, 2f, range));
        // 반올림 여유 0.01m — 부채꼴 가장자리 점이 들어오는지만 본다.
        for (float a = -angle * 0.5f; a <= angle * 0.5f + 0.01f; a += 5f)
        {
            Vector3 edge = Quaternion.Euler(0f, a, 0f) * Vector3.forward * (range - 0.01f);
            Assert.IsTrue(box.Contains(new Vector3(edge.x * 0.995f, 1f, edge.z)), $"r={range} θ={angle} a={a}");
        }

        Assert.IsFalse(box.Contains(new Vector3(0f, 1f, range + 0.05f)), "정면 사거리 밖");
        Assert.IsFalse(box.Contains(new Vector3(0f, 1f, -0.05f)), "몸 뒤");
    }

    [Test]
    public void 프리팹에_일반4타_강타_변신_상자가_배정돼_있다()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.IsNotNull(prefab, PrefabPath);
        var attack = prefab.GetComponent<AssassinBasicAttack>();
        Assert.IsNotNull(attack);

        var so = new SerializedObject(attack);
        SerializedProperty normals = so.FindProperty("normalStepHitboxes");
        Assert.AreEqual(4, normals.arraySize, "일반 1~4타");
        for (int i = 0; i < normals.arraySize; i++)
            AssertBoxAnchor(normals.GetArrayElementAtIndex(i).objectReferenceValue, $"일반 {i + 1}타");
        AssertBoxAnchor(so.FindProperty("enhancedHitbox").objectReferenceValue, "강타");
        AssertBoxAnchor(so.FindProperty("transformedHitbox").objectReferenceValue, "변신 묶음");
    }

    private static void AssertBoxAnchor(Object reference, string label)
    {
        var info = reference as ColliderInfo;
        Assert.IsNotNull(info, label + " 앵커");
        Assert.IsTrue(info.TryGetLocalBox(out _, out Vector3 size), label + " BoxCollider");
        Assert.Greater(size.z, 0f, label);
    }
}
