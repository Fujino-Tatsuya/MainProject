using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// 처치 연출 타임라인 창 여러 개 선택 편집(할 일 296) — 순수 함수(<see cref="BossVictoryMultiEdit"/>): 잡은 노드 기준 스냅 Δ · 0초 앞 막기(간격 유지) ·
/// 클립 끝 끌기 최소 길이 · 키 비율 이동 · 사각형 선택 더하기/바꾸기 · Shift 클릭 토글.
/// 실행 = <c>Tools/Tests/전투 EditMode 테스트 실행</c>(필터 ^BossVictory).
/// </summary>
public sealed class BossVictoryMultiEditTests
{
    const float Tolerance = 1e-4f;
    const float MinLength = 0.01f;

    [Test]
    public void 스냅_Δ_는_잡은_노드를_0_01초_칸에_붙이고_같은_Δ_를_나머지에()
    {
        // 잡은 노드 1.003초 → 0.25초 끌기 = 1.253 → 스냅 1.25 → Δ 0.247 — 다른 노드(2.111)도 0.247 만큼(2.358, 스냅 칸 아님 = 의도).
        float d = BossVictoryMultiEdit.SnapDelta(1.003f, 0.25f, true);
        Assert.AreEqual(1.25f, 1.003f + d, Tolerance, "잡은 노드는 스냅 칸");
        Assert.AreEqual(2.358f, 2.111f + d, Tolerance, "나머지는 같은 Δ");
        Assert.AreEqual(0.25f, BossVictoryMultiEdit.SnapDelta(1.003f, 0.25f, false), Tolerance, "스냅 끄면 Δ 그대로");
    }

    [Test]
    public void 이동_Δ_는_가장_앞_노드가_0초_아래로_못_가게_한_번에_막아_간격_유지()
    {
        float earliest = BossVictoryMultiEdit.Earliest(new List<float> { 0.4f, 1.2f, 3f });
        Assert.AreEqual(0.4f, earliest, Tolerance);
        float d = BossVictoryMultiEdit.ClampMoveDelta(-1f, earliest);
        Assert.AreEqual(-0.4f, d, Tolerance, "가장 앞이 0 에 닿는 데까지만");
        Assert.AreEqual(0.8f, 1.2f + d, Tolerance, "둘째와 간격 0.8 그대로");
        Assert.AreEqual(0.5f, BossVictoryMultiEdit.ClampMoveDelta(0.5f, earliest), Tolerance, "뒤로는 그대로");
        Assert.AreEqual(-5f, BossVictoryMultiEdit.ClampMoveDelta(-5f, BossVictoryMultiEdit.Earliest(new List<float>())), Tolerance, "움직일 노드 없음 = 그대로");
    }

    [Test]
    public void 오른쪽_끝_끌기는_길이_Δ_최소_길이_지킴()
    {
        BossVictoryMultiEdit.ResizeRight(1f, 0.5f, 0.2f, MinLength, out float s, out float l);
        Assert.AreEqual(1f, s, Tolerance, "시작 고정");
        Assert.AreEqual(0.7f, l, Tolerance);
        BossVictoryMultiEdit.ResizeRight(1f, 0.5f, -2f, MinLength, out _, out l);
        Assert.AreEqual(MinLength, l, Tolerance, "짧은 클립은 최소 길이에서 멈춤");
    }

    [Test]
    public void 왼쪽_끝_끌기는_끝_고정_0초와_최소_길이_사이()
    {
        BossVictoryMultiEdit.ResizeLeft(1f, 0.5f, -0.3f, MinLength, out float s, out float l);
        Assert.AreEqual(0.7f, s, Tolerance);
        Assert.AreEqual(0.8f, l, Tolerance, "끝 1.5 고정");
        BossVictoryMultiEdit.ResizeLeft(1f, 0.5f, 2f, MinLength, out s, out l);
        Assert.AreEqual(1.5f - MinLength, s, Tolerance, "끝 − 최소 길이까지");
        Assert.AreEqual(MinLength, l, Tolerance);
        BossVictoryMultiEdit.ResizeLeft(0.2f, 0.5f, -1f, MinLength, out s, out l);
        Assert.AreEqual(0f, s, Tolerance, "0초 앞으로 못 감");
        Assert.AreEqual(0.7f, l, Tolerance);
    }

    [Test]
    public void 같은_Δ_로_여러_클립_길이를_바꾸면_짧은_클립만_최소_길이에서_멈춘다()
    {
        float[] lengths = { 0.1f, 1f, 2f };
        var result = new List<float>();
        foreach (float len in lengths)
        {
            BossVictoryMultiEdit.ResizeRight(0f, len, -0.5f, MinLength, out _, out float l);
            result.Add(l);
        }
        Assert.AreEqual(MinLength, result[0], Tolerance);
        Assert.AreEqual(0.5f, result[1], Tolerance);
        Assert.AreEqual(1.5f, result[2], Tolerance);
    }

    [Test]
    public void 키_이동은_Δ초를_구간_길이로_나눈_비율()
    {
        Assert.AreEqual(0.75f, BossVictoryMultiEdit.MoveKeyU(0.5f, 2f, 0.5f), Tolerance);
        Assert.AreEqual(0.25f, BossVictoryMultiEdit.MoveKeyU(0.5f, 2f, -0.5f), Tolerance);
    }

    [Test]
    public void 사각형_선택은_더하기면_기존에_합치고_아니면_바꾼다_중복없음()
    {
        var baseSel = new List<int> { 1, 2 };
        var hits = new List<int> { 2, 3 };
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, BossVictoryMultiEdit.CombineSelection(baseSel, hits, true));
        CollectionAssert.AreEqual(new[] { 2, 3 }, BossVictoryMultiEdit.CombineSelection(baseSel, hits, false));
        CollectionAssert.AreEqual(new[] { 1, 2 }, BossVictoryMultiEdit.CombineSelection(baseSel, new List<int>(), true));
    }

    [Test]
    public void Shift_클릭은_넣고_다시_누르면_뺀다()
    {
        var sel = new List<int> { 1 };
        Assert.IsTrue(BossVictoryMultiEdit.Toggle(sel, 2));
        CollectionAssert.AreEqual(new[] { 1, 2 }, sel);
        Assert.IsFalse(BossVictoryMultiEdit.Toggle(sel, 1));
        CollectionAssert.AreEqual(new[] { 2 }, sel);
    }
}
