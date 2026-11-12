using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>무기 보조 고정점(팔라딘 방패 손잡이2) — 회전 계산·가중치·상태 판단·프리팹 연결.</summary>
public sealed class WeaponTransformRelaySecondaryTests
{
    const float Tolerance = 1e-4f;

    [Test]
    public void 손잡이2_방향을_팔꿈치_방향으로_돌린다()
    {
        Vector3 pivot = new Vector3(1f, 2f, 3f);
        Quaternion rot = Quaternion.Euler(10f, 20f, 30f);
        Vector3 local = new Vector3(0f, 0f, 0.5f);
        Vector3 handle = pivot + rot * local;
        Vector3 target = pivot + new Vector3(2f, 0f, 0f);
        Quaternion full = WeaponTransformRelay.AlignToSecondary(rot, pivot, handle, target, 1f);
        Assert.AreEqual(1f, Vector3.Dot((full * local).normalized, Vector3.right), Tolerance, "가중치 1 = 손잡이2 가 팔꿈치 쪽");
        Assert.AreEqual(0.5f, (full * local).magnitude, Tolerance, "거리 보정 없음(회전만)");
    }

    [Test]
    public void 가중치_0은_그대로_중간은_사이()
    {
        Quaternion rot = Quaternion.identity;
        Quaternion none = WeaponTransformRelay.AlignToSecondary(rot, Vector3.zero, Vector3.forward, Vector3.right, 0f);
        Assert.AreEqual(0f, Quaternion.Angle(rot, none), Tolerance, "0 = 손만 따라감");
        Quaternion half = WeaponTransformRelay.AlignToSecondary(rot, Vector3.zero, Vector3.forward, Vector3.right, 0.5f);
        Assert.AreEqual(45f, Quaternion.Angle(rot, half), 0.01f, "0.5 = 절반(90° 의 반)");
        Assert.AreEqual(0f, Quaternion.Angle(rot, WeaponTransformRelay.AlignToSecondary(rot, Vector3.zero, Vector3.zero, Vector3.right, 1f)), Tolerance,
            "손잡이2 가 원점과 같으면 그대로");
    }

    [Test]
    public void 가중치는_블렌드_시간에_걸쳐_0과_1로()
    {
        Assert.AreEqual(0.5f, WeaponTransformRelay.StepWeight(0f, true, 0.1f, 0.05f), Tolerance);
        Assert.AreEqual(1f, WeaponTransformRelay.StepWeight(0.9f, true, 0.1f, 0.05f), Tolerance, "1 에서 멈춤");
        Assert.AreEqual(0.5f, WeaponTransformRelay.StepWeight(1f, false, 0.1f, 0.05f), Tolerance, "끄면 내려감");
        Assert.AreEqual(1f, WeaponTransformRelay.StepWeight(0f, true, 0f, 0f), Tolerance, "블렌드 0 = 즉시");
        Assert.AreEqual(0.5f, WeaponTransformRelay.StepWeight(0.5f, true, 0.1f, 0f), Tolerance, "경과 0 이면 그대로");
    }

    [Test]
    public void 상태는_지금_또는_전이_중_다음()
    {
        int ult = Animator.StringToHash(PaladinShieldGripAuthoring.UltimateState);
        int idle = Animator.StringToHash("Idle");
        Assert.IsTrue(WeaponTransformRelay.MatchesState(ult, false, 0, ult), "궁 재생 중(처치 연출 정지·느려짐도 상태는 그대로)");
        Assert.IsTrue(WeaponTransformRelay.MatchesState(idle, true, ult, ult), "궁으로 넘어가는 중");
        Assert.IsTrue(WeaponTransformRelay.MatchesState(ult, true, idle, ult), "궁에서 나가는 중(블렌드로 풀림)");
        Assert.IsFalse(WeaponTransformRelay.MatchesState(idle, false, ult, ult), "전이 아님 — 다음 해시 무시");
        Assert.IsFalse(WeaponTransformRelay.MatchesState(idle, false, 0, ult));
        Assert.IsFalse(WeaponTransformRelay.MatchesState(0, false, 0, 0), "상태 비움 = 끔");
    }

    [Test]
    public void 팔라딘_프리팹_방패_줄에_손잡이2_ElbowSocket_궁_상태가_연결()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PaladinShieldGripAuthoring.PrefabPath);
        Assert.IsNotNull(prefab);
        Transform shield = PaladinShieldGripAuthoring.Find(prefab.transform, PaladinShieldGripAuthoring.ShieldName);
        Transform elbow = PaladinShieldGripAuthoring.Find(prefab.transform, PaladinShieldGripAuthoring.ElbowSocketName);
        Assert.IsNotNull(shield, "방패");
        Transform handle = shield.Find(PaladinShieldGripAuthoring.HandleName);
        Assert.IsNotNull(handle, "방패 자식 손잡이2");
        Assert.IsNotNull(elbow, "ElbowSocket");
        Assert.AreEqual(PaladinShieldGripAuthoring.ElbowParentName, elbow.parent.name, "팔꿈치 본 자식");

        var relay = prefab.GetComponentInChildren<WeaponTransformRelay>(true);
        Assert.IsNotNull(relay);
        SerializedProperty follows = new SerializedObject(relay).FindProperty("follows");
        bool found = false;
        for (int i = 0; i < follows.arraySize; i++)
        {
            SerializedProperty f = follows.GetArrayElementAtIndex(i);
            if (f.FindPropertyRelative("weapon").objectReferenceValue != shield) continue;
            found = true;
            Assert.AreEqual(handle, f.FindPropertyRelative("secondaryHandle").objectReferenceValue);
            Assert.AreEqual(elbow, f.FindPropertyRelative("secondaryTarget").objectReferenceValue);
            Assert.AreEqual(PaladinShieldGripAuthoring.UltimateState, f.FindPropertyRelative("secondaryState").stringValue);
            Assert.Greater(f.FindPropertyRelative("secondaryBlend").floatValue, 0f);
        }
        Assert.IsTrue(found, "방패 줄");

        Animator animator = prefab.GetComponentInChildren<Animator>(true);
        var controller = animator != null ? animator.runtimeAnimatorController as AnimatorController : null;
        Assert.IsNotNull(controller, "팔라딘 AnimatorController");
        bool hasState = false;
        foreach (ChildAnimatorState s in controller.layers[0].stateMachine.states)
            hasState |= s.state.name == PaladinShieldGripAuthoring.UltimateState;
        Assert.IsTrue(hasState, "궁 상태가 0층에 있다");
    }
}
