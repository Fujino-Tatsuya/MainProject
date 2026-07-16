using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 머리 위 월드스페이스 체력바. Player 프리팹 자식(월드스페이스 캔버스)에 부착.
/// 내 캐릭터는 화면 HUD가 담당하므로 스폰된 원격 플레이어에게만 표시한다 (오너/오프라인은 숨김).
/// LateUpdate에서 카메라 회전을 그대로 따라가는 화면 정렬 빌보드.
/// </summary>
public class UnitOverheadHealthBar : MonoBehaviour
{
    [SerializeField] private GameObject barRoot;
    [SerializeField] private Image hpFill;

    private Player player;

    private void Awake()
    {
        player = GetComponentInParent<Player>();
    }

    private void LateUpdate()
    {
        // 소유권은 스폰 후에 확정되므로 매 프레임 판정 (오프라인은 IsSpawned가 항상 false → 숨김 유지)
        bool shouldShow = player != null && player.IsSpawned && !player.IsOwner;
        if (barRoot != null && barRoot.activeSelf != shouldShow)
            barRoot.SetActive(shouldShow);

        if (!shouldShow)
            return;

        Camera cam = Camera.main;
        if (cam != null)
            transform.rotation = cam.transform.rotation;

        if (hpFill != null)
        {
            int maxHp = player.FinalMaxHp;
            hpFill.fillAmount = maxHp > 0 ? Mathf.Clamp01((float)player.CurrentHealth / maxHp) : 0f;
        }
    }
}
