using UnityEditor;
using UnityEngine.UIElements;
using yucp.club.furyextension.Components;
namespace yucp.club.furyextension.Resources
{    
    /// <summary>
    /// Custom Editor that injects the header overlay for AttachToBodyPartData.
    /// </summary>
    [CustomEditor(typeof(AttachToBodyPartData))]
    public class AttachToBodyPartDataEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root = new VisualElement();
            // Add custom header
            root.Add(YUCPComponentHeader.CreateHeaderOverlay("Symetric Armature Auto-Link"));
            // Draw properties excluding m_Script
            var container = new IMGUIContainer(() => {
                serializedObject.Update();
                DrawPropertiesExcluding(serializedObject, "m_Script");
                serializedObject.ApplyModifiedProperties();
            });
            root.Add(container);
            return root;
        }
    }
}