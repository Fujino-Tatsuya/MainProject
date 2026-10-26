using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 간파 성공 이펙트 칸(할 일 87 C4) — 재생 여부 판단과 <c>Player.prefab</c>(base) 배선.
/// </summary>
public sealed class InterruptSuccessFxTests
{
    private const string PlayerPrefabPath = "Assets/2.Prefabs/Player/Player.prefab";

    [Test]
    public void 성공이고_발동자가_이_플레이어면_재생()
    {
        Assert.IsTrue(InterruptSuccessFx.ShouldPlay(SlowMotionPurpose.Success, 2, true, 2));
    }

    [Test]
    public void 시도_슬로우는_재생_안_함()
    {
        Assert.IsFalse(InterruptSuccessFx.ShouldPlay(SlowMotionPurpose.Attempt, 2, true, 2));
    }

    [Test]
    public void 다른_사람의_성공은_재생_안_함()
    {
        Assert.IsFalse(InterruptSuccessFx.ShouldPlay(SlowMotionPurpose.Success, 1, true, 2));
    }

    [Test]
    public void 스폰_전이면_재생_안_함()
    {
        // 스폰 전 OwnerClientId 는 기본값 0(호스트)이라 호스트 성공에 잘못 걸릴 수 있다.
        Assert.IsFalse(InterruptSuccessFx.ShouldPlay(SlowMotionPurpose.Success, 0, false, 0));
    }

    [Test]
    public void Player_base_루트에_있고_성공_이펙트_칸은_비어_있다()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        Assert.IsNotNull(prefab, PlayerPrefabPath);

        var fx = prefab.GetComponent<InterruptSuccessFx>();
        Assert.IsNotNull(fx, "Player.prefab 루트에 InterruptSuccessFx 가 없다");

        var so = new SerializedObject(fx);
        SerializedProperty slot = so.FindProperty("successEffect");
        Assert.IsNotNull(slot, "successEffect 필드 이름이 바뀌었다");
        Assert.IsNull(slot.objectReferenceValue, "base 의 칸은 비워 두고 캐릭터 Variant 가 덮어쓴다");
    }
}
