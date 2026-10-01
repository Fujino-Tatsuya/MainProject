using UnityEngine;

namespace EuniTween
{
    // 진행도 t(0~1)를 이징된 값으로 바꾼다. 입력은 0~1로 고정하고, 출력은 Back/Elastic 처럼
    // 범위를 넘을 수 있다. 공식 = easings.net(Penner) 기준. 상태가 없는 순수 함수라 어디서든 호출해도 된다.
    public static class Easing
    {
        const float BackOvershoot = 1.70158f;
        const float BackInOutOvershoot = BackOvershoot * 1.525f;
        const float ElasticPeriod = 2f * Mathf.PI / 3f;
        const float ElasticInOutPeriod = 2f * Mathf.PI / 4.5f;

        public static float Evaluate(Ease ease, float t)
        {
            t = Mathf.Clamp01(t);

            switch (ease)
            {
                case Ease.Linear: return t;

                case Ease.InSine: return 1f - Mathf.Cos(t * Mathf.PI * 0.5f);
                case Ease.OutSine: return Mathf.Sin(t * Mathf.PI * 0.5f);
                case Ease.InOutSine: return -(Mathf.Cos(Mathf.PI * t) - 1f) * 0.5f;

                case Ease.InQuad: return t * t;
                case Ease.OutQuad: return 1f - (1f - t) * (1f - t);
                case Ease.InOutQuad: return t < 0.5f ? 2f * t * t : 1f - Pow(-2f * t + 2f, 2) * 0.5f;

                case Ease.InCubic: return t * t * t;
                case Ease.OutCubic: return 1f - Pow(1f - t, 3);
                case Ease.InOutCubic: return t < 0.5f ? 4f * t * t * t : 1f - Pow(-2f * t + 2f, 3) * 0.5f;

                case Ease.InQuart: return Pow(t, 4);
                case Ease.OutQuart: return 1f - Pow(1f - t, 4);
                case Ease.InOutQuart: return t < 0.5f ? 8f * Pow(t, 4) : 1f - Pow(-2f * t + 2f, 4) * 0.5f;

                case Ease.InQuint: return Pow(t, 5);
                case Ease.OutQuint: return 1f - Pow(1f - t, 5);
                case Ease.InOutQuint: return t < 0.5f ? 16f * Pow(t, 5) : 1f - Pow(-2f * t + 2f, 5) * 0.5f;

                case Ease.InExpo: return t <= 0f ? 0f : Mathf.Pow(2f, 10f * t - 10f);
                case Ease.OutExpo: return t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
                case Ease.InOutExpo:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return t < 0.5f
                        ? Mathf.Pow(2f, 20f * t - 10f) * 0.5f
                        : (2f - Mathf.Pow(2f, -20f * t + 10f)) * 0.5f;

                case Ease.InCirc: return 1f - Mathf.Sqrt(1f - t * t);
                case Ease.OutCirc: return Mathf.Sqrt(1f - (t - 1f) * (t - 1f));
                case Ease.InOutCirc:
                    return t < 0.5f
                        ? (1f - Mathf.Sqrt(1f - 4f * t * t)) * 0.5f
                        : (Mathf.Sqrt(1f - Pow(-2f * t + 2f, 2)) + 1f) * 0.5f;

                case Ease.InBack: return (BackOvershoot + 1f) * t * t * t - BackOvershoot * t * t;
                case Ease.OutBack:
                    return 1f + (BackOvershoot + 1f) * Pow(t - 1f, 3) + BackOvershoot * Pow(t - 1f, 2);
                case Ease.InOutBack:
                    return t < 0.5f
                        ? Pow(2f * t, 2) * ((BackInOutOvershoot + 1f) * 2f * t - BackInOutOvershoot) * 0.5f
                        : (Pow(2f * t - 2f, 2) * ((BackInOutOvershoot + 1f) * (2f * t - 2f) + BackInOutOvershoot) + 2f) * 0.5f;

                case Ease.InElastic:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return -Mathf.Pow(2f, 10f * t - 10f) * Mathf.Sin((t * 10f - 10.75f) * ElasticPeriod);
                case Ease.OutElastic:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return Mathf.Pow(2f, -10f * t) * Mathf.Sin((t * 10f - 0.75f) * ElasticPeriod) + 1f;
                case Ease.InOutElastic:
                    if (t <= 0f) return 0f;
                    if (t >= 1f) return 1f;
                    return t < 0.5f
                        ? -(Mathf.Pow(2f, 20f * t - 10f) * Mathf.Sin((20f * t - 11.125f) * ElasticInOutPeriod)) * 0.5f
                        : Mathf.Pow(2f, -20f * t + 10f) * Mathf.Sin((20f * t - 11.125f) * ElasticInOutPeriod) * 0.5f + 1f;

                case Ease.InBounce: return 1f - OutBounce(1f - t);
                case Ease.OutBounce: return OutBounce(t);
                case Ease.InOutBounce:
                    return t < 0.5f
                        ? (1f - OutBounce(1f - 2f * t)) * 0.5f
                        : (1f + OutBounce(2f * t - 1f)) * 0.5f;

                default: return t;
            }
        }

        // 0 → 1 → 0 으로 한 번 튀었다 돌아오는 값. 피격 펀치 스케일처럼 "제자리로 돌아오는" 연출용.
        // ease 는 올라가는 절반에 적용하고, 내려오는 절반은 그 거울이다.
        public static float Punch(Ease ease, float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? Evaluate(ease, t * 2f) : Evaluate(ease, (1f - t) * 2f);
        }

        static float OutBounce(float t)
        {
            const float n = 7.5625f;
            const float d = 2.75f;

            if (t < 1f / d)
                return n * t * t;
            if (t < 2f / d)
                return n * (t -= 1.5f / d) * t + 0.75f;
            if (t < 2.5f / d)
                return n * (t -= 2.25f / d) * t + 0.9375f;
            return n * (t -= 2.625f / d) * t + 0.984375f;
        }

        static float Pow(float value, int exponent)
        {
            float result = 1f;
            for (int i = 0; i < exponent; i++)
                result *= value;
            return result;
        }
    }
}
