using Unity.Netcode;
using UnityEngine;

public class ServerSetAnimState : NetworkBehaviour
{
    [SerializeField] Animator animator;

    #region Integer Parameter Setters
    /// <summary>
    /// 서버가 애니메이터의 Integer 파라미터를 설정하는 함수입니다.
    /// <param name="parameterName">설정할 파라미터의 이름</param>
    /// <param name="value">설정할 값</param>
    /// </summary>
    public void ServerSetInteger(string parameterName, int value)
    { 
        if(!IsServer) return;
        animator.SetInteger(parameterName, value);
    }

    /// <summary>
    /// 서버가 애니메이터의 Integer 파라미터를 설정하는 함수입니다.
    /// </summary>
    /// <typeparam name="T"> 파라미터의 값이 될 Enum 타입</typeparam>
    /// <param name="parameterName">설정할 파라미터의 이름</param>
    /// <param name="enumValue">설정할 Enum 값</param>
    public void ServerSetInteger<T>(string parameterName, T enumValue) where T : System.Enum
    {
        if (!IsServer) return;
        int value = System.Convert.ToInt32(enumValue);
        animator.SetInteger(parameterName, value);
    }

    /// <summary>
    /// 서버가 애니메이터의 Integer 파라미터를 설정하는 함수입니다.
    /// </summary>
    /// <param name="id">설정할 파라미터의 ID</param>
    /// <param name="value">설정할 값</param>
    public void ServerSetInteger(int id, int value)
    {
        if (!IsServer) return;
        animator.SetInteger(id, value);
    }

    /// <summary>
    /// 서버가 애니메이터의 Integer 파라미터를 설정하는 함수입니다.
    /// </summary>
    /// <typeparam name="T"> 파라미터의 값이 될 Enum 타입</typeparam>
    /// <param name="id">설정할 파라미터의 ID</param>
    /// <param name="enumValue">설정할 Enum 값</param>
    public void ServerSetInteger<T>(int id, T enumValue) where T : System.Enum
    {
        if (!IsServer) return;
        int value = System.Convert.ToInt32(enumValue);
        animator.SetInteger(id, value);
    }
    #endregion                                                                          

    #region Trigger Parameter Setters and Resetters
    /// <summary>
    /// 서버가 애니메이터의 Trigger 파라미터를 설정하는 함수입니다.
    /// </summary>
    /// <param name="parameterName">설정할 파라미터의 이름</param>
    public void ServerSetTrigger(string parameterName)
    {
        if (!IsServer) return;
        animator.SetTrigger(parameterName);
    }

    /// <summary>
    /// 서버가 애니메이터의 Trigger 파라미터를 설정하는 함수입니다.
    /// </summary>
    /// <param name="id">설정할 파라미터의 ID</param>
    public void ServerSetTrigger(int id)
    {
        if (!IsServer) return;
        animator.SetTrigger(id);
    }

    /// <summary>
    /// 서버가 애니메이터의 Trigger 파라미터를 리셋하는 함수입니다.
    /// </summary>
    /// <param name="parameterName">리셋할 파라미터의 이름</param>
    public void ServerResetTrigger(string parameterName)
    {
        if (!IsServer) return;
        animator.ResetTrigger(parameterName);
    }

    /// <summary>
    /// 서버가 애니메이터의 Trigger 파라미터를 리셋하는 함수입니다.
    /// </summary>
    /// <param name="id">리셋할 파라미터의 ID</param>
    public void ServerResetTrigger(int id)
    {
        if (!IsServer) return;
        animator.ResetTrigger(id);
    }
    #endregion
    
    #region Float Parameter Setters
    /// <summary>
    /// 서버가 애니메이터의 Float 파라미터를 설정하는 함수입니다.
    /// </summary>
    /// <param name="parameterName">설정할 파라미터의 이름</param>
    /// <param name="value">설정할 값</param>
    public void ServerSetFloat(string parameterName, float value)
    {
        if (!IsServer) return;
        animator.SetFloat(parameterName, value);
    }

    /// <summary>
    /// 서버가 애니메이터의 Float 파라미터를 설정하는 함수입니다.      
    /// </summary>
    /// <param name="id">설정할 파라미터의 ID</param>
    /// <param name="value">설정할 값</param>
    public void ServerSetFloat(int id, float value)
    {
        if (!IsServer) return;
        animator.SetFloat(id, value);
    }
    #endregion

    #region Bool Parameter Setters
    /// <summary>
    /// 서버가 애니메이터의 Bool 파라미터를 설정하는 함수입니다.
    /// </summary>
    /// <param name="parameterName">설정할 파라미터의 이름</param>
    /// <param name="value">설정할 값</param>
    public void ServerSetBool(string parameterName, bool value)
    {
        if (!IsServer) return;
        animator.SetBool(parameterName, value);
    }

    /// <summary>
    /// 서버가 애니메이터의 Bool 파라미터를 설정하는 함수입니다.
    /// </summary>
    /// <param name="id">설정할 파라미터의 ID</param>
    /// <param name="value">설정할 값</param>
    public void ServerSetBool(int id, bool value)
    {
        if (!IsServer) return;
        animator.SetBool(id, value);
    }
    #endregion
}
