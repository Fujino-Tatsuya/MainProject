using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// L00 라커 방문 — 할 일 157 커밋 ③. 영역 볼륨(본체 기준점·높이 범위·회전·쓰러짐) + 모델 누적 방문(시작 스냅샷·재출입 1회·연결 종료·스폰 완료 참여자·N=0).
/// </summary>
public sealed class LobbyLockerVisitTests
{
    const int Run = 3;

    static readonly LobbyTutorialStepKind[] Kinds =
    {
        LobbyTutorialStepKind.LockerVisit,
        LobbyTutorialStepKind.Gather,
        LobbyTutorialStepKind.Dialogue,
    };

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

    // 라커룸 = 바닥 y 0 기준 높이 3m, 4×5 footprint.
    LobbyAreaVolume CreateVolume(Vector3 position, Vector3 size, float yaw = 0f)
    {
        var go = new GameObject("Area_Locker_Test");
        _created.Add(go);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, size.y * 0.5f, 0f);
        box.size = size;
        return go.AddComponent<LobbyAreaVolume>();
    }

    static LobbyTutorialFlow Create(params ulong[] clientIds)
    {
        var flow = new LobbyTutorialFlow(Kinds, Run);
        foreach (var clientId in clientIds)
            flow.AddClient(clientId);
        return flow;
    }

    // ── 볼륨 판정 ──

    [Test]
    public void 본체_기준점이_footprint_안이면_안()
    {
        var volume = CreateVolume(new Vector3(10f, 0f, -4f), new Vector3(4f, 3f, 5f));

        Assert.That(volume.ContainsPoint(new Vector3(10f, 0f, -4f)), Is.True, "바닥 중심(본체 기준점 = 발밑)");
        Assert.That(volume.ContainsPoint(new Vector3(11.9f, 0.1f, -6.4f)), Is.True, "모서리 안");
        Assert.That(volume.ContainsPoint(new Vector3(12.1f, 0.1f, -4f)), Is.False, "x 밖(벽 너머 복도)");
        Assert.That(volume.ContainsPoint(new Vector3(10f, 0.1f, -1.4f)), Is.False, "z 밖(문 바깥)");
    }

    [Test]
    public void 높이_범위_밖_다른_층은_불인정()
    {
        var volume = CreateVolume(Vector3.zero, new Vector3(4f, 3f, 5f));

        Assert.That(volume.ContainsPoint(new Vector3(0f, 2.9f, 0f)), Is.True);
        Assert.That(volume.ContainsPoint(new Vector3(0f, 4.5f, 0f)), Is.False, "위층(같은 footprint)");
        Assert.That(volume.ContainsPoint(new Vector3(0f, -1f, 0f)), Is.False, "아래층·낙하");
    }

    [Test]
    public void 회전한_볼륨도_로컬_박스로_판정()
    {
        var volume = CreateVolume(Vector3.zero, new Vector3(2f, 3f, 8f), yaw: 90f);   // 긴 변이 월드 x 축

        Assert.That(volume.ContainsPoint(new Vector3(3.5f, 0.5f, 0f)), Is.True);
        Assert.That(volume.ContainsPoint(new Vector3(0f, 0.5f, 3.5f)), Is.False);
    }

    [Test]
    public void 쓰러짐은_안에_있어도_세지_않는다()
    {
        var volume = CreateVolume(Vector3.zero, new Vector3(4f, 3f, 5f));

        Assert.That(volume.Counts(new Vector3(0f, 0.2f, 0f), incapacitated: false), Is.True);
        Assert.That(volume.Counts(new Vector3(0f, 0.2f, 0f), incapacitated: true), Is.False);
        Assert.That(volume.CountsPlayer(null), Is.False);
    }

    [Test]
    public void 로컬_박스_경계는_포함()
    {
        Assert.That(LobbyAreaVolume.ContainsLocal(new Vector3(1f, 0f, 0f), Vector3.zero, new Vector3(2f, 2f, 2f)), Is.True);
        Assert.That(LobbyAreaVolume.ContainsLocal(new Vector3(1.01f, 0f, 0f), Vector3.zero, new Vector3(2f, 2f, 2f)), Is.False);
    }

    // ── 누적 방문(모델) ──

    [Test]
    public void 시작_때_이미_안에_있던_사람도_첫_폴링에_인정()
    {
        var flow = Create(0, 1);

        // 진입 직후(전이 없이) 첫 폴링 — 이미 라커룸 안
        Assert.That(flow.MarkLockerVisited(0), Is.True);
        Assert.That(flow.HasVisitedLocker(0), Is.True);
    }

    [Test]
    public void 재출입은_1회만_세고_나가도_기록_유지()
    {
        var flow = Create(0, 1);
        flow.MarkLockerVisited(0);

        Assert.That(flow.MarkLockerVisited(0), Is.False, "다시 들어와도 중복 없음");
        Assert.That(flow.LockerVisited.Count, Is.EqualTo(1));
        Assert.That(flow.AllSpawnedParticipantsVisitedLocker(_ => true), Is.False, "1 은 아직");

        flow.MarkLockerVisited(1);
        Assert.That(flow.AllSpawnedParticipantsVisitedLocker(_ => true), Is.True, "0 은 나갔어도 누적");
    }

    [Test]
    public void 연결_종료는_방문_기록에서_빠지고_남은_사람으로_재평가()
    {
        var flow = Create(0, 1);
        flow.MarkLockerVisited(0);

        Assert.That(flow.RemoveClient(0), Is.True);
        Assert.That(flow.HasVisitedLocker(0), Is.False);
        Assert.That(flow.AllSpawnedParticipantsVisitedLocker(_ => true), Is.False);

        flow.MarkLockerVisited(1);
        Assert.That(flow.AllSpawnedParticipantsVisitedLocker(_ => true), Is.True);
    }

    [Test]
    public void 스폰_전_접속자는_세지_않고_스폰_완료_0명이면_완료_아님()
    {
        var flow = Create(0, 1);
        flow.MarkLockerVisited(0);

        Assert.That(flow.AllSpawnedParticipantsVisitedLocker(id => id == 0), Is.True, "1 은 로딩 중 — 세지 않음");
        Assert.That(flow.AllSpawnedParticipantsVisitedLocker(_ => false), Is.False, "스폰 완료 0명 = N=0 → 전이 금지");
        Assert.That(flow.AllSpawnedParticipantsVisitedLocker(_ => true), Is.False, "1 이 스폰되면 기다린다");
    }

    [Test]
    public void 접속자_0명이면_라커_완료도_전이도_없다()
    {
        var flow = Create();

        Assert.That(flow.AllSpawnedParticipantsVisitedLocker(_ => true), Is.False);
        Assert.That(flow.TryAdvance(Run, 1, 10, 0), Is.EqualTo(LobbyTutorialAdvanceResult.NoParticipants));
    }

    [Test]
    public void 라커_단계가_지나면_방문을_더_받지_않는다_회차는_새_모델로_초기화()
    {
        var flow = Create(0, 1);
        flow.MarkLockerVisited(0);
        flow.TryAdvance(Run, 1, 10, 0);

        Assert.That(flow.MarkLockerVisited(1), Is.False, "L10 부터는 기록 안 함");
        Assert.That(flow.HasVisitedLocker(0), Is.True, "같은 회차 안에서는 유지");

        var next = new LobbyTutorialFlow(Kinds, Run + 1);
        next.AddClient(0);
        Assert.That(next.HasVisitedLocker(0), Is.False, "새 회차 = 빈 기록");
    }

    [Test]
    public void 캐릭터_교체는_방문_기록을_지우지_않는다()
    {
        var flow = Create(0);
        flow.MarkLockerVisited(0);

        // 교체 = 같은 clientId 의 PlayerObject 디스폰·재스폰 — 모델엔 AddClient/RemoveClient 가 오지 않는다(재접속 아님).
        Assert.That(flow.AddClient(0), Is.False, "같은 clientId 는 새 접속이 아니다");
        Assert.That(flow.HasVisitedLocker(0), Is.True);
        Assert.That(flow.MarkLockerVisited(0), Is.False, "교체 뒤 라커 안이어도 중복 없음");
    }
}
