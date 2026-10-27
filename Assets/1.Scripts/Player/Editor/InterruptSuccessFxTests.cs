using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 간파 성공 이펙트 칸(할 일 87 C4) — 재생 여부 판단과 <c>Player.prefab</c>(base) 배선.
/// 판단은 할 일 118 에서 <see cref="InterruptProductionRules"/> 로 옮겼다(같은 케이스).
/// </summary>
public sealed class InterruptSuccessFxTests
{
    private const string PlayerPrefabPath = "Assets/2.Prefabs/Player/Player.prefab";

    [Test]
    public void 성공이고_발동자가_이_플레이어면_재생()
    {
        Assert.IsTrue(InterruptProductionRules.ShouldPlaySuccessFx(SlowMotionPurpose.Success, 2, true, 2));
    }

    [Test]
    public void 시도_슬로우는_재생_안_함()
    {
        Assert.IsFalse(InterruptProductionRules.ShouldPlaySuccessFx(SlowMotionPurpose.Attempt, 2, true, 2));
    }

    [Test]
    public void 다른_사람의_성공은_재생_안_함()
    {
        Assert.IsFalse(InterruptProductionRules.ShouldPlaySuccessFx(SlowMotionPurpose.Success, 1, true, 2));
    }

    [Test]
    public void 스폰_전이면_재생_안_함()
    {
        // 스폰 전 OwnerClientId 는 기본값 0(호스트)이라 호스트 성공에 잘못 걸릴 수 있다.
        Assert.IsFalse(InterruptProductionRules.ShouldPlaySuccessFx(SlowMotionPurpose.Success, 0, false, 0));
    }

    // ─── 칸 선택(C5) ───

    [Test]
    public void 칸_둘_다_있으면_둘_다_재생()
    {
        Assert.AreEqual(InterruptSuccessFx.Slots.Caster | InterruptSuccessFx.Slots.Target,
            InterruptProductionRules.SelectSuccessFxSlots(true, true, true));
    }

    [Test]
    public void 시전자_칸만_있으면_시전자만()
    {
        Assert.AreEqual(InterruptSuccessFx.Slots.Caster, InterruptProductionRules.SelectSuccessFxSlots(true, true, false));
    }

    [Test]
    public void 대상_칸만_있으면_대상만()
    {
        Assert.AreEqual(InterruptSuccessFx.Slots.Target, InterruptProductionRules.SelectSuccessFxSlots(true, false, true));
    }

    [Test]
    public void 칸_둘_다_비면_아무것도_안_함()
    {
        Assert.AreEqual(InterruptSuccessFx.Slots.None, InterruptProductionRules.SelectSuccessFxSlots(true, false, false));
    }

    [Test]
    public void 재생_조건이_아니면_칸이_있어도_안_함()
    {
        Assert.AreEqual(InterruptSuccessFx.Slots.None, InterruptProductionRules.SelectSuccessFxSlots(false, true, true));
    }

    [Test]
    public void Player_base_루트에_있고_성공_이펙트_칸_둘_다_비어_있다()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(prefab, PlayerPrefabPath);

        var fx = prefab.GetComponent<InterruptSuccessFx>();
        Assert.IsNotNull(fx, "Player.prefab 루트에 InterruptSuccessFx 가 없다");

        var so = new SerializedObject(fx);
        foreach (string field in new[] { "casterFx", "targetFx" })
        {
            SerializedProperty slot = so.FindProperty(field);
            Assert.IsNotNull(slot, $"{field} 필드 이름이 바뀌었다");
            Assert.IsNull(slot.objectReferenceValue, $"base 의 {field} 칸은 비워 두고 캐릭터 Variant 가 덮어쓴다");
        }
    }
}
