using UnityEngine;

public class TriggerKnockbackAttack : MonoBehaviour
{
    [SerializeField] KnockbackAttack knockbackAttack;

    void Awake()
    {
        if (knockbackAttack == null)
        {
            Debug.LogError("KnockbackAttack 컴포넌트가 할당되지 않았습니다.", this);
        }
    }
    private void OnTriggerEnter(Collider other)
    {
        knockbackAttack.ApplyKnockbackAttack(other.gameObject);
    }
}
