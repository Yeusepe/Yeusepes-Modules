    // File: Packages/com.yucp.club.furyextension/Runtime/com/yucp/club/furyextension/Components/AttachToBodyPartData.cs
using UnityEngine;
using VRC.SDKBase;  // for IEditorOnly & IPreprocessCallbackBehaviour

namespace yucp.club.furyextension.Components
{
    [AddComponentMenu("Symmetric Armature Auto-Link")]
    [HelpURL("https://github.com/Yeusepe/Yeusepes-Modules")] // ← Optional link-out button
    [DisallowMultipleComponent]
    public class AttachToBodyPartData : MonoBehaviour, IEditorOnly, IPreprocessCallbackBehaviour
    {
        public enum BodyPart
        {
            UpperLeg, LowerLeg, Foot, Toes,
            Shoulder, UpperArm, LowerArm, Hand,
            ThumbProximal, ThumbIntermediate, ThumbDistal,
            IndexProximal, IndexIntermediate, IndexDistal,
            MiddleProximal, MiddleIntermediate, MiddleDistal,
            RingProximal, RingIntermediate, RingDistal,
            LittleProximal, LittleIntermediate, LittleDistal,
            Eye
        }

        public enum Side
        {
            Left, Right, Closest
        }

        [Tooltip("Which symmetric body part to attach to.")]
        public BodyPart part;

        [Tooltip("For symmetric parts, pick a side (or Closest).")]
        public Side side = Side.Closest;

        [Tooltip("Optional offset string for fine tuning the attachment.")]
        public string offset = "";

        // build-time stub
        public int PreprocessOrder => 0;
        public bool OnPreprocess() => true;

        // runtime parent-to-bone
        private void Awake()
        {
            Animator animator = GetComponentInParent<Animator>();
            if (animator == null)
            {
                Debug.LogError($"[AttachToBodyPartData] No Animator on parents of '{name}'", this);
                return;
            }

            if (!TryResolveBone(animator, out HumanBodyBones bone))
            {
                Debug.LogError($"[AttachToBodyPartData] Could not resolve bone for part={part}, side={side}", this);
                return;
            }

            Transform boneT = animator.GetBoneTransform(bone);
            if (boneT == null)
            {
                Debug.LogError($"[AttachToBodyPartData] Bone '{bone}' not found on '{animator.gameObject.name}'", this);
                return;
            }

            transform.SetParent(boneT, worldPositionStays: true);
            Debug.Log($"[AttachToBodyPartData] Runtime parent '{name}' → {bone}", this);
        }

        public bool TryResolveBone(Animator animator, out HumanBodyBones result)
        {
            Transform leftT = null, rightT = null;
            HumanBodyBones leftB = HumanBodyBones.Hips, rightB = HumanBodyBones.Hips;

            switch (part)
            {
                case BodyPart.UpperLeg:
                    leftB = HumanBodyBones.LeftUpperLeg; rightB = HumanBodyBones.RightUpperLeg; break;
                case BodyPart.LowerLeg:
                    leftB = HumanBodyBones.LeftLowerLeg; rightB = HumanBodyBones.RightLowerLeg; break;
                case BodyPart.Foot:
                    leftB = HumanBodyBones.LeftFoot; rightB = HumanBodyBones.RightFoot; break;
                case BodyPart.Toes:
                    leftB = HumanBodyBones.LeftToes; rightB = HumanBodyBones.RightToes; break;
                case BodyPart.Shoulder:
                    leftB = HumanBodyBones.LeftShoulder; rightB = HumanBodyBones.RightShoulder; break;
                case BodyPart.UpperArm:
                    leftB = HumanBodyBones.LeftUpperArm; rightB = HumanBodyBones.RightUpperArm; break;
                case BodyPart.LowerArm:
                    leftB = HumanBodyBones.LeftLowerArm; rightB = HumanBodyBones.RightLowerArm; break;
                case BodyPart.Hand:
                    leftB = HumanBodyBones.LeftHand; rightB = HumanBodyBones.RightHand; break;
                case BodyPart.ThumbProximal:
                    leftB = HumanBodyBones.LeftThumbProximal; rightB = HumanBodyBones.RightThumbProximal; break;
                case BodyPart.ThumbIntermediate:
                    leftB = HumanBodyBones.LeftThumbIntermediate; rightB = HumanBodyBones.RightThumbIntermediate; break;
                case BodyPart.ThumbDistal:
                    leftB = HumanBodyBones.LeftThumbDistal; rightB = HumanBodyBones.RightThumbDistal; break;
                case BodyPart.IndexProximal:
                    leftB = HumanBodyBones.LeftIndexProximal; rightB = HumanBodyBones.RightIndexProximal; break;
                case BodyPart.IndexIntermediate:
                    leftB = HumanBodyBones.LeftIndexIntermediate; rightB = HumanBodyBones.RightIndexIntermediate; break;
                case BodyPart.IndexDistal:
                    leftB = HumanBodyBones.LeftIndexDistal; rightB = HumanBodyBones.RightIndexDistal; break;
                case BodyPart.MiddleProximal:
                    leftB = HumanBodyBones.LeftMiddleProximal; rightB = HumanBodyBones.RightMiddleProximal; break;
                case BodyPart.MiddleIntermediate:
                    leftB = HumanBodyBones.LeftMiddleIntermediate; rightB = HumanBodyBones.RightMiddleIntermediate; break;
                case BodyPart.MiddleDistal:
                    leftB = HumanBodyBones.LeftMiddleDistal; rightB = HumanBodyBones.RightMiddleDistal; break;
                case BodyPart.RingProximal:
                    leftB = HumanBodyBones.LeftRingProximal; rightB = HumanBodyBones.RightRingProximal; break;
                case BodyPart.RingIntermediate:
                    leftB = HumanBodyBones.LeftRingIntermediate; rightB = HumanBodyBones.RightRingIntermediate; break;
                case BodyPart.RingDistal:
                    leftB = HumanBodyBones.LeftRingDistal; rightB = HumanBodyBones.RightRingDistal; break;
                case BodyPart.LittleProximal:
                    leftB = HumanBodyBones.LeftLittleProximal; rightB = HumanBodyBones.RightLittleProximal; break;
                case BodyPart.LittleIntermediate:
                    leftB = HumanBodyBones.LeftLittleIntermediate; rightB = HumanBodyBones.RightLittleIntermediate; break;
                case BodyPart.LittleDistal:
                    leftB = HumanBodyBones.LeftLittleDistal; rightB = HumanBodyBones.RightLittleDistal; break;
                case BodyPart.Eye:
                    leftB = HumanBodyBones.LeftEye; rightB = HumanBodyBones.RightEye; break;
                default:
                    result = HumanBodyBones.Hips;
                    return false;
            }

            leftT = animator.GetBoneTransform(leftB);
            rightT = animator.GetBoneTransform(rightB);
            result = ChooseSide(leftT, rightT, leftB, rightB);
            return true;
        }

        private HumanBodyBones ChooseSide(
            Transform leftT, Transform rightT,
            HumanBodyBones leftB, HumanBodyBones rightB)
        {
            switch (side)
            {
                case Side.Left:
                    return (leftT != null) ? leftB : rightB;
                case Side.Right:
                    return (rightT != null) ? rightB : leftB;
                case Side.Closest:
                    if (leftT == null) return rightB;
                    if (rightT == null) return leftB;
                    float dL = Vector3.Distance(transform.position, leftT.position);
                    float dR = Vector3.Distance(transform.position, rightT.position);
                    return (dL <= dR) ? leftB : rightB;
                default:
                    return leftB;
            }
        }
    }
}
