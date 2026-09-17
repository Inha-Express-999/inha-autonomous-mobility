using System;
using UnityEditor;
using UnityEditor.AnimatedValues;
using UnityEditor.Rendering;
using UnityEngine;

namespace StylizedWater3
{
    public static partial class UI
    {
        public static class Material
        {
            //Section toggles
            public class Section
            {
                public bool Expanded
                {
                    get { return SessionState.GetBool(id, false); }
                    set { SessionState.SetBool(id, value); }
                }
                public AnimBool anim;

                public readonly string id;
                public GUIContent title;

                public Section(MaterialEditor owner, string id, GUIContent title)
                {
                    this.id = AssetInfo.ASSET_ABRV + "_" + id + "_SECTION";
                    this.title = title;

                    anim = new AnimBool(true);
                    anim.valueChanged.AddListener(owner.Repaint);
                    anim.speed = ANIM_SPEED;
                    anim.target = Expanded;
                }
                
                public void DrawHeader(Action clickAction)
                {
                    UI.Material.DrawHeader(title, Expanded, clickAction);
                    anim.target = Expanded;
                }
            }
            
            private const float HeaderHeight = 25f;

            //https://github.com/Unity-Technologies/Graphics/blob/d0473769091ff202422ad13b7b764c7b6a7ef0be/com.unity.render-pipelines.core/Editor/CoreEditorUtils.cs#L460
            public static bool DrawHeader(GUIContent content, bool isExpanded, Action clickAction = null)
            {
#if URP
                CoreEditorUtils.DrawSplitter();
#endif

                Rect backgroundRect = GUILayoutUtility.GetRect(1f, HeaderHeight);
                
                //Negate room made for parameter locking (material variants functionality)
                backgroundRect.xMin -= 15f;

                var labelRect = backgroundRect;
                labelRect.xMin += 10f;
                labelRect.xMax -= 20f + 16 + 5;

                var foldoutRect = backgroundRect;
                foldoutRect.xMin -= 8f;
                foldoutRect.width = HeaderHeight;
                foldoutRect.height = HeaderHeight;

                // Background rect should be full-width
                backgroundRect.xMin = 0f;
                backgroundRect.width += 4f;

                // Background
                float backgroundTint = (EditorGUIUtility.isProSkin ? 0.1f : 1f);
                if (backgroundRect.Contains(Event.current.mousePosition)) backgroundTint *= EditorGUIUtility.isProSkin ? 1.5f : 0.9f;
                
                EditorGUI.DrawRect(backgroundRect, new Color(backgroundTint, backgroundTint, backgroundTint, 0.2f));

                if (content.image != null)
                {
                    Rect iconRect = labelRect;
                    iconRect.width = 19;
                    iconRect.height = 19;
                    iconRect.y += 2f;
                    GUI.DrawTexture(iconRect, content.image);
                    
                    labelRect.x += iconRect.width + 4f;
                }
                // Title
                EditorGUI.LabelField(labelRect, new GUIContent(content.text, content.tooltip), EditorStyles.boldLabel);

                // Foldout
                GUI.Label(foldoutRect, new GUIContent(isExpanded ? "−" : "≡"), EditorStyles.boldLabel);
                
                // Handle events
                var e = Event.current;

                if (e.type == EventType.MouseDown)
                {
                    if (backgroundRect.Contains(e.mousePosition))
                    {
                        if (e.button == 0)
                        {
                            isExpanded = !isExpanded;
                            if(clickAction != null) clickAction.Invoke();
                        }

                        e.Use();
                    }
                }
                
                return isExpanded;
            }
            
            public static void DrawIntSlider(MaterialProperty prop, string label = null, string tooltip = null)
            {
                MaterialEditor.BeginProperty(prop);

                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = prop.hasMixedValue;
                
                float value = (float)EditorGUILayout.IntSlider(new GUIContent(label ?? prop.displayName, null, tooltip), (int)prop.floatValue, (int)prop.rangeLimits.x, (int)prop.rangeLimits.y);
                
                if (ExpandTooltips && tooltip != string.Empty) EditorGUILayout.HelpBox(tooltip, MessageType.None);

                if (EditorGUI.EndChangeCheck())
                    prop.floatValue = value;
                EditorGUI.showMixedValue = false;
                
                MaterialEditor.EndProperty();
            }

            public static void DrawFloatField(MaterialProperty prop, string label = null, string tooltip = null)
            {
                MaterialEditor.BeginProperty(prop);

                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = prop.hasMixedValue;
                
                float value = EditorGUILayout.FloatField(new GUIContent(label ?? prop.displayName, null, tooltip), prop.floatValue, GUILayout.MaxWidth(EditorGUIUtility.labelWidth + 50f));
                
                if (ExpandTooltips && tooltip != null) EditorGUILayout.HelpBox(tooltip, MessageType.None);

                if (EditorGUI.EndChangeCheck())
                    prop.floatValue = value;
                EditorGUI.showMixedValue = false;
                
                MaterialEditor.EndProperty();
            }

            public static void DrawFloatTicker(MaterialProperty prop, string label = null, string tooltip = null, bool showReverse = false)
            {
                MaterialEditor.BeginProperty(prop);

                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = prop.hasMixedValue;
                
                float value = m_DrawFloatTicker(prop.floatValue, label ?? prop.displayName, tooltip, showReverse);
                
                if (EditorGUI.EndChangeCheck())
                    prop.floatValue = value;
                EditorGUI.showMixedValue = false;
                
                MaterialEditor.EndProperty();
            }

