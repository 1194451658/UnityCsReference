// Unity C# reference source
// Copyright (c) Unity Technologies. For terms of use, see
// https://unity3d.com/legal/licenses/Unity_Reference_Only_License

using System;
using UnityEngine;
using UnityEngine.Events;

namespace UnityEditor.AnimatedValues
{
    public abstract class BaseAnimValue<T>
    {
        private T m_Start;

        [SerializeField]
        private T m_Target;

        private double m_LastTime;
        private double m_LerpPosition = 1f;

        public float speed = 2f;

        [NonSerialized]
        public UnityEvent valueChanged;

        private bool m_Animating;

        protected BaseAnimValue(T value)
        {
            // 开始值
            m_Start = value;
            // 结束值
            m_Target = value;

            // 值被更改回调
            valueChanged = new UnityEvent();
        }

        protected BaseAnimValue(T value, UnityAction callback)
        {
            // 开始值
            m_Start = value;
            // 结束值
            m_Target = value;
            // 值被更改回调
            valueChanged = new UnityEvent();
            valueChanged.AddListener(callback);
        }

        // 把值val，限制在[min, max]之内
        private static T2 Clamp<T2>(T2 val, T2 min, T2 max) where T2 : IComparable<T2>
        {
            if (val.CompareTo(min) < 0) return min;
            if (val.CompareTo(max) > 0) return max;
            return val;
        }

        // 开始值动画
        // newStart: 开始值
        // newTarget: 结束值
        protected void BeginAnimating(T newTarget, T newStart)
        {
            m_Start = newStart;
            m_Target = newTarget;
            
            // 直接，自己使用EditorApplication.update，进行更新
            EditorApplication.update += Update;

            // 标记动画开始
            m_Animating = true;
            m_LastTime = EditorApplication.timeSinceStartup;

            // 当前
            m_LerpPosition = 0;
        }

        // 是否正在动画中
        public bool isAnimating
        {
            get { return m_Animating; }
        }

        // EditorApplication.update
        private void Update()
        {
            if (!m_Animating)
                return;

            // update the lerpPosition
            // 将m_LerpPosition从0开始，按照速度speed，加速到1
            UpdateLerpPosition();

            // 值更改回调
            if (valueChanged != null)
                valueChanged.Invoke();

            // 变量lerpPosition:
            //  * 从1到0
            //  * 凸起来的
            if (lerpPosition >= 1f)
            {
                m_Animating = false;
                EditorApplication.update -= Update;
            }
        }


        //  返回，根据m_LerpPosition得到的值, 
        //  * 从1到0
        //  * 凸起来的
        protected float lerpPosition
        {
            get
            {
                // m_LerpPosition: 
                //  * 从0到1
                // v: 
                //  * 从1到0；
                //  * 线性
                var v = 1.0 - m_LerpPosition;

                // result: 
                //  * 从1到0
                //  * 凸起来的
                var result = 1.0 - v * v * v * v;
                return (float)result;
            }
        }

        // 跟新m_LerpPosition的值
        // 将m_LerpPosition从0开始，按照速度speed，加速到1
        private void UpdateLerpPosition()
        {
            double nowTime = EditorApplication.timeSinceStartup;

            // Update()经过的时间
            double deltaTime = nowTime - m_LastTime;

            // m_LerpPosition从0开始，按照速度speed，加速到1
            m_LerpPosition = Clamp(m_LerpPosition + (deltaTime * speed), 0.0, 1.0);
            m_LastTime = nowTime;
        }

        // 结束动画
        //  * 直接设置值到newValue
        protected void StopAnim(T newValue)
        {
            // If the new value is different, or we might be in the middle of a fade, we need to refresh.
            // Checking GetValue is not reliable on its own, since for e.g. bool it'll return the "closest" value,
            // but that doesn't mean the fade is done.
            bool invoke = false;
            if ((!newValue.Equals(GetValue()) || m_LerpPosition < 1) && valueChanged != null)
                invoke = true;

            m_Target = newValue;
            m_Start = newValue;
            m_LerpPosition = 1;
            m_Animating = false;
            // Only refresh *after* we set the correct new value.
            if (invoke)
                valueChanged.Invoke();
        }

        protected T start
        {
            get { return m_Start; }
        }

        public T target
        {
            get { return m_Target; }
            set
            {
                if (!m_Target.Equals(value))
                    BeginAnimating(value, this.value);
            }
        }

        public T value
        {
            get { return GetValue(); }
            set { StopAnim(value); }
        }

        protected abstract T GetValue();
    }

    [Serializable]
    public class AnimFloat : BaseAnimValue<float>
    {
        [SerializeField]
        private float m_Value;

        public AnimFloat(float value)
            : base(value)
        {}

        public AnimFloat(float value, UnityAction callback) : base(value, callback)
        {}

        protected override float GetValue()
        {
            m_Value = Mathf.Lerp(start, target, lerpPosition);
            return m_Value;
        }
    }

    [Serializable]
    public class AnimVector3 : BaseAnimValue<Vector3>
    {
        [SerializeField]
        private Vector3 m_Value;

        public AnimVector3()
            : base(Vector3.zero)
        {}

        public AnimVector3(Vector3 value)
            : base(value)
        {}

        public AnimVector3(Vector3 value, UnityAction callback)
            : base(value, callback)
        {}

        protected override Vector3 GetValue()
        {
            m_Value = Vector3.Lerp(start, target, lerpPosition);
            return m_Value;
        }
    }

    [Serializable]
    public class AnimBool : BaseAnimValue<bool>
    {
        [SerializeField]
        private float m_Value;

        public AnimBool()
            : base(false)
        {}

        public AnimBool(bool value)
            : base(value)
        {}

        public AnimBool(UnityAction callback)
            : base(false, callback)
        {}

        public AnimBool(bool value, UnityAction callback)
            : base(value, callback)
        {}

        public float faded
        {
            get
            {
                GetValue();
                return m_Value;
            }
        }

        protected override bool GetValue()
        {
            float startVal = target ? 0f : 1f;
            float end = 1f - startVal;

            m_Value = Mathf.Lerp(startVal, end, lerpPosition);

            return m_Value > .5f;
        }

        public float Fade(float from, float to)
        {
            return Mathf.Lerp(from, to, faded);
        }
    }

    [Serializable]
    public class AnimQuaternion : BaseAnimValue<Quaternion>
    {
        [SerializeField]
        private Quaternion m_Value;


        public AnimQuaternion(Quaternion value)
            : base(value)
        {}

        public AnimQuaternion(Quaternion value, UnityAction callback)
            : base(value, callback)
        {}

        protected override Quaternion GetValue()
        {
            m_Value = Quaternion.Slerp(start, target, lerpPosition);
            return m_Value;
        }
    }
}
//namespace
