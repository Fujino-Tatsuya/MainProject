using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 캐릭터 선택 = 영역 방식 — 할 일 161(157 ③-b). LobbyInteractable 의 area 칸(Area_Locker 와 같은 볼륨):
/// 클라 사정거리(안/밖·높이), 고르기 후보, 서버 재검증 여유(footprint 만 넓힘, 높이 엄격), area 비면 옛 반경 그대로.
/// </summary>
public sealed class LobbyCharacterSelectAreaTests
{
    readonly List<GameObject> _created = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        foreach (var go in _created)
        {
            if (go != null)
                Object.DestroyImmediate(go);
        }

        _created.Clear();
    }

    // 라커룸 = 바닥 y 0 기준 높이 3m, 4×6 footprint, 중심 (-8, 0, 2).
    LobbyAreaVolume CreateVolume(float yaw = 0f)
    {
        var go = new GameObject("Area_Locker_Test");
        _created.Add(go);
        go.transform.SetPositionAndRotation(new Vector3(-8f, 0f, 2f), Quaternion.Euler(0f, yaw, 0f));
        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 1.5f, 0f);
        box.size = new Vector3(4f, 3f, 6f);
        return go.AddComponent<LobbyAreaVolume>();
    }

    LobbyInteractable CreateStation(Vector3 position, LobbyAreaVolume area, float radiusOverride = 0f)
    {
        var go = new GameObject("CharacterSelectStation_Test");
        _created.Add(go);
        go.transform.position = position;
        var station = go.AddComponent<LobbyInteractable>();
        var so = new SerializedObject(station);
        so.FindProperty("kind").enumValueIndex = (int)LobbyInteractableKind.CharacterSelect;
        so.FindProperty("area").objectReferenceValue = area;
        so.FindProperty("radiusOverride").floatValue = radiusOverride;
        so.ApplyModifiedPropertiesWithoutUndo();
        return station;
    }

    // ── 클라 사정거리(프롬프트·안내 스텝) ──

    [Test]
    public void 영역_안이면_사정거리_안_스테이션에서_멀어도()
    {
        var station = CreateStation(new Vector3(-8f, 0f, 2f), CreateVolume());

        Assert.That(station.IsInReach(new Vector3(-6.2f, 0f, 4.8f), 0f), Is.True, "박스 모서리 근처 — 스테이션 반경 2m 밖이어도 영역이면 안");
        Assert.That(station.IsInReach(new Vector3(-8f, 0f, 2f), 0f), Is.True);
    }

    [Test]
    public void 영역_밖이면_스테이션_가까워도_밖()
    {
        // 스테이션을 영역 경계 바로 옆에 둬도 판정은 영역 하나.
        var station = CreateStation(new Vector3(-6.1f, 0f, 2f), CreateVolume());

        Assert.That(station.IsInReach(new Vector3(-5.5f, 0f, 2f), 0f), Is.False, "스테이션에서 0.6m 지만 박스 밖");
    }

    [Test]
    public void 높이_범위_밖이면_밖()
    {
        var station = CreateStation(new Vector3(-8f, 0f, 2f), CreateVolume());

        Assert.That(station.IsInReach(new Vector3(-8f, 3.5f, 2f), 0f), Is.False, "위층(박스 높이 3m 위)");
        Assert.That(station.IsInReach(new Vector3(-8f, -0.5f, 2f), 0f), Is.False, "아래층");
        Assert.That(station.IsInReach(new Vector3(-8f, 2.9f, 2f), 0f), Is.True);
    }

    [Test]
    public void 회전된_영역도_로컬_footprint로_판정()
    {
        var station = CreateStation(new Vector3(-8f, 0f, 2f), CreateVolume(90f));

        // 90° 회전 → 월드 x 반폭 3, z 반폭 2.
        Assert.That(station.IsInReach(new Vector3(-8f + 2.8f, 0f, 2f), 0f), Is.True);
        Assert.That(station.IsInReach(new Vector3(-8f, 0f, 2f + 2.5f), 0f), Is.False);
    }

    [Test]
    public void 고르기_후보_영역_안이면_골라지고_밖이면_안_골라짐()
    {
        var station = CreateStation(new Vector3(-8f, 0f, 2f), CreateVolume());
        var inside = new Vector3(-6.2f, 0f, 4.8f);
        var outside = new Vector3(-5f, 0f, 2f);

        Assert.That(NearestInteractable.Pick(inside, new[] { station.ToCandidate(inside, true) }), Is.EqualTo(0));
        Assert.That(NearestInteractable.Pick(outside, new[] { station.ToCandidate(outside, true) }), Is.EqualTo(-1));
        Assert.That(NearestInteractable.Pick(inside, new[] { station.ToCandidate(inside, false) }), Is.EqualTo(-1), "쓸 수 없으면(패널 없음 등) 안이어도 안 고름");
    }

    // ── 서버 재검증 여유 ──

    [Test]
    public void 서버_재검증은_footprint만_여유만큼_넓힌다()
    {
        var station = CreateStation(new Vector3(-8f, 0f, 2f), CreateVolume());
        var tol = LobbyInteractable.ServerReachTolerance;

        // x 경계 = -6. 경계 밖 여유 안 / 여유 밖.
        Assert.That(station.IsInReach(new Vector3(-6f + tol * 0.8f, 0f, 2f), tol), Is.True, "경계 + 여유 안");
        Assert.That(station.IsInReach(new Vector3(-6f + tol * 0.8f, 0f, 2f), 0f), Is.False, "클라 기준으론 밖");
        Assert.That(station.IsInReach(new Vector3(-6f + tol * 1.5f, 0f, 2f), tol), Is.False, "여유 밖");
        Assert.That(station.IsInReach(new Vector3(-8f, 3f + tol * 0.5f, 2f), tol), Is.False, "높이는 여유 없음");
    }

    [Test]
    public void 서버_AnyWithinServerReach_영역_기준()
    {
        // EditMode 에선 OnEnable 이 돌지 않아 레지스트리가 비어 있다 — 직접 등록·해제한다.
        var station = CreateStation(new Vector3(-8f, 0f, 2f), CreateVolume());
        CallLifecycle(station, "OnEnable");
        try
        {
            Assert.That(LobbyInteractable.AnyWithinServerReach(LobbyInteractableKind.CharacterSelect, new Vector3(-6.2f, 0f, 4.8f), out _), Is.True);
            Assert.That(LobbyInteractable.AnyWithinServerReach(LobbyInteractableKind.CharacterSelect, new Vector3(5f, 0f, -6f), out var distance), Is.False, "옛 중앙 스테이션 자리");
            Assert.That(distance, Is.LessThan(float.MaxValue));
        }
        finally
        {
            CallLifecycle(station, "OnDisable");
        }
    }

    static void CallLifecycle(LobbyInteractable station, string method)
    {
        typeof(LobbyInteractable)
            .GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(station, null);
    }

    [Test]
    public void 영역_스케일이_있어도_여유는_월드_m()
    {
        var volume = CreateVolume();
        volume.transform.localScale = new Vector3(2f, 1f, 2f);   // footprint 8×12, x 경계 = -4
        var station = CreateStation(new Vector3(-8f, 0f, 2f), volume);

        Assert.That(station.IsInReach(new Vector3(-4f + 0.4f, 0f, 2f), 0.5f), Is.True);
        Assert.That(station.IsInReach(new Vector3(-4f + 0.7f, 0f, 2f), 0.5f), Is.False, "여유가 스케일로 두 배가 되지 않는다");
    }

    // ── 회귀: area 비면 옛 반경 ──

    [Test]
    public void 영역이_비면_옛_평면_반경_그대로()
    {
        var station = CreateStation(new Vector3(5f, 0f, -6f), null, 2f);

        Assert.That(station.IsInReach(new Vector3(6.9f, 5f, -6f), 0f), Is.True, "평면 거리 — 높이 무시");
        Assert.That(station.IsInReach(new Vector3(7.3f, 0f, -6f), 0f), Is.False);
        Assert.That(station.IsInReach(new Vector3(7.3f, 0f, -6f), LobbyInteractable.ServerReachTolerance), Is.True);
        var p = new Vector3(6.9f, 0f, -6f);
        Assert.That(NearestInteractable.Pick(p, new[] { station.ToCandidate(p, true) }), Is.EqualTo(0));
    }
}