            private static float m_DrawFloatTicker(float value, string label = null, string tooltip = null, bool showReverse = false)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PrefixLabel(new GUIContent(label, null, tooltip));
                if (GUILayout.Button(new GUIContent("«", "-0.1"), EditorStyles.miniButtonLeft, GUILayout.Width(20)))
                {
                    value -= 0.1f;
                }
                if (GUILayout.Button(new GUIContent("‹", "-0.01"), EditorStyles.miniButtonMid, GUILayout.Width(20)))
                {
                    value -= 0.01f;
                }
                
                value = EditorGUILayout.FloatField(value, GUILayout.MaxWidth(EditorGUIUtility.fieldWidth));

                if (GUILayout.Button(new GUIContent("›", "+0.01"), EditorStyles.miniButtonMid, GUILayout.Width(20)))
                {
                    value += 0.01f;
                }
                if (GUILayout.Button(new GUIContent("»", "+0.1"), EditorStyles.miniButtonRight, GUILayout.Width(20)))
                {
                    value += 0.1f;
                }

                if (showReverse)
                {
                    if (GUILayout.Button("Reverse", EditorStyles.miniButton, GUILayout.MaxWidth(70f)))
                    {
                        value = -value;
                    }
                }
                EditorGUILayout.EndHorizontal();
                
                if (ExpandTooltips && tooltip != null) EditorGUILayout.HelpBox(tooltip, MessageType.None);

                return value;
            }
            
            public static void DrawMinMaxSlider(MaterialProperty prop, float min, float max, string label = null, string tooltip = null)
            {
                MaterialEditor.BeginProperty(prop);

                float minVal = prop.vectorValue.x;
                float maxVal = prop.vectorValue.y;

                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = prop.hasMixedValue;
                
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent(label, null, tooltip), GUILayout.MaxWidth(EditorGUIUtility.labelWidth));
                    minVal = EditorGUILayout.FloatField(minVal, GUILayout.Width(EditorGUIUtility.fieldWidth));
                    EditorGUILayout.MinMaxSlider(ref minVal, ref maxVal, min, max);
                    maxVal = EditorGUILayout.FloatField(maxVal, GUILayout.Width(EditorGUIUtility.fieldWidth));
                }
                
                if (ExpandTooltips && tooltip != null) EditorGUILayout.HelpBox(tooltip, MessageType.None);

                if (EditorGUI.EndChangeCheck())
                {
                    prop.vectorValue = new Vector4(minVal, maxVal);
                }
                EditorGUI.showMixedValue = false;
                
                MaterialEditor.EndProperty();
            }

            public static void DrawColorField(MaterialProperty prop, bool hdr, string name = null, string tooltip = null)
            {
                MaterialEditor.BeginProperty(prop);

                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = prop.hasMixedValue;

                Color color;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel(new GUIContent(name ?? prop.displayName, tooltip));
                    color = EditorGUILayout.ColorField(new GUIContent("", null, tooltip), prop.colorValue, true, true, hdr, GUILayout.MaxWidth(60f));
                    
                    EditorGUILayout.LabelField($"Opacity: {Math.Round(prop.colorValue.a * 100f, 2)}%", EditorStyles.miniLabel);
                }

                if (ExpandTooltips && tooltip != null) EditorGUILayout.HelpBox(tooltip, MessageType.None);

                if (EditorGUI.EndChangeCheck())
                    prop.colorValue = color;
                EditorGUI.showMixedValue = false;
                
                MaterialEditor.EndProperty();
            }

            public static void DrawVector2(MaterialProperty prop, string label, string tooltip = null)
            {
                MaterialEditor.BeginProperty(prop);

                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = prop.hasMixedValue;

                Vector2 value;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel(new GUIContent(label, tooltip));
                    value = EditorGUILayout.Vector2Field(new GUIContent("", null, tooltip), prop.vectorValue);
                }
                
                if (ExpandTooltips && tooltip != null) EditorGUILayout.HelpBox(tooltip, MessageType.None);

                if (EditorGUI.EndChangeCheck())
                    prop.vectorValue = value;
                EditorGUI.showMixedValue = false;

                MaterialEditor.EndProperty();
            }
            
            public static void DrawVector2Ticker(MaterialProperty prop, string label, string tooltip = null)
            {
                MaterialEditor.BeginProperty(prop);

                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = prop.hasMixedValue;

                Vector2 value = prop.vectorValue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PrefixLabel(new GUIContent(label, tooltip));

                    if (GUILayout.Button(new GUIContent("«", "-0.1"), EditorStyles.miniButtonLeft, GUILayout.Width(20)))
                    {
                        value.x -= 0.1f;
                        value.y -= 0.1f;
                    }
                    if (GUILayout.Button(new GUIContent("‹", "-0.01"), EditorStyles.miniButtonMid, GUILayout.Width(20)))
                    {
                        value.x -= 0.01f;
                        value.y -= 0.01f;
                    }
                    
                    value = EditorGUILayout.Vector2Field(new GUIContent("", null, tooltip), value, GUILayout.MaxWidth(EditorGUIUtility.fieldWidth * 2f));

                    if (GUILayout.Button(new GUIContent("›", "+0.01"), EditorStyles.miniButtonMid, GUILayout.Width(20)))
                    {
                        value.x += 0.01f;
                        value.y += 0.01f;
                    }
                    if (GUILayout.Button(new GUIContent("»", "+0.1"), EditorStyles.miniButtonRight, GUILayout.Width(20)))
                    {
                        value.x += 0.1f;
                        value.y += 0.1f;
                    }
                }
                
                if (ExpandTooltips && tooltip != null) EditorGUILayout.HelpBox(tooltip, MessageType.None);

                if (EditorGUI.EndChangeCheck())
                {
                    value.x = Mathf.Max(value.x, 0.01f);
                    value.y = Mathf.Max(value.y, 0.01f);
                    
                    prop.vectorValue = value;
                }
                EditorGUI.showMixedValue = false;
                
                MaterialEditor.EndProperty();
            }
        }
    }
}