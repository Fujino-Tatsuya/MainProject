using System.Collections.Generic;

public class BitMaskHelper <T> where T : System.Enum
{
    /// <summary>
    /// 원본 enum 값에 새로운 enum 값을 추가하는 함수
    /// </summary>
    /// <param name="original">변경할 원본 enum 값</param>
    /// <param name="newState">추가할 새로운 enum 값</param>
    /// <returns>원본 enum 값에 새로운 enum 값이 추가된 결과</returns>
    public static T Add(T original, T newState)
    {
        int originalValue = System.Convert.ToInt32(original);
        int newStateValue = System.Convert.ToInt32(newState);
        T state = (T)(object)(originalValue | newStateValue);
        return state;
    }

    /// <summary>
    /// 새로운 enum 값을 원본 enum 값에서 제거하는 함수
    /// </summary>
    /// <param name="original">변경할 원본 enum 값</param>
    /// <param name="newState">제거할 새로운 enum 값</param>
    /// <returns>원본 enum 값에서 새로운 enum값이 제거된 결과</returns>
    public static T Remove(T original, T newState)
    {
        long originalValue = System.Convert.ToInt64(original);
        long newStateValue = System.Convert.ToInt64(newState);
        T state = (T)System.Enum.ToObject(typeof(T), originalValue & ~newStateValue);
        return state;
    }
    
    /// <summary>
    /// 두 enum 값이 동일한지 확인하는 함수
    /// </summary>
    /// <param name="original">비교할 원본 enum 값</param>
    /// <param name="newState">비교할 새로운 enum 값</param>
    /// <returns>두 enum 값이 동일하면 true, 그렇지 않으면 false</returns>
    public static bool CheckEquals(T original, T newState)
    {
        return EqualityComparer<T>.Default.Equals(original, newState);
    }

    /// <summary>
    /// 새로운 enum 값이 원본 enum 값에 포함되어 있는지 확인하는 함수
    /// </summary>
    /// <param name="original">비교할 원본 enum 값</param>
    /// <param name="newState">비교할 새로운 enum 값</param>
    /// <returns>새로운 enum 값이 원본 enum 값에 포함되어 있으면 true, 그렇지 않으면 false</returns>
    public static bool CheckContains(T original, T newState)
    {
        long originalValue = System.Convert.ToInt64(original);
        long newStateValue = System.Convert.ToInt64(newState);
        return (originalValue & newStateValue) != 0;
    }
}
