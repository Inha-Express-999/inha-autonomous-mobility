// Stylized Water 3 by Staggart Creations (http://staggart.xyz)
// COPYRIGHT PROTECTED UNDER THE UNITY ASSET STORE EULA (https://unity.com/legal/as-terms)
//    • Copying or referencing source code for the production of new asset store, or public, content is strictly prohibited!
//    • Uploading this file to a public repository will subject it to an automated DMCA takedown request.

using UnityEditor;
using UnityEngine;

namespace StylizedWater3
{
    [CustomEditor(typeof(WaterGrid))]
    public class CreateWaterGridInspector : Editor
    {
        private WaterGrid script;

        private SerializedProperty material;
        private SerializedProperty followSceneCamera;
        private SerializedProperty autoAssignCamera;
        private SerializedProperty followTarget;
        
        private SerializedProperty scale;
        private SerializedProperty vertexDistance;
        private SerializedProperty rowsColumns;
        
        private int vertexCount;
        
        private StylizedWaterEditor.MaterialSelector materialSelector;

        private void OnEnable()
        {
            script = (WaterGrid) target;
            script.m_rowsColumns = script.rowsColumns;

            material = serializedObject.FindProperty("material");
            followSceneCamera = serializedObject.FindProperty("followSceneCamera");
            autoAssignCamera = serializedObject.FindProperty("autoAssignCamera");
            followTarget = serializedObject.FindProperty("followTarget");
            
            scale = serializedObject.FindProperty("scale");
            vertexDistance = serializedObject.FindProperty("vertexDistance");
            rowsColumns = serializedObject.FindProperty("rowsColumns");

            materialSelector = new StylizedWaterEditor.MaterialSelector(new[]
            {
                "9a3a73f5564a39544a7958f1d9a318e8",
                "6cf5fe6b7ede58746b80d2a4c985e09c",
                "e8333e151973f1c4188ff534979c823b",
                "5d35554b48c05c3488160c6eb5354ac5",
                "c619752e7569e794fbd3df586f3d8249",
                "9eaadba20d5899341a463ca95a74ce58",
                "ac035e850020fe84dae1e5dd49abb816",
                "f62e0413f72de5c479f62fc6b9beea07",
                "e641fc50a6991f94488a292b9be307c9",
                "d9bee4d86b9f55b46a4288109fb2b181",
                "bd2dea484b1242b459507e84366c6b09",
                "99c221ab76521b54ca7e1fb8c0fd7be3",
                "973c9b04a35c7254680c406d9c302ce1",
                "fcf1aac0ecc2b9c4caec5df34421a231",
                "e49bb378b39a3e8488666e25804d6c32",
            });
        }
        
        public override void OnInspectorGUI()
        {
            UI.DrawHeader();

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth);
                WaterGrid.DisplayGrid = GUILayout.Toggle(WaterGrid.DisplayGrid , new GUIContent("  Display Grid", EditorGUIUtility.IconContent((WaterGrid.DisplayGrid ? "animationvisibilitytoggleon" : "animationvisibilitytoggleoff")).image), "Button");
                WaterGrid.DisplayWireframe = GUILayout.Toggle(WaterGrid.DisplayWireframe, new GUIContent("  Show Wireframe", EditorGUIUtility.IconContent((WaterGrid.DisplayWireframe ? "animationvisibilitytoggleon" : "animationvisibilitytoggleoff")).image), "Button");
            }
            
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField("Appearance", EditorStyles.boldLabel);
            UI.MaterialPropertyField(material, material.displayName, materialSelector, () =>
            {
                script.Recreate();
            });
            if(material.objectReferenceValue == null) EditorGUILayout.HelpBox("A material must be assigned", MessageType.Error);
            
            EditorGUILayout.Space();
            
            EditorGUILayout.LabelField("Movement", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(followSceneCamera);
            using (new EditorGUI.DisabledScope(autoAssignCamera.boolValue))
            {
                EditorGUILayout.PropertyField(followTarget);
            }
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(autoAssignCamera);
            EditorGUI.indentLevel--;
            
            EditorGUILayout.Space();

            EditorGUILayout.LabelField("Grid geometry", EditorStyles.boldLabel);

            EditorGUILayout.PropertyField(scale, GUILayout.MaxWidth(EditorGUIUtility.labelWidth + 95f));

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(rowsColumns.displayName);
                using (new EditorGUI.DisabledScope(rowsColumns.intValue <= 0))
                {
                    if (GUILayout.Button("-", EditorStyles.miniButtonLeft, GUILayout.Width(25f)))
                    {
                        rowsColumns.intValue--;
                    }
                }
                EditorGUILayout.PropertyField(rowsColumns, GUIContent.none, GUILayout.MaxWidth(40f));
                if (GUILayout.Button("+", EditorStyles.miniButtonRight, GUILayout.Width(25f)))
                {
                    rowsColumns.intValue++;
                }
                EditorGUILayout.LabelField($"= {rowsColumns.intValue * rowsColumns.intValue} tiles", EditorStyles.miniLabel);
            }
            
            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(vertexDistance, new GUIContent("Min. vertex distance", vertexDistance.tooltip));
            vertexCount = Mathf.FloorToInt(((scale.floatValue / rowsColumns.intValue) / vertexDistance.floatValue) * ((scale.floatValue / rowsColumns.intValue) / vertexDistance.floatValue));
            //EditorGUILayout.HelpBox($"Vertex count: {vertexCount}", MessageType.None);

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                
                //Executed here since objects can't be destroyed from OnValidate
                script.Recreate();
            }
            
            UI.DrawFooter();
        }
    }
}