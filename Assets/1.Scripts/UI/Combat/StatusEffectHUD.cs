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

        for (int i = 0; i < widgets.Length; i++)
        {
            EffectWidget widget = widgets[i];
            if (widget == null || widget.root == null)
                continue;

            bool used = i < activeCount;
            if (widget.root.activeSelf != used)
                widget.root.SetActive(used);

            if (!used)
                continue;

            StatusEffectInstance instance = effectController.GetActive(i);

            if (widget.icon != null)
            {
                Sprite sprite = IconFor(instance.type);
                if (widget.icon.sprite != sprite)
                    widget.icon.sprite = sprite;
            }

            if (widget.text != null)
                widget.text.text = BuildLabel(instance, effectController.GetRemainingTime(i));
        }
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
        string name = instance.type.ToString().Replace("Modifier", string.Empty);
        if (instance.stackCount > 1)
            name += $" x{instance.stackCount}";

        return remaining < 0f ? name : $"{name}\n{remaining:F1}s";
    }
}
