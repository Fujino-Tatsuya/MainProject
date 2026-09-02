using System.Linq;
using NUnit.Framework;
using UnityEditor;

/// <summary>
/// 23호 두 변형(No23 · No23_Solo)의 카운터 저작값 고정.
///
/// 🔴 이 테스트는 <c>AssetDatabase</c> 로 읽는다 — 즉 <b>Unity 가 실제로 로드한 값</b>을 본다.
///    파일을 에디터 밖에서 고쳤을 때 엔진이 낡은 사본을 서빙하는 사고를 여기서 잡는다.
///    (에셋에 키가 없으면 코드 초기값이 들어오므로, 저작 누락도 같은 방식으로 걸린다.)
/// </summary>
public sealed class BossCounterDataTests
{
    [TestCase("Assets/2.Prefabs/Monster/Data/No23.asset")]
    [TestCase("Assets/2.Prefabs/Monster/Data/No23_Solo.asset")]
    public void Variant_HasApprovedCounterDefaults(string path)
    {
        BossDataSO data = AssetDatabase.LoadAssetAtPath<BossDataSO>(path);
        Assert.That(data, Is.Not.Null, $"{path} 를 못 읽었다");

        BossAttackEntry grab = data.attacks.Single(a => a.attackId == BossAttackId.Grab);
        BossAttackEntry dash = data.attacks.Single(a => a.attackId == BossAttackId.Dash);

        // 확정값 — Grab 1.5 / Dash 1.5.
        // 🔴 Grab 은 설계 초기값이 1.0 이었으나 실측으로 올렸다(2026-09-02, 팀장 확정).
        //    Grab 클립의 OnAttackHit 은 정규화 0.354 = 188프레임/60fps × 0.354 = **1.11초** 에 온다.
        //    창이 그보다 짧으면 게이트가 붙잡을 시간이 없어 창이 하는 일이 없어진다
        //    (설계 §4.3 의 "이벤트가 창보다 먼저 온다" 전제가 Grab 에서 깨져 있었다).
        //    Dash 는 0.15 = 86프레임/60fps × 0.15 = 0.22초라 1.5 초 창이 1.29초를 붙잡는다.
        Assert.That(grab.opensCounterWindow, Is.True);
        Assert.That(grab.counterWindowDuration, Is.EqualTo(1.5f));
        Assert.That(dash.opensCounterWindow, Is.True);
        Assert.That(dash.counterWindowDuration, Is.EqualTo(1.5f));

        // 창을 여는 공격은 이 둘뿐이어야 한다 — 훅·어퍼까지 열리면 카운터가 상시 자원이 된다.
        Assert.That(data.attacks.Where(a => a.opensCounterWindow).Select(a => a.attackId),
            Is.EquivalentTo(new[] { BossAttackId.Grab, BossAttackId.Dash }));

        // 전체 행동 불능 시간 — Hit 을 앞에 더하지 않으므로 이 값이 곧 체감 시간이다(설계 §3.3).
        Assert.That(data.maxGroggyCount, Is.EqualTo(5));
        Assert.That(data.groggyDuration, Is.EqualTo(0.5f));
        Assert.That(data.breakDuration, Is.EqualTo(2f));
    }
}
