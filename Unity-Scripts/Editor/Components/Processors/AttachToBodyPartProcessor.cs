// File: Packages/com.yucp.club.furyextension/Editor/com/yucp/club/furyextension/Editor/AttachToBodyPartProcessor.cs
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;  // IVRCSDKPreprocessAvatarCallback
using com.vrcfury.api;                   // FuryComponents
using com.vrcfury.api.Components;        // FuryArmatureLink
using yucp.club.furyextension.Components;

namespace yucp.club.furyextension.Editor
{
    public class AttachToBodyPartProcessor : IVRCSDKPreprocessAvatarCallback
    {
        public int callbackOrder => int.MinValue;

        public bool OnPreprocessAvatar(GameObject avatarRoot)
        {
            Debug.Log("[AttachToBodyPartProcessor] Running preprocess hook", avatarRoot);
            var dataList = avatarRoot.GetComponentsInChildren<AttachToBodyPartData>(true);
            foreach (var data in dataList)
            {
                Animator animator = avatarRoot.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    Debug.LogError("[AttachToBodyPartProcessor] Animator missing on avatar.", avatarRoot);
                    continue;
                }

                if (!data.TryResolveBone(animator, out HumanBodyBones bone))
                {
                    Debug.LogError($"[AttachToBodyPartProcessor] Cannot resolve bone part={data.part}, side={data.side}", data);
                    continue;
                }

                var link = FuryComponents.CreateArmatureLink(data.gameObject);
                if (link == null)
                {
                    Debug.LogError($"[AttachToBodyPartProcessor] Failed to create FuryArmatureLink on '{data.name}'", data);
                    continue;
                }

                link.LinkTo(bone, data.offset);
                Debug.Log($"[AttachToBodyPartProcessor] Linked '{data.name}' → {bone} offset='{data.offset}'", data);
            }
            return true;
        }
    }
}
