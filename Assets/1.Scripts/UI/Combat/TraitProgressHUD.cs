using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로컬 플레이어의 특성(증강) 진행도 — XP 바·레벨 숫자·일반 스킬 슬롯 상단 칸. 규칙은 <see cref="TraitProgressRules"/>.
/// 획득 목록 = <see cref="TraitDirector.GetAcquired"/>, 게이트 = <see cref="MapSceneDirector.IsGateOpen"/>(둘 다 기존 복제 값).
/// 두 디렉터는 맵 씬에 있어 HUD 보다 늦게 생기거나 사라지므로 매 프레임 인스턴스만 확인해 구독을 옮기고,
/// 값은 변경 알림(<c>ClientStateChanged</c>·<c>StateChanged</c>)이 왔을 때만 다시 계산한다.
/// </summary>
public class TraitProgressHUD : MonoBehaviour
{
    [System.Serializable]
    private class SlotCells
    {
        public PlayerSkillSlot slot;
        public SkillUpgradeCellsView view;
    }

    [SerializeField] private Image xpFill;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private SlotCells[] slotCells = new SlotCells[0];

    private readonly List<int> acquired = new List<int>();
    private Player player;
    private TraitDirector traitDirector;
    private bool dirty = true;

    public void Bind(Player boundPlayer)
    {
        player = boundPlayer;
        dirty = true;
        Refresh();
    }

    private void OnEnable()
    {
        MapSceneDirector.StateChanged += MarkDirty;
        dirty = true;
    }

    private void OnDisable()
    {
        MapSceneDirector.StateChanged -= MarkDirty;
        SetTraitDirector(null);
    }

    private void Update()
    {
        if (traitDirector != TraitDirector.Instance)
            SetTraitDirector(TraitDirector.Instance);

        if (dirty)
            Refresh();
    }

    private void SetTraitDirector(TraitDirector director)
    {
        if (traitDirector != null)
            traitDirector.ClientStateChanged -= MarkDirty;

        traitDirector = director;
        if (traitDirector != null)
            traitDirector.ClientStateChanged += MarkDirty;
        dirty = true;
    }

    private void MarkDirty() => dirty = true;

    private void Refresh()
    {
        dirty = false;

        // 디렉터가 없거나(튜토리얼 밖) 플레이어가 없으면 획득 0장·튜토리얼 밖으로 본다.
        bool inTutorial = traitDirector != null && player != null;
        acquired.Clear();
        if (inTutorial)
            traitDirector.GetAcquired(player.OwnerClientId, acquired);

        if (levelText != null)
            levelText.text = TraitProgressRules.Level(inTutorial, acquired.Count).ToString();

        if (xpFill != null)
            xpFill.fillAmount = TraitProgressRules.XpFill(inTutorial, acquired.Count, OpenedGateMask());

        if (slotCells == null)
            return;

        foreach (SlotCells cells in slotCells)
        {
            if (cells != null && cells.view != null)
                cells.view.SetStage(inTutorial ? UpgradeStage(cells.slot) : 0);
        }
    }

    private int UpgradeStage(PlayerSkillSlot slot)
    {
        if (traitDirector.Pool == null)
            return 0;

        int count = 0;
        foreach (int traitHash in acquired)
        {
            TraitDefinition trait = traitDirector.GetTrait(traitHash);
            if (trait != null && trait.AffectsCooldownOf(slot))
                count++;
        }
        return TraitProgressRules.ClampUpgradeStage(count);
    }

    private static int OpenedGateMask()
    {
        MapSceneDirector director = MapSceneDirector.Instance;
        if (director == null)
            return 0;

        int mask = 0;
        for (int g = 0; g < MapSceneFlowModel.GateCount; g++)
        {
            if (director.IsGateOpen((MapSceneGate)g))
                mask |= 1 << g;
        }
        return mask;
    }
}
