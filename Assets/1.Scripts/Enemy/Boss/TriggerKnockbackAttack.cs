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
        // 루트 콜라이더와 Hurtbox 콜라이더가 각각 트리거 이벤트를 발생시키므로 Hurtbox만 판정한다
        if (other.GetComponentInParent<Hurtbox>() == null)
            return;

        knockbackAttack.ApplyKnockbackAttack(other.gameObject);
    }
}
