using UnityEngine;

[CreateAssetMenu(menuName = "Characters/Character Definition")]
public class CharacterDefinition : ScriptableObject
{
    [SerializeField] private string characterId;
    [SerializeField] private GameObject visualPrefab;
    [SerializeField] private RuntimeAnimatorController animatorController;
    [SerializeField] private DefaultAttackData defaultAttackData;

    [Header("Base Stats")]
    [SerializeField] private int attackDamage;
    [SerializeField] private float moveSpeed;
    [SerializeField] private float attackSpeed;
    [SerializeField] private int maxHp;
    [SerializeField] private int defense;

    public string CharacterId => characterId;
    public GameObject VisualPrefab => visualPrefab;
    public RuntimeAnimatorController AnimatorController => animatorController;
    public DefaultAttackData DefaultAttackData => defaultAttackData;
    public int AttackDamage => attackDamage;
    public float MoveSpeed => moveSpeed;
    public float AttackSpeed => attackSpeed;
    public int MaxHp => maxHp;
    public int Defense => defense;
}
