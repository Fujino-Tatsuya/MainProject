using TMPro;
using UnityEngine;

/// <summary>
/// 로컬 플레이어의 활성 상태이상 표시. StatusEffectController의 복제 리스트를 매 프레임 폴링해
/// 고정 슬롯(위젯 풀)에 타입명·스택·남은시간을 채운다. 슬롯 수를 넘는 항목은 표시를 생략한다.
/// </summary>
public class StatusEffectHUD : MonoBehaviour
{
    [System.Serializable]
    private class EffectWidget
    {
        public GameObject root;
        public TMP_Text text;
    }

    [SerializeField] private EffectWidget[] widgets;

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

            if (!used || widget.text == null)
                continue;

            StatusEffectInstance instance = effectController.GetActive(i);
            widget.text.text = BuildLabel(instance, effectController.GetRemainingTime(i));
        }
    }

    private static string BuildLabel(StatusEffectInstance instance, float remaining)
    {
        string name = instance.type.ToString().Replace("Modifier", string.Empty);
        if (instance.stackCount > 1)
            name += $" x{instance.stackCount}";

        return remaining < 0f ? name : $"{name}\n{remaining:F1}s";
    }
}
