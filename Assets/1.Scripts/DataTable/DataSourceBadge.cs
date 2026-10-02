#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// 에디터 Play 중 게임 화면 오른쪽 위에 현재 데이터 출처("DATA: TABLE" 등)를 상시 표시한다.
/// "왜 수치가 안 바뀌지?" 의 대부분은 모드 착각이라 항상 보이게 둔다. 에디터 전용 — 빌드에는 이 클래스가 없다.
/// 만드는 쪽 = DataSourcePlayMode(Editor). 씬 전환에도 살아남고, 하이어라키에 안 보이며, 저장되지 않는다.
/// </summary>
public sealed class DataSourceBadge : MonoBehaviour
{
    private static DataSourceBadge instance;

    private string text;
    private Color color;
    private GUIStyle style;

    public static void Show(string text, Color color)
    {
        if (instance == null)
        {
            var go = new GameObject(nameof(DataSourceBadge)) { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DataSourceBadge>();
        }

        instance.text = text;
        instance.color = color;
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        style ??= new GUIStyle(GUI.skin.box)
        {
            fontSize = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color32(0x26, 0x2A, 0x33, 0xFF), background = Texture2D.whiteTexture },
        };

        var content = new GUIContent(text);
        Vector2 size = style.CalcSize(content) + new Vector2(12, 4);
        Color previous = GUI.backgroundColor;
        GUI.backgroundColor = color;
        GUI.Box(new Rect(Screen.width - size.x - 8, 8, size.x, size.y), content, style);
        GUI.backgroundColor = previous;
    }
}
#endif
