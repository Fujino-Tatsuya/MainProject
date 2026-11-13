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

    // 로비 결과 행은 초상화 없음(11-13 사용자 프리팹 수정) — 참조가 비어도 Bind 가 오류 없이 이름을 채운다.
    [Test]
    public void LobbyResultRow_HasNoPortrait_BindWorksWithoutIt()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RowPrefab);
        Assert.That(prefab.GetComponentsInChildren<Image>(true), Has.None.Matches<Image>(i => i.name == "Image_Portrait"));
        Assert.That(new SerializedObject(prefab.GetComponent<ResultPlayerRowView>()).FindProperty("portraitImage").objectReferenceValue, Is.Null);

        var go = Object.Instantiate(prefab);
        try
        {
            var roster = AssetDatabase.LoadAssetAtPath<CharacterRoster>(RosterPath);
            var view = go.GetComponent<ResultPlayerRowView>();
            Assert.DoesNotThrow(() => view.Bind(1, default, roster.Entries[0], true));
            Assert.That(go.transform.Find("Cell_Player/Text_Character").GetComponent<TMPro.TMP_Text>().text, Is.EqualTo(roster.Entries[0].DisplayName));
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // 5.ResultScene 행: 레이아웃 칸 안 고정 64×64 마스크(레이아웃이 마스크 크기를 정하지 않음 — Bind 순간에도 크기 확정).
    [Test]
    public void ResultSceneRow_PortraitIsInsideFixedRectMask()
    {
        var go = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2.Prefabs/UI/Result/ResultPlayerRow.prefab");
        var img = go.transform.Find("Cell_Portrait/PortraitMask/Image_Portrait");
        Assert.That(img, Is.Not.Null);
        var mask = (RectTransform)img.parent;
        Assert.That(mask.GetComponent<RectMask2D>(), Is.Not.Null);
        Assert.That(mask.GetComponent<LayoutElement>(), Is.Null);
        Assert.That(mask.anchorMin, Is.EqualTo(mask.anchorMax));
        Assert.That(mask.sizeDelta, Is.EqualTo(new Vector2(64f, 64f)));
        Assert.That(mask.parent.GetComponent<LayoutElement>().preferredWidth, Is.EqualTo(64f));
        var view = new SerializedObject(go.GetComponent<ResultPlayerRowView>());
        Assert.That(view.FindProperty("portraitImage").objectReferenceValue, Is.EqualTo(img.GetComponent<Image>()));
    }
}
