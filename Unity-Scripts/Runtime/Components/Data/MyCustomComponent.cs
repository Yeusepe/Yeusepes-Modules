// Runtime/Components/MyCustomComponent.cs
using UnityEngine;
using VRC.SDKBase;  // brings in IEditorOnly & IPreprocessCallbackBehaviour

namespace yucp.club.furyextension.Components
{
    public class MyCustomComponent : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        [Tooltip("Example toggle flag")]
        public bool exampleFlag;

        // The order you want this to run relative to other preprocessors
        public int PreprocessOrder => 0;

        // <-- SIGNATURE MUST BE bool, NOT void
        public bool OnPreprocess()
        {
            // Here your build-time logic runs on each instance
            // e.g. var avatarRoot = transform.root.gameObject;
            bool flag = exampleFlag;

            // return true to indicate “no error, go on”
            return true;
        }
    }
}
