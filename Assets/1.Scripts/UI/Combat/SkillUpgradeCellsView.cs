using UnityEngine;

/// <summary>
/// 일반 스킬 슬롯(Q·E·R·RMB) 상단 칸 — 그 스킬에 붙은 특성(증강) 강화 단계 수만큼 앞에서부터 칸을 켠다.
/// 단계 데이터 연결은 아직 없다(특성↔스킬 연결 확정 후). 지금은 프리팹 기본값대로 항상 0칸.
/// 칸 on/off 는 GameObject 로 한다 — SkillCooldownHUD 의 차단 색은 꺼진 칸까지 포함해 캐시하므로 색은 건드리지 않는다.
/// </summary>
public class SkillUpgradeCellsView : MonoBehaviour
{
    [SerializeField] private GameObject[] cells = new GameObject[0];

    public int CellCount => cells != null ? cells.Length : 0;

    public void SetStage(int stage)
    {
        if (cells == null)
            return;

        for (int i = 0; i < cells.Length; i++)
        {
            bool on = IsCellOn(i, stage, cells.Length);
            if (cells[i] != null && cells[i].activeSelf != on)
                cells[i].SetActive(on);
        }
    }

    /// <summary>켤 칸 수 = 단계 수를 0..칸 수로 자른 값.</summary>
    public static int ActiveCellCount(int stage, int cellCount) => Mathf.Clamp(stage, 0, Mathf.Max(0, cellCount));

    public static bool IsCellOn(int index, int stage, int cellCount) =>
        index >= 0 && index < ActiveCellCount(stage, cellCount);
}
