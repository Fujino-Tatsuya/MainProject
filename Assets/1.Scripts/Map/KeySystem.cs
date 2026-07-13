using UnityEngine;

public class KeySystem : MonoBehaviour
{
    [SerializeField] private int currentKeys = 0;
    
    [Header("열쇠 드롭 확률 (0.0 ~ 1.0)")]
    public float DropChanceTier1 = 0.5f;
    public float DropChanceTier2 = 0.3f;
    public float DropChanceTier3 = 0.1f;

    public void OnMonsterKilled(NodeTier nodeTier)
    {
        float chance = GetDropChance(nodeTier);
        if (Random.value <= chance)
        {
            AddKey();
            Edit.Log("[KeySystem] 열쇠를 획득했습니다! 현재 열쇠: " + currentKeys);
        }
    }

    public bool HasKey()
    {
        return currentKeys > 0;
    }

    public bool TryUseKey()
    {
        if (currentKeys > 0)
        {
            currentKeys--;
            Edit.Log("[KeySystem] 열쇠를 사용했습니다. 남은 열쇠: " + currentKeys);
            return true;
        }
        return false;
    }

    private void AddKey()
    {
        currentKeys++;
        // TODO: UI 업데이트 이벤트 발생
    }

    private float GetDropChance(NodeTier tier)
    {
        switch (tier)
        {
            case NodeTier.Tier1_Large: return DropChanceTier1;
            case NodeTier.Tier2_Medium: return DropChanceTier2;
            case NodeTier.Tier3_Small: return DropChanceTier3;
            default: return 0f;
        }
    }
}
