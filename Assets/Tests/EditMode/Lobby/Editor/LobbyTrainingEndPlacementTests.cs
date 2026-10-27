using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>훈련 종료 텔레포트 자리(브리핑 구역 박스 안 격자) — 할 일 111.</summary>
public sealed class LobbyTrainingEndPlacementTests
{
    private const float Margin = LobbyTrainingEndPlacement.DefaultMargin;
    private const float Spacing = LobbyTrainingEndPlacement.DefaultSpacing;

    // 씬 Gather_Brief 박스(가로 6 × 세로 11.5).
    private static readonly Vector2 BriefBox = new Vector2(6f, 11.5f);

    private static List<Vector2> All(int count, Vector2 box)
    {
        var slots = new List<Vector2>();
        for (var i = 0; i < count; i++)
            slots.Add(LobbyTrainingEndPlacement.Slot(i, count, box, Margin, Spacing));
        return slots;
    }

    [Test]
    public void 혼자면_정중앙()
    {
        Assert.That(LobbyTrainingEndPlacement.Slot(0, 1, BriefBox, Margin, Spacing), Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void 세_명은_가로_한_줄_가운데_정렬()
    {
        var slots = All(3, BriefBox);
        Assert.That(slots[0].x, Is.EqualTo(-Spacing).Within(1e-4f));
        Assert.That(slots[1].x, Is.EqualTo(0f).Within(1e-4f));
        Assert.That(slots[2].x, Is.EqualTo(Spacing).Within(1e-4f));
        foreach (var slot in slots)
            Assert.That(slot.y, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void 네_명이면_다음_줄로_넘어가고_마지막_줄도_가운데()
    {
        var slots = All(4, BriefBox);
        Assert.That(slots[0].y, Is.EqualTo(Spacing * 0.5f).Within(1e-4f));
        Assert.That(slots[3].y, Is.EqualTo(-Spacing * 0.5f).Within(1e-4f));
        Assert.That(slots[3].x, Is.EqualTo(0f).Within(1e-4f));
    }

    [Test]
    public void 자리_수까지는_겹치지_않고_벽_여백_안쪽()
    {
        var capacity = LobbyTrainingEndPlacement.Capacity(BriefBox, Margin, Spacing);
        Assert.That(capacity, Is.EqualTo(3 * 7));

        for (var count = 1; count <= capacity; count++)
        {
            var slots = All(count, BriefBox);
            for (var i = 0; i < slots.Count; i++)
            {
                Assert.That(Mathf.Abs(slots[i].x), Is.LessThanOrEqualTo(BriefBox.x * 0.5f - Margin + 1e-3f), $"{count}명 {i}번 X");
                Assert.That(Mathf.Abs(slots[i].y), Is.LessThanOrEqualTo(BriefBox.y * 0.5f - Margin + 1e-3f), $"{count}명 {i}번 Z");
                for (var j = i + 1; j < slots.Count; j++)
                    Assert.That(Vector2.Distance(slots[i], slots[j]), Is.GreaterThanOrEqualTo(Spacing - 1e-3f), $"{count}명 {i}·{j}번");
            }
        }
    }

    [Test]
    public void 자리보다_많으면_처음부터_순환()
    {
        var box = new Vector2(4f, 2f);   // 안쪽 2 × 0 → 가로 2자리 한 줄
        Assert.That(LobbyTrainingEndPlacement.Capacity(box, Margin, Spacing), Is.EqualTo(2));

        var first = LobbyTrainingEndPlacement.Slot(0, 3, box, Margin, Spacing);
        var second = LobbyTrainingEndPlacement.Slot(1, 3, box, Margin, Spacing);
        var third = LobbyTrainingEndPlacement.Slot(2, 3, box, Margin, Spacing);
        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(third, Is.EqualTo(first));
    }

    [Test]
    public void 여백보다_좁은_박스는_중심_한_자리()
    {
        var box = new Vector2(1f, 1f);
        Assert.That(LobbyTrainingEndPlacement.Capacity(box, Margin, Spacing), Is.EqualTo(1));
        for (var i = 0; i < 3; i++)
            Assert.That(LobbyTrainingEndPlacement.Slot(i, 3, box, Margin, Spacing), Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void 범위_밖_순번은_명단_안으로_순환()
    {
        Assert.That(LobbyTrainingEndPlacement.Slot(3, 3, BriefBox, Margin, Spacing),
            Is.EqualTo(LobbyTrainingEndPlacement.Slot(0, 3, BriefBox, Margin, Spacing)));
        Assert.That(LobbyTrainingEndPlacement.Slot(-1, 3, BriefBox, Margin, Spacing),
            Is.EqualTo(LobbyTrainingEndPlacement.Slot(2, 3, BriefBox, Margin, Spacing)));
    }
}
