using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// 할 일 145 — 특성 강화 연출 사건(<see cref="TraitSkillFx"/>)의 순수 부분: 특성 종류 ↔ 명세 ID, 구독자 예외 격리, 공용 RPC 메시지 직렬화.
/// </summary>
public sealed class TraitSkillFxTests
{
    [TearDown]
    public void TearDown() => ClearSubscribers();

    [Test]
    public void EveryTrait_MapsToCatalogId_AndParsesBack()
    {
        foreach (TraitFxTrait trait in (TraitFxTrait[])Enum.GetValues(typeof(TraitFxTrait)))
        {
            string id = TraitSkillFx.IdOf(trait);
            Assert.IsTrue(TraitCatalog.TryGetSlot(id, out _), $"{trait} → '{id}' 가 명세 고정표에 없다");
            Assert.IsTrue(TraitSkillFx.TryParse(id, out TraitFxTrait parsed));
            Assert.AreEqual(trait, parsed);
        }

        Assert.IsFalse(TraitSkillFx.TryParse("PAL_1_Q_DAMAGE", out _), "연출 사건이 없는 특성은 false");
    }

    [Test]
    public void Raise_ThrowingSubscriber_DoesNotStopOthers()
    {
        var received = new List<TraitFxEvent>();
        Action<TraitFxEvent> thrower = _ => throw new InvalidOperationException("구독자 오류");
        Action<TraitFxEvent> listener = e => received.Add(e);
        TraitSkillFx.Raised += thrower;
        TraitSkillFx.Raised += listener;

        LogAssert.Expect(LogType.Exception, "InvalidOperationException: 구독자 오류");
        var e = new TraitFxEvent(TraitFxTrait.GunnerEchoBomb, TraitFxKind.Attached, null, Vector3.one, Vector3.forward, Vector3.one, variant: 7);
        TraitSkillFx.Raise(e);

        Assert.AreEqual(1, received.Count);
        Assert.AreEqual(TraitFxKind.Attached, received[0].Kind);
        Assert.AreEqual("GUN_4_ECHO_Q", received[0].TraitId);
        Assert.AreEqual(7, received[0].Variant);
        Assert.IsFalse(received[0].HasShape);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Message_RoundTrips(bool hasTarget)
    {
        var pose = new AttackShapePose(new Vector3(1f, 0f, 2f), new Vector3(0f, 0f, 3f));
        var sent = new TraitSkillFxMessage(TraitFxTrait.AssassinPursuitTrail, TraitFxKind.Hit, new Vector3(1f, 2f, 3f), Vector3.right,
            new Vector3(4f, 5f, 6f), pose, hasTarget ? 42UL : 0UL, hasTarget, 2);

        using var writer = new FastBufferWriter(256, Allocator.Temp);
        writer.WriteNetworkSerializable(sent);
        using var reader = new FastBufferReader(writer, Allocator.Temp);
        reader.ReadNetworkSerializable(out TraitSkillFxMessage got);

        Assert.AreEqual(sent.Trait, got.Trait);
        Assert.AreEqual(sent.Kind, got.Kind);
        Assert.AreEqual(sent.Position, got.Position);
        Assert.AreEqual(sent.Direction, got.Direction);
        Assert.AreEqual(sent.End, got.End);
        Assert.AreEqual(pose.Position, got.Pose.Position);
        Assert.AreEqual(pose.Forward, got.Pose.Forward);
        Assert.AreEqual(hasTarget, got.HasTarget);
        Assert.AreEqual(hasTarget ? 42UL : 0UL, got.TargetId);
        Assert.AreEqual(2, got.Variant);
    }

    static void ClearSubscribers() =>
        typeof(TraitSkillFx).GetMethod("ResetStaticEvents", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.Invoke(null, null);
}
