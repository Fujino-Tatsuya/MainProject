using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>카메라 컷 기준 '처치자 무기'(KillerWeapon) — 좌표계·폴백·부드러움·무기 찾기.</summary>
public sealed class BossVictoryWeaponAnchorTests
{
    const float Tol = 1e-4f;
    static readonly BossVictoryCameraCuts.Anchor W = BossVictoryCameraCuts.Anchor.KillerWeapon;

    static BossVictoryCameraCuts.Subjects S(Vector3 killer, Vector3 boss, Vector3? weapon) => new BossVictoryCameraCuts.Subjects
    {
        Killer = killer, BossBase = boss, HasWeapon = weapon.HasValue, Weapon = weapon ?? default,
    };

    [Test]
    public void 원점은_무기_축은_처치자에서_보스()
    {
        var s = S(new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 11f), new Vector3(1.3f, 1.2f, 1.1f));
        BossVictoryCameraCuts.Frame(W, s, out Vector3 origin, out Quaternion frame);
        Assert.Less(Vector3.Distance(origin, s.Weapon), Tol);
        Assert.Less(Vector3.Distance(frame * Vector3.forward, Vector3.forward), Tol, "+Z = 처치자→보스");
        // 키 (0.5, 0.2, -1) = 총 오른쪽 0.5 · 위 0.2 · 뒤 1
        BossVictoryCameraCuts.ToWorld(W, s, new Vector3(0.5f, 0.2f, -1f), Quaternion.identity, out Vector3 p, out _);
        Assert.Less(Vector3.Distance(p, s.Weapon + new Vector3(0.5f, 0.2f, -1f)), Tol);
        BossVictoryCameraCuts.ToLocal(W, s, p, Quaternion.Euler(5f, 10f, 0f), out Vector3 lp, out Quaternion lr);
        Assert.Less(Vector3.Distance(lp, new Vector3(0.5f, 0.2f, -1f)), Tol, "왕복");
        Assert.Less(Quaternion.Angle(lr, Quaternion.Euler(5f, 10f, 0f)), 0.01f);
    }

    [Test]
    public void 무기가_없으면_처치자_발밑()
    {
        var s = S(new Vector3(2f, 0f, 3f), new Vector3(5f, 0f, 3f), null);
        Assert.Less(Vector3.Distance(BossVictoryCameraCuts.AnchorPoint(W, s), s.Killer), Tol);
    }

    [Test]
    public void 기존_기준은_무기와_무관_매_프레임은_무기만()
    {
        var a = S(Vector3.zero, new Vector3(0f, 0f, 10f), new Vector3(9f, 9f, 9f));
        var b = S(Vector3.zero, new Vector3(0f, 0f, 10f), null);
        foreach (var anchor in new[] { BossVictoryCameraCuts.Anchor.Middle, BossVictoryCameraCuts.Anchor.Killer, BossVictoryCameraCuts.Anchor.Boss })
        {
            Assert.AreEqual(BossVictoryCameraCuts.AnchorPoint(anchor, b), BossVictoryCameraCuts.AnchorPoint(anchor, a));
            Assert.IsFalse(BossVictoryCameraCuts.FollowsEveryFrame(anchor));
        }
        Assert.IsTrue(BossVictoryCameraCuts.FollowsEveryFrame(W));
    }

    [Test]
    public void 부드러움_0은_딱_붙고_값이_있으면_지수로_따라간다()
    {
        Vector3 from = Vector3.zero, to = new Vector3(1f, 0f, 0f);
        Assert.AreEqual(to, BossVictoryCameraCuts.SmoothFollow(from, to, 0f, 0.016f));
        Vector3 one = BossVictoryCameraCuts.SmoothFollow(from, to, 0.2f, 0.2f);
        Assert.AreEqual(1f - Mathf.Exp(-1f), one.x, Tol, "시간 상수만큼 지나면 63%");
        Vector3 split = from;
        for (int i = 0; i < 10; i++) split = BossVictoryCameraCuts.SmoothFollow(split, to, 0.2f, 0.02f);
        Assert.AreEqual(one.x, split.x, Tol, "프레임 속도와 무관");
    }

    [Test]
    public void 매_프레임_추종은_처치자_보스는_지금_무기는_부드럽게()
    {
        var prev = S(Vector3.zero, new Vector3(0f, 0f, 10f), Vector3.zero);
        var live = S(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, 12f), new Vector3(2f, 0f, 0f));
        var s = BossVictoryCameraCuts.FollowWeapon(prev, live, 0.5f, 0.1f);
        Assert.AreEqual(live.Killer, s.Killer);
        Assert.AreEqual(live.BossBase, s.BossBase);
        Assert.Greater(s.Weapon.x, 0f);
        Assert.Less(s.Weapon.x, 2f);
        var noPrev = S(Vector3.zero, Vector3.forward, null);
        Assert.AreEqual(live.Weapon, BossVictoryCameraCuts.FollowWeapon(noPrev, live, 0.5f, 0.1f).Weapon, "무기가 막 생기면 바로 맞춤");
        Assert.IsFalse(BossVictoryCameraCuts.FollowWeapon(prev, noPrev, 0.5f, 0.1f).HasWeapon, "무기가 없어지면 폴백");
    }

    [Test]
    public void 무기_찾기는_표식_먼저_다음_relay_첫_무기()
    {
        var root = new GameObject("Killer");
        try
        {
            Assert.IsNull(BossVictoryWeaponMarker.Find(root.transform), "없으면 null(폴백)");
            var slot = new GameObject("Slot").transform;
            slot.SetParent(root.transform, false);
            var sword = new GameObject("Sword").transform;
            sword.SetParent(slot, false);
            var socket = new GameObject("Socket").transform;
            socket.SetParent(root.transform, false);
            var relay = slot.gameObject.AddComponent<WeaponTransformRelay>();
            var so = new SerializedObject(relay);
            SerializedProperty follows = so.FindProperty("follows");
            follows.arraySize = 1;
            follows.GetArrayElementAtIndex(0).FindPropertyRelative("target").objectReferenceValue = socket;
            follows.GetArrayElementAtIndex(0).FindPropertyRelative("weapon").objectReferenceValue = sword;
            so.ApplyModifiedPropertiesWithoutUndo();
            Assert.AreSame(sword, BossVictoryWeaponMarker.Find(root.transform), "relay 첫 무기");

            var gun = new GameObject("Gun").transform;
            gun.SetParent(root.transform, false);
            gun.gameObject.AddComponent<BossVictoryWeaponMarker>();
            Assert.AreSame(gun, BossVictoryWeaponMarker.Find(root.transform), "표식이 먼저");
        }
        finally { Object.DestroyImmediate(root); }
    }

    [Test]
    public void 거너_프리팹_총에_표식()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossVictoryWeaponMarkerAuthoring.GunnerPrefabPath);
        Assert.IsNotNull(prefab);
        Transform gun = BossVictoryWeaponMarker.Find(prefab.transform);
        Assert.IsNotNull(gun, "거너 무기");
        Assert.AreEqual(BossVictoryWeaponMarkerAuthoring.GunnerWeaponName, gun.name);
    }
}
