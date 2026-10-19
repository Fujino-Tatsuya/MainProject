using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 판 도중 재합류 스폰 지점(C14 Q-A) — 처음 스폰과 같은 칸 공식, 다른 플레이어가 없는 가장 앞 칸, 결정적. 보스 룸 모서리는 BossRoomRejoinTests.
/// </summary>
public sealed class RejoinSpawnPickerTests
{
    static readonly Vector3 Base = new Vector3(100f, 2f, -50f);
    static readonly Quaternion BaseRotation = Quaternion.Euler(0f, 90f, 0f);
    static readonly Vector3 Offset = new Vector3(0f, 10f, 0f);

    static RejoinSpawnContext Context(bool inBossRoom = false, bool hasBase = true, Vector3[] others = null)
    {
        return new RejoinSpawnContext(inBossRoom, hasBase, Base, BaseRotation, Offset, new List<Vector3>(others ?? new Vector3[0]));
    }

    // 칸 i 에 서 있는 사람(낙하 뒤 바닥 높이 — y 는 보지 않는다).
    static Vector3 StandingOn(int slot) => Base + Vector3.right * (slot * 2f);

    [Test]
    public void 칸_공식은_처음_스폰과_같다()
    {
        var pose = RejoinSpawnPicker.SlotPose(true, Base, BaseRotation, Offset, 2);
        Assert.That(pose.position, Is.EqualTo(Base + Offset + Vector3.right * 4f));
        Assert.That(pose.rotation, Is.EqualTo(BaseRotation));

        var fallback = RejoinSpawnPicker.SlotPose(false, Base, BaseRotation, Offset, 1);
        Assert.That(fallback.position, Is.EqualTo(new Vector3(2f, 5f, 0f)), "기준점이 없으면 처음 스폰 폴백 좌표");
        Assert.That(fallback.rotation, Is.EqualTo(Quaternion.identity));
    }

    [Test]
    public void 아무도_없으면_첫_칸()
    {
        var pick = RejoinSpawnPicker.Pick(Context());
        Assert.That(pick.SlotIndex, Is.EqualTo(0));
        Assert.That(pick.Pose.position, Is.EqualTo(Base + Offset));
    }

    [Test]
    public void 다른_사람이_멀리_있으면_첫_칸()
    {
        Assert.That(RejoinSpawnPicker.Pick(Context(others: new[] { new Vector3(500f, 0f, 0f) })).SlotIndex, Is.EqualTo(0));
    }

    [Test]
    public void 첫_칸에_누가_서_있으면_다음_빈_칸()
    {
        Assert.That(RejoinSpawnPicker.Pick(Context(others: new[] { StandingOn(0) })).SlotIndex, Is.EqualTo(1));
        Assert.That(RejoinSpawnPicker.Pick(Context(others: new[] { StandingOn(0), StandingOn(1) })).SlotIndex, Is.EqualTo(2));
        Assert.That(RejoinSpawnPicker.Pick(Context(others: new[] { StandingOn(1), StandingOn(0) })).SlotIndex, Is.EqualTo(2), "순서와 무관");
    }

    [Test]
    public void 가운데가_비면_그_칸()
    {
        Assert.That(RejoinSpawnPicker.Pick(Context(others: new[] { StandingOn(0), StandingOn(2) })).SlotIndex, Is.EqualTo(1));
    }

    [Test]
    public void 높이는_보지_않고_수평_반경_안이면_찬_칸()
    {
        var hovering = StandingOn(0) + new Vector3(0.5f, 30f, 0.5f);
        Assert.That(RejoinSpawnPicker.Pick(Context(others: new[] { hovering })).SlotIndex, Is.EqualTo(1));

        var justOutside = StandingOn(0) + new Vector3(0f, 0f, RejoinSpawnPicker.OccupiedRadius);
        Assert.That(RejoinSpawnPicker.Pick(Context(others: new[] { justOutside })).SlotIndex, Is.EqualTo(0), "반경 경계는 비어 있다");
    }

    [Test]
    public void 기준점이_없어도_폴백_칸으로_빈_칸을_찾는다()
    {
        var pick = RejoinSpawnPicker.Pick(Context(hasBase: false, others: new[] { new Vector3(0f, 0f, 0f) }));
        Assert.That(pick.SlotIndex, Is.EqualTo(1));
        Assert.That(pick.Pose.position, Is.EqualTo(new Vector3(2f, 5f, 0f)));
    }

    [Test]
    public void 같은_입력이면_같은_결과다()
    {
        var context = Context(others: new[] { StandingOn(0), new Vector3(7f, 0f, 3f) });
        var first = RejoinSpawnPicker.Pick(context);
        var second = RejoinSpawnPicker.Pick(context);
        Assert.That(second.SlotIndex, Is.EqualTo(first.SlotIndex));
        Assert.That(second.Pose.position, Is.EqualTo(first.Pose.position));
    }

    [Test]
    public void 보스_룸이어도_모서리_입력이_없으면_첫_스폰_공식이다()
    {
        // 모서리 선택은 BossRoomRejoinTests(R3c).
        var pick = RejoinSpawnPicker.Pick(Context(inBossRoom: true));
        Assert.That(pick.Pose.position, Is.EqualTo(Base + Offset));
    }

    [Test]
    public void 다른_사람_목록이_null_이어도_첫_칸()
    {
        var context = new RejoinSpawnContext(false, true, Base, BaseRotation, Offset, null);
        Assert.That(RejoinSpawnPicker.Pick(context).SlotIndex, Is.EqualTo(0));
    }
}
