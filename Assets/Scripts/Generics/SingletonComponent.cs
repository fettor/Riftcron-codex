using System;
using UnityEngine;

namespace Tuntenfisch.Generics
{
    public abstract class SingletonComponent<T> : MonoBehaviour where T : MonoBehaviour
    {
        public static T Instance
        {
            get
            {
                if (s_instance == null)
                {

#if UNITY_2023_1_OR_NEWER
                    s_instance = FindFirstObjectByType<T>();
#else
                    s_instance = FindObjectOfType<T>();
#endif

                    if (s_instance == null)
                    {
                        throw new ArgumentNullException(nameof(s_instance));
                    }
                }

                return s_instance;
            }
        }

        private static T s_instance = default;
    }
}
