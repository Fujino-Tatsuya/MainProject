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
    public void 손잡이1을_제자리에_둔_채_돈다()
    {
        Vector3 origin = new Vector3(1f, 2f, 3f);
        Quaternion rot = Quaternion.Euler(10f, 20f, 30f);
        Vector3 grip1Local = new Vector3(0.05f, -0.3f, 0.02f);   // 손잡이1 이 방패 원점에서 떨어진 경우
        Vector3 elbow = origin + new Vector3(0.4f, 0.6f, -0.2f);
        foreach (Vector3 grip2Local in new[] { new Vector3(0f, 0f, 0.5f), new Vector3(-0.1f, 0.2f, 0.3f), new Vector3(0.3f, -0.1f, -0.2f) })
        {
            Vector3 grip1 = origin + rot * grip1Local;
            Vector3 grip2 = origin + rot * grip2Local;
            WeaponTransformRelay.AlignAboutPivot(origin, rot, grip1, grip2, elbow, 1f, out Vector3 pos, out Quaternion newRot);
            Assert.Less(Vector3.Distance(grip1, pos + newRot * grip1Local), Tolerance, $"손잡이2 {grip2Local} — 손잡이1 월드 위치 그대로");
            Vector3 dir = (pos + newRot * grip2Local - grip1).normalized;
            Assert.AreEqual(1f, Vector3.Dot(dir, (elbow - grip1).normalized), Tolerance, "손잡이1→손잡이2 가 손잡이1→ElbowSocket 쪽");
            WeaponTransformRelay.AlignAboutPivot(origin, rot, grip1, grip2, elbow, 0.5f, out pos, out newRot);
            Assert.Less(Vector3.Distance(grip1, pos + newRot * grip1Local), Tolerance, "가중치 중간에도 손잡이1 그대로");
        }
    }

    [Test]
    public void 손잡이2_마커를_옮겨도_손잡이1은_손_소켓_자리에()
    {
        var rig = Rig();
        try
        {
            var primary = new GameObject(PaladinShieldGripAuthoring.PrimaryHandleName).transform;
            primary.SetParent(rig.shield, false);
            primary.localPosition = new Vector3(0.05f, -0.2f, 0.1f);
            var so = new SerializedObject(rig.relay);
            so.FindProperty("follows").GetArrayElementAtIndex(0).FindPropertyRelative("primaryHandle").objectReferenceValue = primary;
            so.ApplyModifiedPropertiesWithoutUndo();
            rig.socket.parent.SetPositionAndRotation(new Vector3(0.3f, 1f, -0.5f), Quaternion.Euler(0f, 40f, 15f));

            rig.relay.previewWeight = 0f;
            rig.relay.ApplyNow();
            Vector3 before = primary.position;   // 보정 전 = 손 소켓 기준 자리
            Assert.IsTrue(rig.relay.TryGetGrip(out Transform socket, out Transform p));
            Assert.AreSame(rig.socket, socket);
            Assert.AreSame(primary, p);

            foreach (Vector3 grip2Local in new[] { new Vector3(0f, 0f, 0.5f), new Vector3(0.2f, 0.3f, 0.1f), new Vector3(-0.4f, 0.1f, -0.3f) })
            {
                rig.handle.localPosition = grip2Local;
                rig.relay.previewWeight = 1f;
                rig.relay.ApplyNow();
                Assert.Less(Vector3.Distance(before, primary.position), Tolerance, $"손잡이2 {grip2Local} — 손잡이1 이 손에서 안 떨어짐");
                Vector3 dir = (rig.handle.position - primary.position).normalized;
                Assert.AreEqual(1f, Vector3.Dot(dir, (rig.elbow.position - primary.position).normalized), Tolerance, "방향만 맞춘다");
            }
        }
        finally { Object.DestroyImmediate(rig.root); }
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
        Transform primary = shield.Find(PaladinShieldGripAuthoring.PrimaryHandleName);
        Assert.IsNotNull(primary, "방패 자식 손잡이1");
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
            Assert.AreEqual(primary, f.FindPropertyRelative("primaryHandle").objectReferenceValue);
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

    // ── 미리보기(보면서 세팅) ──

    static (GameObject root, Transform socket, Transform shield, Transform handle, Transform elbow, WeaponTransformRelay relay) Rig()
    {
        var root = new GameObject("GripRig");
        var bone = new GameObject("bone").transform;
        bone.SetParent(root.transform, false);
        var socket = new GameObject(PaladinShieldGripAuthoring.HandSocketName).transform;
        socket.SetParent(bone, false);
        var elbow = new GameObject(PaladinShieldGripAuthoring.ElbowSocketName).transform;
        elbow.SetParent(bone, false);
        elbow.localPosition = new Vector3(1f, 0f, 0f);
        var slot = new GameObject("PlayerWaeponSlot").transform;
        slot.SetParent(root.transform, false);
        var shield = new GameObject(PaladinShieldGripAuthoring.ShieldName).transform;
        shield.SetParent(slot, false);
        var handle = new GameObject(PaladinShieldGripAuthoring.HandleName).transform;
        handle.SetParent(shield, false);
        handle.localPosition = new Vector3(0f, 0f, 0.5f);
        var relay = slot.gameObject.AddComponent<WeaponTransformRelay>();
        var so = new SerializedObject(relay);
        SerializedProperty follows = so.FindProperty("follows");
        follows.arraySize = 1;
        SerializedProperty f = follows.GetArrayElementAtIndex(0);
        f.FindPropertyRelative("target").objectReferenceValue = socket;
        f.FindPropertyRelative("weapon").objectReferenceValue = shield;
        f.FindPropertyRelative("secondaryHandle").objectReferenceValue = handle;
        f.FindPropertyRelative("secondaryTarget").objectReferenceValue = elbow;
        f.FindPropertyRelative("secondaryState").stringValue = PaladinShieldGripAuthoring.UltimateState;
        f.FindPropertyRelative("secondaryBlend").floatValue = 0.1f;
        so.ApplyModifiedPropertiesWithoutUndo();
        return (root, socket, shield, handle, elbow, relay);
    }

    [Test]
    public void 미리보기_가중치로_보정을_켜고_끈다()
    {
        var rig = Rig();
        try
        {
            rig.relay.previewWeight = 1f;
            rig.relay.ApplyNow();
            Vector3 dir = (rig.handle.position - rig.shield.position).normalized;
            Assert.AreEqual(1f, Vector3.Dot(dir, Vector3.right), Tolerance, "켜기 = 손잡이2 가 ElbowSocket 쪽");
            rig.relay.previewWeight = 0f;
            rig.relay.ApplyNow();
            Assert.AreEqual(0f, Quaternion.Angle(rig.socket.rotation, rig.shield.rotation), Tolerance, "끄기 = 손 소켓 그대로");
            rig.relay.previewWeight = -1f;
            rig.relay.ApplyNow();
            Assert.AreEqual(0f, Quaternion.Angle(rig.socket.rotation, rig.shield.rotation), Tolerance, "미리보기 아님(Play 아님) = 보정 없음");
            Assert.IsTrue(rig.relay.TryGetSecondary(out Transform w, out Transform h, out Transform e, out float blend));
            Assert.AreSame(rig.shield, w);
            Assert.AreSame(rig.handle, h);
            Assert.AreSame(rig.elbow, e);
            Assert.AreEqual(0.1f, blend, Tolerance);
        }
        finally { Object.DestroyImmediate(rig.root); }
    }

    [Test]
    public void 프리팹에_저장할_값을_손잡이2_ElbowSocket_블렌드에_넣는다()
    {
        var rig = Rig();
        try
        {
            Assert.IsTrue(PaladinShieldGripAuthoring.ApplyGrip(rig.root, new Vector3(0.1f, 0.2f, 0.3f), new Vector3(-0.4f, 0f, 0.05f), 0.25f));
            Assert.AreEqual(new Vector3(0.1f, 0.2f, 0.3f), rig.handle.localPosition);
            Assert.AreEqual(new Vector3(-0.4f, 0f, 0.05f), rig.elbow.localPosition);
            rig.relay.TryGetSecondary(out _, out _, out _, out float blend);
            Assert.AreEqual(0.25f, blend, Tolerance);
            Assert.IsFalse(PaladinShieldGripAuthoring.ApplyGrip(rig.root, Vector3.zero, Vector3.zero, 0.1f, Vector3.one), "손잡이1 값을 줬는데 마커 없으면 false");
            var primary = new GameObject(PaladinShieldGripAuthoring.PrimaryHandleName).transform;
            primary.SetParent(rig.shield, false);
            Assert.IsTrue(PaladinShieldGripAuthoring.ApplyGrip(rig.root, Vector3.zero, Vector3.zero, 0.1f, new Vector3(0.01f, -0.2f, 0f)));
            Assert.AreEqual(new Vector3(0.01f, -0.2f, 0f), primary.localPosition);
            Object.DestroyImmediate(rig.handle.gameObject);
            Assert.IsFalse(PaladinShieldGripAuthoring.ApplyGrip(rig.root, Vector3.zero, Vector3.zero, 0.1f), "손잡이2 없으면 false");
        }
        finally { Object.DestroyImmediate(rig.root); }
    }

    [Test]
    public void 미리보기를_끄면_애니_자세와_방패가_원래대로()
    {
        var rig = Rig();
        var clip = new AnimationClip();
        clip.SetCurve("bone", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 2f));
        Transform bone = rig.root.transform.Find("bone");
        Vector3 shieldPos = rig.shield.localPosition;
        Quaternion shieldRot = rig.shield.localRotation;
        try
        {
            PaladinShieldGripPreview.Start();
            PaladinShieldGripPreview.Sample(rig.root, clip, 0.5f, rig.shield);
            Assert.AreEqual(1f, bone.localPosition.x, Tolerance, "샘플링 = 클립 자세");
            rig.relay.previewWeight = 1f;
            rig.relay.ApplyNow();
            Assert.AreNotEqual(shieldPos, rig.shield.localPosition, "방패가 따라감");
        }
        finally
        {
            PaladinShieldGripPreview.Stop();
            rig.relay.previewWeight = -1f;
        }
        try
        {
            Assert.IsFalse(PaladinShieldGripPreview.Active);
            Assert.AreEqual(0f, bone.localPosition.x, Tolerance, "끄면 본 원복");
            Assert.AreEqual(shieldPos, rig.shield.localPosition, "방패 위치 원복");
            Assert.AreEqual(0f, Quaternion.Angle(shieldRot, rig.shield.localRotation), Tolerance, "방패 회전 원복");
        }
        finally
        {
            Object.DestroyImmediate(rig.root);
            Object.DestroyImmediate(clip);
        }
    }
}
