using NUnit.Framework;
using UnityEngine;

/// <summary>
/// 할 일 242 — 피해 숫자 확인 씬: 구간별 피해량이 그 구간으로 판정되는가, 기준 최대 체력 덮어쓰기, 설정의 구간 경계 조회.
/// </summary>
public sealed class FloatingDamageDebugLabTests
{
    // 에셋 기본 경계: 일반 15/50 · 중간보스 5/20 · 보스 0.75/3. 기준 체력 = 데이터 값(23호 레이드 2880 · 휴머노이드 90 · 팔라딘 가정 1000).
    [TestCase(2880, 0.75f, 3f)]
    [TestCase(90, 15f, 50f)]
    [TestCase(1000, 15f, 50f)]
    [TestCase(600, 5f, 20f)]
    public void 구간별_피해량은_그_구간으로_판정된다(int maxHp, float mid, float high)
    {
        foreach (FloatingDamageTier tier in new[] { FloatingDamageTier.Low, FloatingDamageTier.Mid, FloatingDamageTier.High })
        {
            int amount = DebugFloatingDamageLabRules.AmountFor(tier, maxHp, mid, high);
            Assert.AreEqual(tier, FloatingDamageTierPolicy.Classify(amount, maxHp, mid, high), $"{tier} {amount}/{maxHp}");
        }
    }

    [Test]
    public void 누적_한_번_값은_합계가_넘치지_않고_최소_1()
    {
        Assert.AreEqual(10, DebugFloatingDamageLabRules.PerHitAmount(65, 6));
        Assert.AreEqual(1, DebugFloatingDamageLabRules.PerHitAmount(3, 6));
        Assert.LessOrEqual(DebugFloatingDamageLabRules.PerHitAmount(65, 6) * 6, 65);
    }

    [Test]
    public void 기준_최대_체력_덮어쓰기가_있으면_그_값()
    {
        var request = new FloatingPopupRequest(null, PopupKind.Damage, 10, true, 0, AttackType.Default, AttackHitPattern.Single);
        Assert.AreEqual(0, request.BaseMaxHp, "대상·덮어쓰기 없음 = 0");
        request.baseMaxHpOverride = 2880;
        Assert.AreEqual(2880, request.BaseMaxHp);
    }

    [Test]
    public void 설정에서_등급별_구간_경계를_읽는다()
    {
        var settings = ScriptableObject.CreateInstance<FloatingDamageSettings>();
        try
        {
            Assert.IsTrue(settings.TryGetTierThresholds(MonsterRank.Boss, out float mid, out float high));
            Assert.AreEqual(0.75f, mid, 1e-4f);
            Assert.AreEqual(3f, high, 1e-4f);
        }
        finally
        {
            Object.DestroyImmediate(settings);
        }
    }

    [Test]
    public void 공격자_자리_왼쪽_오른쪽_정면_없음()
    {
        Vector3 target = new Vector3(1f, 2f, 3f);
        Vector3 right = new Vector3(0.7071f, -0.5f, 0.7071f); // 기울어진 카메라 오른쪽 — 바닥 평면으로 눕힌다
        Vector3 back = new Vector3(-0.5f, 0.8f, 0.5f);  // 카메라 쪽(오른쪽과 수직)

        Assert.IsTrue(DebugFloatingDamageLabRules.TryGetSource(DebugFloatingDamageLab.SourceChoice.Left, target, right, back, 3f, out Vector3 left));
        Assert.AreEqual(target.y, left.y, 1e-4f, "높이는 대상과 같다");
        Assert.AreEqual(3f, Vector3.Distance(target, left), 1e-3f);
        Assert.Less(Vector3.Dot(left - target, right), 0f, "카메라 왼쪽");

        Assert.IsTrue(DebugFloatingDamageLabRules.TryGetSource(DebugFloatingDamageLab.SourceChoice.Right, target, right, back, 3f, out Vector3 rightPoint));
        Assert.Greater(Vector3.Dot(rightPoint - target, right), 0f, "카메라 오른쪽");

        Assert.IsTrue(DebugFloatingDamageLabRules.TryGetSource(DebugFloatingDamageLab.SourceChoice.Front, target, right, back, 3f, out Vector3 front));
        Assert.AreEqual(0f, Vector3.Dot(front - target, new Vector3(right.x, 0f, right.z).normalized), 1e-3f, "정면 = 화면 x 차이 없음");

        Assert.IsFalse(DebugFloatingDamageLabRules.TryGetSource(DebugFloatingDamageLab.SourceChoice.None, target, right, back, 3f, out _));
    }
}
