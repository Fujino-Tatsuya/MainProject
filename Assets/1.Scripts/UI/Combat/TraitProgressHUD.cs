using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 로컬 플레이어의 특성(증강) 진행도 — XP 바·레벨 숫자·일반 스킬 슬롯 상단 칸. 규칙은 <see cref="TraitProgressRules"/>.
/// 획득 목록 = <see cref="TraitDirector.GetAcquired"/>, 게이트 = <see cref="MapSceneDirector.IsGateOpen"/>(둘 다 기존 복제 값).
/// 두 디렉터는 맵 씬에 있어 HUD 보다 늦게 생기거나 사라지므로 매 프레임 인스턴스만 확인해 구독을 옮기고,
/// 값은 변경 알림(<c>ClientStateChanged</c>·<c>StateChanged</c>)이 왔을 때만 다시 계산한다.
/// 어쌔신(분노 있음)이면 XP 바 칸(<c>xpFill</c>)이 분노 게이지를 대신 보여 준다(할 일 229) — 분노는 매 프레임 복제 스냅샷에서 읽는다.
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

    [Header("어쌔신 분노 (229) — XP 바 칸을 대신 씀")]
    [Tooltip("분노가 최소 변신량 미만일 때 채움 색.")]
    [SerializeField] private Color rageBelowMinColor = Color.white;
    [Tooltip("R 로 변신할 수 있을 만큼 찼을 때 채움 색.")]
    [SerializeField] private Color rageReadyColor = new Color(1f, 0.3f, 0.3f, 1f);
    [Tooltip("변신 중(분노가 줄어드는 동안) 채움 색.")]
    [SerializeField] private Color rageTransformedColor = new Color(0.85f, 0.5f, 1f, 1f);

    private readonly List<int> acquired = new List<int>();
    private Player player;
    private TraitDirector traitDirector;
    private bool dirty = true;
    private AssassinState assassin;
    private Color xpFillColor = Color.white;
    private bool rageShown;

    public void Bind(Player boundPlayer)
    {
        player = boundPlayer;
        assassin = player != null ? player.GetComponentInChildren<AssassinState>(true) : null;
        dirty = true;
        Refresh();
    }

    private void Awake()
    {
        if (xpFill != null)
            xpFillColor = xpFill.color;
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

        RefreshRage();

        if (dirty)
            Refresh();
    }

    // 어쌔신이고 분노가 있으면(절제된 분노 아님) XP 바 칸에 분노 비율을 넣는다. 아니면 XP 로 되돌린다.
    private void RefreshRage()
    {
        bool show = assassin != null && !assassin.IsRestrained;
        if (show != rageShown)
        {
            rageShown = show;
            if (!show && xpFill != null)
                xpFill.color = xpFillColor;
            dirty = true; // XP 로 돌아가면 XP 값을 다시 채운다
        }

        if (!show || xpFill == null)
            return;

        float max = assassin.MaxRage;
        xpFill.fillAmount = max > 0f ? Mathf.Clamp01(assassin.Rage / max) : 0f;
        xpFill.color = assassin.IsTransformed ? rageTransformedColor
            : assassin.CanBeginTransform ? rageReadyColor
            : rageBelowMinColor;
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

        if (xpFill != null && !rageShown)
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
