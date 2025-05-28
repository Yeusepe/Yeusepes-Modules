// Assets/YUCP Assets/Common/Editor/Components/MyCustomComponentProcessor.cs
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;    // for IVRCSDKPreprocessAvatarCallback

namespace yucp.club.furyextension.Editor
{
    public class MyCustomComponentProcessor : IVRCSDKPreprocessAvatarCallback
    {
        // ordering relative to other preprocessors
        public int callbackOrder => 0;

        // ---> must match this exactly:
        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            // find all instances of your component on the avatar
            var comps = avatarRoot
                .GetComponentsInChildren<yucp.club.furyextension.Components.MyCustomComponent>(true);

            foreach (var comp in comps)
            {
                // your build-time logic here…
                bool flag = comp.exampleFlag;
            }

            // return true to indicate “no errors, continue processing”
            return true;
        }
    }
}
