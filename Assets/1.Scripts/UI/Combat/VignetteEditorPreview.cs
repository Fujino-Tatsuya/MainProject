#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 비네팅 HUD(<see cref="HealthVignetteHUD"/>)의 에디터 미리보기 공용 — Play 없이 Prefab Mode·씬뷰·게임뷰에서 보기.
/// 미리보기는 저장 안 되는 자식(DontSave | NotEditable)으로만 그린다. 실제 Image 의 색·알파·재질은 건드리지 않아
/// 프리팹을 저장해도 미리보기 흔적이 남지 않는다(<see cref="TitleSkipHint"/> 와 같은 방식). 에디터 전용 — 빌드에 없음.
/// </summary>
public static class VignetteEditorPreview
{
    private const HideFlags PreviewFlags = HideFlags.DontSave | HideFlags.NotEditable;

    /// <summary>이름이 같은 미리보기 자식을 찾고, 없으면 화면 전체로 늘린 자식을 만든다.</summary>
    public static T Ensure<T>(Transform parent, string name) where T : MaskableGraphic
    {
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);
            if (child.name == name && (child.gameObject.hideFlags & HideFlags.DontSave) != 0 && child.TryGetComponent(out T existing))
                return existing;
        }

        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(T));
        go.hideFlags = PreviewFlags;
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var graphic = go.GetComponent<T>();
        graphic.raycastTarget = false;
        graphic.maskable = false;
        return graphic;
    }

    /// <summary>미리보기 자식(DontSave 표시)만 지운다.</summary>
    public static void Clear(Transform parent)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            GameObject child = parent.GetChild(i).gameObject;
            if ((child.hideFlags & HideFlags.DontSave) != 0)
                Object.DestroyImmediate(child);
        }
    }

    /// <summary>OnValidate·레이아웃 콜백 안에서 오브젝트를 만들거나 지우지 않게 한 박자 뒤로 미룬다.</summary>
    public static void Defer(MonoBehaviour owner, System.Action refresh)
    {
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (owner != null && !Application.isPlaying)
                refresh();
        };
    }
}
#endif
