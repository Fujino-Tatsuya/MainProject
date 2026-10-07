using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Dev Boot 툴바 요소(버튼·씬·캐릭터 드롭다운)와 데이터 출처 드롭다운에 Play 묶음과 같은 둥근 배경을 깔고 파스텔 색을 입힌다.
///
/// Play 묶음의 배경은 별도 오브젝트가 아니라 <see cref="EditorToolbarUtility.SetupChildrenAsButtonStrip"/> 가
/// 요소에 붙이는 button-strip USS 클래스다. 우리 두 요소는 서로 다른 Overlay 라 부모가 달라 그 함수를 직접 못 쓰므로
/// 같은 클래스(단독 = alone)를 각자 붙이고 색만 인라인으로 덮는다.
///
/// MainToolbar 공개 API 로는 생성된 VisualElement 에 닿을 수 없어서, 툴바 창 UI 트리에서
/// <c>ussName</c>(DevBootToolbar 의 어트리뷰트)으로 찾는다. 툴바는 Refresh 때 요소를 새로 만들므로
/// 주기적으로 다시 입힌다(이미 칠한 건 건너뜀). 툴바 창 타입을 못 찾으면 아무것도 안 한다 — 기능에는 영향 없음.
/// </summary>
[InitializeOnLoad]
public static class DevBootToolbarStyle
{
    private const string StyledClass = "devboot-pastel";
    private const string ToolbarElementClass = "unity-editor-toolbar-element";
    private const string StripElementClass = "unity-editor-toolbar__button-strip-element";
    private const string AloneStripElementClass = "unity-editor-toolbar__button-strip-element--alone";
    private const double ScanIntervalSeconds = 0.5;

    private static readonly Color LaunchColor = new Color32(0xB5, 0xEA, 0xD7, 0xFF); // 민트
    private static readonly Color SceneColor = new Color32(0xC7, 0xCE, 0xEA, 0xFF);  // 라벤더
    private static readonly Color CharacterColor = new Color32(0xFF, 0xDA, 0xC1, 0xFF); // 피치
    private static readonly Color TextColor = new Color32(0x26, 0x2A, 0x33, 0xFF);
    private const float HoverDarken = 0.88f;

    private static readonly Type ToolbarWindowType = typeof(EditorWindow).Assembly
        .GetTypes()
        .FirstOrDefault(t => t.Name == "MainToolbarWindow" && typeof(UnityEngine.Object).IsAssignableFrom(t));

    private static double nextScanTime;

    static DevBootToolbarStyle()
    {
        if (ToolbarWindowType == null)
        {
            return;
        }

        EditorApplication.update -= OnUpdate;
        EditorApplication.update += OnUpdate;
    }

    private static void OnUpdate()
    {
        if (EditorApplication.timeSinceStartup < nextScanTime)
        {
            return;
        }

        nextScanTime = EditorApplication.timeSinceStartup + ScanIntervalSeconds;
        foreach (VisualElement root in FindToolbarRoots())
        {
            StyleNamed(root, DevBootToolbar.LaunchUssName, LaunchColor);
            StyleNamed(root, DevBootToolbar.SceneUssName, SceneColor);
            StyleNamed(root, DevBootToolbar.CharacterUssName, CharacterColor);
            // 데이터 출처는 모드마다 색이 다르다. 선택을 바꾸면 툴바가 요소를 새로 만들므로 다음 스캔에 새 색이 입혀진다.
            StyleNamed(root, DataSourceToolbar.UssName,
                DataSourcePlayMode.Current == DataSource.Table ? DataSourcePlayMode.TableColor : DataSourcePlayMode.InspectorColor);
        }
    }

    private static void StyleNamed(VisualElement root, string ussName, Color background)
    {
        VisualElement named = root.Q(ussName);
        if (named == null)
        {
            return;
        }

        // ussName 이 Overlay 쪽에 붙는지 요소 자체에 붙는지에 상관없이 실제 툴바 요소를 칠한다.
        VisualElement target = named.ClassListContains(ToolbarElementClass)
            ? named
            : named.Q(className: ToolbarElementClass) ?? named;

        if (!target.ClassListContains(StyledClass))
        {
            Apply(target, background);
        }
    }

    private static void Apply(VisualElement element, Color background)
    {
        element.AddToClassList(StyledClass);
        element.AddToClassList(StripElementClass);
        element.AddToClassList(AloneStripElementClass);

        element.style.backgroundColor = background;
        element.style.color = TextColor;

        // 라벨·아이콘·드롭다운 화살표를 어두운 색으로 — 밝은 배경 위에서 기본 밝은 회색은 안 보인다.
        element.Query<VisualElement>().ForEach(child =>
        {
            child.style.color = TextColor;
            child.style.unityBackgroundImageTintColor = TextColor;
            if (child is Image image)
            {
                image.tintColor = TextColor;
            }
        });

        // 인라인 배경은 기본 호버 표시를 덮으므로 직접 어둡게 한다.
        Color hover = new Color(background.r * HoverDarken, background.g * HoverDarken, background.b * HoverDarken, 1f);
        element.RegisterCallback<PointerEnterEvent>(_ => element.style.backgroundColor = hover);
        element.RegisterCallback<PointerLeaveEvent>(_ => element.style.backgroundColor = background);
    }

    private static IEnumerable<VisualElement> FindToolbarRoots()
    {
        foreach (UnityEngine.Object window in Resources.FindObjectsOfTypeAll(ToolbarWindowType))
        {
            VisualElement root = window is EditorWindow editorWindow
                ? editorWindow.rootVisualElement
                : window.GetType()
                    .GetProperty("visualTree", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(window) as VisualElement;
            if (root != null)
            {
                yield return root;
            }
        }
    }
}
