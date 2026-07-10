public interface IAttackReceiver
{
    bool ReceiveAttack(AttackInfo attackInfo, AttackHitContext hitContext);
}
