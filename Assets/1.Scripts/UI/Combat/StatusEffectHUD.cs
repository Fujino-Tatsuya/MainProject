using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로컬 플레이어의 활성 상태이상 표시. StatusEffectController의 복제 리스트를 매 프레임 폴링해
/// 고정 슬롯(위젯 풀)에 아이콘·타입명·스택·남은시간을 채운다. 슬롯 수를 넘는 항목은 표시를 생략한다.
/// </summary>
public class StatusEffectHUD : MonoBehaviour
{
    [System.Serializable]
    private class EffectWidget
    {
        public GameObject root;
        public Image icon;
        public TMP_Text text;
    }

    [System.Serializable]
    private struct IconEntry
    {
        public StatusEffectType type;
        public Sprite sprite;
    }

    [SerializeField] private EffectWidget[] widgets;

    [Tooltip("타입별 아이콘이 없을 때 쓰는 공용 아이콘.")]
    [SerializeField] private Sprite defaultIcon;

    [Tooltip("타입별 아이콘. 아트가 나오면 채운다. 없는 타입은 defaultIcon.")]
    [SerializeField] private IconEntry[] icons;

    private StatusEffectController effectController;

    public void Bind(Player player)
    {
        effectController = player != null ? player.GetComponent<StatusEffectController>() : null;
        Refresh();
    }

    private void Update()
    {
        Refresh();
    }

    private void Refresh()
    {
        if (widgets == null)
            return;

        int activeCount = effectController != null ? effectController.ActiveCount : 0;
        int effectIndex = NextVisible(0, activeCount);

        for (int i = 0; i < widgets.Length; i++)
        {
            EffectWidget widget = widgets[i];
            if (widget == null || widget.root == null)
                continue;

            bool used = effectIndex < activeCount;
            if (widget.root.activeSelf != used)
                widget.root.SetActive(used);

            if (!used)
                continue;

            int effect = effectIndex;
            effectIndex = NextVisible(effectIndex + 1, activeCount);
            StatusEffectInstance instance = effectController.GetActive(effect);

            if (widget.icon != null)
            {
                Sprite sprite = IconFor(instance.type);
                if (widget.icon.sprite != sprite)
                    widget.icon.sprite = sprite;
            }

            if (widget.text != null)
                widget.text.text = BuildLabel(instance, effectController.GetRemainingTime(effect));
        }
    }

    // 제압 가속처럼 한 버프가 타입 여럿으로 걸리면 대표 1개만 보인다(SuppressHasteRules.IsHudHidden).
    private int NextVisible(int from, int activeCount)
    {
        int index = from;
        while (index < activeCount && SuppressHasteRules.IsHudHidden(effectController.GetActive(index).type))
            index++;
        return index;
    }

    private Sprite IconFor(StatusEffectType type)
    {
        if (icons != null)
        {
            for (int i = 0; i < icons.Length; i++)
            {
                if (icons[i].type == type && icons[i].sprite != null)
                    return icons[i].sprite;
            }
        }

        return defaultIcon;
    }

    private static string BuildLabel(StatusEffectInstance instance, float remaining)
    {
        string name = DisplayName(instance.type);
        if (instance.stackCount > 1)
            name += $" x{instance.stackCount}";

        return remaining < 0f ? name : $"{name}\n{UiStrings.Seconds(remaining, "F1")}";
    }

    /// <summary>표시 이름 — <c>UI.StatusEffect.&lt;enum&gt;</c>. 행이 없으면 enum 이름(Modifier 뺌), 목록에 없는 값(조합 등)도 enum 이름.</summary>
    public static string DisplayName(StatusEffectType type) =>
        UiStrings.TryGetStatusEffect(type, out UiString name)
            ? name.Text
            : type.ToString().Replace("Modifier", string.Empty);
}
