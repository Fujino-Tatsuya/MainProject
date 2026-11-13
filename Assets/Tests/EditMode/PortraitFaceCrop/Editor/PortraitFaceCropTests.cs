using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// 할 일 276 후속 — 작은 초상화 칸 얼굴 크롭(계산 순수 함수 + 로스터 값 + 칸 배선).
public sealed class PortraitFaceCropTests
{
    const string RosterPath = "Assets/9.ScriptableObject/Player/CharacterRoster.asset";
    const string RowPrefab = "Assets/2.Prefabs/UI/Result/LobbyResultPlayerRow.prefab";

    [Test]
    public void Layout_Full_FitsSpriteWidthToViewport()
    {
        PortraitFaceCrop.Layout(new Vector2(100f, 100f), new Vector2(1024f, 1024f), PortraitFaceCrop.Full, out var size, out var pos);
        Assert.That(size.x, Is.EqualTo(100f).Within(1e-3f));
        Assert.That(size.y, Is.EqualTo(100f).Within(1e-3f));
        Assert.That(pos, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void Layout_PutsFaceCenterAtViewportCenter()
    {
        var crop = new PortraitFaceCrop { center = new Vector2(0.53f, 0.71f), size = 0.25f };
        PortraitFaceCrop.Layout(new Vector2(84f, 84f), new Vector2(1024f, 512f), crop, out var size, out var pos);
        // 짧은 변(84)에 스프라이트 너비의 0.25 가 꽉 찬다 → 너비 336, 비율 유지
        Assert.That(size.x, Is.EqualTo(336f).Within(1e-3f));
        Assert.That(size.y, Is.EqualTo(168f).Within(1e-3f));
        // 가운데 피벗 이미지에서 얼굴 점 = pos + (center - 0.5) * size → 0 이어야 칸 가운데
        Vector2 face = pos + new Vector2((crop.center.x - 0.5f) * size.x, (crop.center.y - 0.5f) * size.y);
        Assert.That(face.magnitude, Is.LessThan(1e-3f));
    }

    [Test]
    public void Layout_NoSprite_FillsViewport()
    {
        var crop = new PortraitFaceCrop { center = new Vector2(0.2f, 0.9f), size = 0.3f };
        PortraitFaceCrop.Layout(new Vector2(106f, 106f), Vector2.zero, crop, out var size, out var pos);
        Assert.That(size, Is.EqualTo(new Vector2(106f, 106f)));
        Assert.That(pos, Is.EqualTo(Vector2.zero));
    }

    [Test]
    public void Roster_AllCharactersHaveFaceCrop()
    {
        var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
        Assert.That(roster.Count, Is.GreaterThanOrEqualTo(3));
        for (int i = 0; i < 3; i++)
        {
            var c = roster.Entries[i].FaceCrop;
            Assert.That(c.size, Is.InRange(0.1f, 0.5f), $"칸 {i} 크롭 크기(얼굴 확대)");
            Assert.That(c.center.x, Is.InRange(0.3f, 0.7f), $"칸 {i} 얼굴 x");
            Assert.That(c.center.y, Is.InRange(0.55f, 0.9f), $"칸 {i} 얼굴 y(위쪽)");
        }
    }

    [Test]
    public void LobbyResultRow_PortraitIsInsideRectMask()
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>(RowPrefab);
        var img = go.transform.Find("Cell_Player/PortraitMask/Image_Portrait");
        Assert.That(img, Is.Not.Null);
        Assert.That(img.parent.GetComponent<RectMask2D>(), Is.Not.Null);
        Assert.That(((RectTransform)img.parent).sizeDelta, Is.EqualTo(new Vector2(84f, 84f)));
    }
}
