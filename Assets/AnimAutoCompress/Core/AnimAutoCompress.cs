using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AnimAutoCompress
{
    [Serializable]
    public class AnimAndPMap
    {
        public AvatarMask avatarMask;
        public AnimParamSetting animParamSetting;
        public List<string> ignoreRemoveList = new List<string>();
    }
    
    [Serializable]
    public class AnimParamSetting
    {
        public int retainDecimals = 3;
        public bool resampleCurves = true;
            
        public ModelImporterAnimationCompression animationCompression = ModelImporterAnimationCompression.Optimal;
        public float rotationError = 0.5f;
        public float positionError = 0.5f;
        public float scaleError = 0.5f;
    }
    
    [CreateAssetMenu(fileName = "AnimAutoCompress.asset", menuName = "AnimAutoCompress")]
    public class AnimAutoCompress : ScriptableObject
    {
        //公共参数
        public string storePath = "Assets/TempAnimationClip";
        public AnimationClip animClip;
        public AvatarMask uniformMask;
        public bool removeScaleCurves = true;
        public List<string> ignoreRemoveList = new List<string>() { "_scale$" };
        
        //高中低三档
        public AnimParamSetting highLevelCompress = new AnimParamSetting();
        public AnimParamSetting lowLevelCompress = new AnimParamSetting();
        public AnimParamSetting noCompress = new AnimParamSetting();
        
        //高中低三档的List，存历史记录
        //public List<AnimationClip> highLevelCompressList = new List<AnimationClip>();
        public List<AnimationClip> lowLevelCompressList = new List<AnimationClip>();
        public List<AnimationClip> noCompressList = new List<AnimationClip>();

        public AnimationClip HighCompressAnimAndPMap()
        {
            return CompressAnimAndPMap(highLevelCompress);
        }

        public AnimationClip LowCompressAnimAndPMap()
        {
            return CompressAnimAndPMap(lowLevelCompress);
        }

        public AnimationClip NoCompressAnimAndPMap()
        {
            return CompressAnimAndPMap(noCompress);
        }

        public AnimationClip CompressAnimAndPMap(AnimParamSetting map)
        {
            if (map == null)
                return null;

            AnimationClip originClip = animClip;
            if (!originClip)
                return null;

            string clipName = originClip.name;
            string originPath = AssetDatabase.GetAssetPath(originClip);
            string fileName = Path.GetFileNameWithoutExtension(originPath);
            string extension = Path.GetExtension(originPath);
            bool isClip = extension.ToLower() == ".anim";

            AvatarMask avatarMask = uniformMask;
            int retainDecimals = map.retainDecimals;
            bool resampleCurves = map.resampleCurves;
            float rotationError = map.rotationError;
            float positionError = map.positionError;
            float scaleError = map.scaleError;
            ModelImporterAnimationCompression animationCompression = map.animationCompression;

            // 当目录不存在时创建目录
            if (!Directory.Exists(storePath))
                Directory.CreateDirectory(storePath);

            AnimationClip targetClip = null;
            if (!isClip)
            {
                string targetPath = $"{storePath}/compression_{fileName}{extension}";
                try
                {
                    File.Copy(originPath, targetPath, true);
                    File.Copy($"{originPath}.meta", $"{targetPath}.meta", true);
                    AssetDatabase.Refresh();

                    ModelImporter targetImporter = AssetImporter.GetAtPath(targetPath) as ModelImporter;
                    targetImporter.resampleCurves = resampleCurves;
                    targetImporter.animationCompression = animationCompression;
                    targetImporter.animationRotationError = rotationError;
                    targetImporter.animationPositionError = positionError;
                    targetImporter.animationScaleError = scaleError;
                    targetImporter.SaveAndReimport();

                    Object[] objs = AssetDatabase.LoadAllAssetsAtPath(targetPath);
                    foreach (var obj in objs)
                    {
                        if (obj is AnimationClip clip && clip.name == clipName)
                        {
                            targetClip = new AnimationClip();
                            EditorUtility.CopySerialized(clip, targetClip);
                            break;
                        }
                    }

                    if (targetClip)
                    { 
                        //数据需要压缩
                        if (targetImporter.animationCompression != ModelImporterAnimationCompression.Off)
                        {
                            //存在遮罩需要数据混合
                            if (avatarMask)
                            {
                                string noCompressionPath = storePath + "/noCompression_" + fileName + extension;
                                try
                                {
                                    File.Copy(originPath, noCompressionPath, true);
                                    File.Copy(originPath + ".meta", noCompressionPath + ".meta", true);
                                    AssetDatabase.Refresh();
                                    ModelImporter noCompressionImporter = AssetImporter.GetAtPath(noCompressionPath) as ModelImporter;
                                    noCompressionImporter.animationCompression = ModelImporterAnimationCompression.Off;
                                    noCompressionImporter.SaveAndReimport();

                                    AnimationClip noCompressionClip = null;
                                    objs = AssetDatabase.LoadAllAssetsAtPath(noCompressionPath);
                                
                                    for (int j = 0; j < objs.Length; j++)
                                    {
                                        AnimationClip clip = objs[j] as AnimationClip;
                                        if (clip && clip.name == clipName)
                                        {
                                            noCompressionClip = new AnimationClip();
                                            EditorUtility.CopySerialized(clip, noCompressionClip);
                                            break;
                                        }
                                    }

                                    if (noCompressionClip)
                                        targetClip = CompressAnimWithMask(targetClip, noCompressionClip, avatarMask);
                                }
                                finally
                                {
                                    AssetDatabase.DeleteAsset(noCompressionPath);
                                    AssetDatabase.Refresh();
                                }
                            }
                        }
                    }


                }
                finally
                {
                    AssetDatabase.DeleteAsset(targetPath);
                    AssetDatabase.Refresh();
                }
            }

            if (!targetClip)
            {
                targetClip = new AnimationClip();
                EditorUtility.CopySerialized(originClip, targetClip);
            }

            if (removeScaleCurves && ignoreRemoveList != null)
            {
                RemoveScaleCurves(targetClip, ignoreRemoveList);
            }

            if (retainDecimals > 0)
                CompressCurves(targetClip, retainDecimals);
            
            SerializedObject serializedObject = new SerializedObject(targetClip);
            SerializedProperty editorCurves = serializedObject.FindProperty("m_EditorCurves");
            if (editorCurves != null && editorCurves.arraySize > 0)
            {
                editorCurves.arraySize = 0;
                serializedObject.ApplyModifiedProperties();
            }
            SerializedProperty eulerEditorCurves = serializedObject.FindProperty("m_EulerEditorCurves");
            if (eulerEditorCurves != null && eulerEditorCurves.arraySize > 0)
            {
                eulerEditorCurves.arraySize = 0;
                serializedObject.ApplyModifiedProperties();
            }

            return targetClip;
        }

        public static AnimationClip CompressAnimWithMask(AnimationClip clipUnityCompress, AnimationClip clipNoneCompress, AvatarMask mask)
        {
            int count = mask.transformCount;
            Dictionary<string, bool> protectPath = new Dictionary<string, bool>();
            int proCount = 0;
            for (int i = 1; i < count; i++)
            {
                if (mask.GetTransformActive(i))
                {
                    Debug.Log(proCount + "  预先保护  " + mask.GetTransformPath(i));
                    protectPath.Add(mask.GetTransformPath(i), false);
                    proCount++;
                }
            }

            EditorCurveBinding[] unity_curveBindings = AnimationUtility.GetCurveBindings(clipUnityCompress);
            AnimationClipCurveData[] unityCurves = new AnimationClipCurveData[unity_curveBindings.Length];
            for (int index = 0; index < unityCurves.Length; ++index)
            {
                unityCurves[index] = new AnimationClipCurveData(unity_curveBindings[index]);
                unityCurves[index].curve = AnimationUtility.GetEditorCurve(clipUnityCompress, unity_curveBindings[index]);
            }

            clipUnityCompress.ClearCurves();
            EditorCurveBinding[] none_curveBindings = AnimationUtility.GetCurveBindings(clipNoneCompress);
            AnimationClipCurveData[] noneCurves = new AnimationClipCurveData[none_curveBindings.Length];
            for (int index = 0; index < noneCurves.Length; ++index)
            {
                noneCurves[index] = new AnimationClipCurveData(none_curveBindings[index]);
                noneCurves[index].curve = AnimationUtility.GetEditorCurve(clipNoneCompress, none_curveBindings[index]);
            }

            int protectedIndex = 0;
            foreach (var curveData in unityCurves)
            {
                AnimationClipCurveData curve = curveData;
                if (protectPath.ContainsKey(curveData.path))
                {
                    //Debug.Log($"{protectedIndex} Protecting curve {curveData.path}");
                    curve = new AnimationClipCurveData(none_curveBindings[protectedIndex]);
                    curve.curve = AnimationUtility.GetEditorCurve(clipNoneCompress, none_curveBindings[protectedIndex]);
                    protectedIndex++;
                }
                clipUnityCompress.SetCurve(curve.path, curve.type, curve.propertyName, curve.curve);
            }

            AnimationClip newClip = new AnimationClip();
            EditorUtility.CopySerialized(clipUnityCompress, newClip);
            AnimationClipSettings unity_clipSettings = AnimationUtility.GetAnimationClipSettings(clipUnityCompress);
            AnimationClipSettings none_clipSettings = AnimationUtility.GetAnimationClipSettings(clipNoneCompress);
            if (unity_clipSettings.loopTime != none_clipSettings.loopTime)
                Debug.LogError("The loop mode of the two animations is inconsistent, please check");

            AnimationClipSettings new_clipSettings = AnimationUtility.GetAnimationClipSettings(newClip);
            new_clipSettings.loopTime = unity_clipSettings.loopTime;
            AnimationUtility.SetAnimationClipSettings(newClip, new_clipSettings);

            return newClip;
        }

        public static void RemoveScaleCurves(AnimationClip clip, List<string> ignoreRemoveList)
        {
            EditorCurveBinding[] curveDatas = AnimationUtility.GetCurveBindings(clip);
            foreach (var curve in curveDatas)
            {
                bool trim = curve.propertyName.ToLower().Contains("scale");
                if (trim)
                {
                    foreach (var ignorePattern in ignoreRemoveList)
                    {
                        if (!string.IsNullOrEmpty(ignorePattern) && Regex.IsMatch(curve.path, ignorePattern, RegexOptions.IgnoreCase))
                        {
                            trim = false;
                            break;
                        }
                    }
                }
                if (trim)
                    AnimationUtility.SetEditorCurve(clip, curve, null);
            }
        }

        public static void CompressCurves(AnimationClip clip, int retainDecimals)
        {
            Keyframe key;
            Keyframe[] keys;
            string format = "f" + retainDecimals;

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            for (int i = 0; i < bindings.Length; ++i)
            {
                EditorCurveBinding binding = bindings[i];
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.keys == null)
                {
                    Debug.LogWarningFormat("{0} has no keyframes for curve {1} in {2}", clip.name, binding.propertyName, binding.path);
                    continue;
                }
                keys = curve.keys;
                for (int j = 0; j < keys.Length; ++j)
                {
                    key = keys[j];
                    key.value = float.Parse(key.value.ToString(format));
                    key.inTangent = float.Parse(key.inTangent.ToString(format));
                    key.outTangent = float.Parse(key.outTangent.ToString(format));
                    key.inWeight = float.Parse(key.inWeight.ToString(format));
                    key.outWeight = float.Parse(key.outWeight.ToString(format));
                    keys[j] = key;
                }
                curve.keys = keys;
                clip.SetCurve(binding.path, binding.type, binding.propertyName, curve);
            }
        }

        [CustomEditor(typeof(AnimAutoCompress))]
        private class AnimAutoCompressEditor : Editor
        {
            private AnimAutoCompress tool;

            private void OnEnable()
            {
                tool = (AnimAutoCompress)target;
            }

            public override void OnInspectorGUI()
            {
                DrawDefaultInspector();
                
                AnimAutoCompress animationData = (AnimAutoCompress)target;
                
                //美化界面，加个空格
                EditorGUILayout.Space(20);
                
                //创建按钮
                if (GUILayout.Button("应用上一次压缩设置（默认高压缩选项）"))
                {
                    // 应用上一次压缩设置（默认高压缩选项）
                    ActivateButtonBasedOnClip(animationData);
                }
                
                if (GUILayout.Button("高压缩（文件占用小）"))
                {
                    ApplyCompression(tool.HighCompressAnimAndPMap());
                    MoveAnimClipToList(animationData, null, animationData.animClip);
                }

                if (GUILayout.Button("低压缩（文件占用中）"))
                {
                    ApplyCompression(tool.LowCompressAnimAndPMap());
                    MoveAnimClipToList(animationData, animationData.lowLevelCompressList, animationData.animClip);
                }

                if (GUILayout.Button("不压缩（文件占用大）"))
                {
                    ApplyCompression(tool.NoCompressAnimAndPMap());
                    MoveAnimClipToList(animationData, animationData.noCompressList, animationData.animClip);
                }
            }

            private void ApplyCompression(AnimationClip newClip)
            {
                if (newClip != null)
                {
                    string path = $"{tool.storePath}/{tool.animClip.name}.anim";
                    
                    AssetDatabase.DeleteAsset(path);
                    AssetDatabase.CreateAsset(newClip, path);
                    AssetDatabase.SaveAssets();
                    //Debug.Log($"compression applied and saved at {path}");
                }
                else 
                {
                    Debug.LogWarning($" compression failed for {tool.animClip.name}");
                }
            }
            
            private void ActivateButtonBasedOnClip(AnimAutoCompress animationData)
            {
                AnimationClip clip = animationData.animClip;

                if (clip != null)
                {
                    if (animationData.lowLevelCompressList.Contains(clip))
                    {
                        MoveAnimClipToList(animationData, animationData.lowLevelCompressList, clip);
                        ApplyCompression(tool.LowCompressAnimAndPMap());
                    }
                    else if (animationData.noCompressList.Contains(clip))
                    {
                        MoveAnimClipToList(animationData, animationData.noCompressList, clip);
                        ApplyCompression(tool.NoCompressAnimAndPMap());
                    }
                    else
                    {
                        MoveAnimClipToList(animationData, null, clip);
                        ApplyCompression(tool.HighCompressAnimAndPMap());
                    }
                }
                else
                {
                    Debug.LogWarning("No AnimationClip assigned.");
                }
            }

            private void MoveAnimClipToList(AnimAutoCompress animationData, List<AnimationClip> targetList, AnimationClip clip)
            {
                if (clip != null)
                {
                    // 在其他List中删除
                    animationData.lowLevelCompressList.Remove(clip);
                    animationData.noCompressList.Remove(clip);

                    if (targetList != null)
                    {
                        // 添加Clip在当前List
                        if (!targetList.Contains(clip))
                        {
                            targetList.Add(clip);
                            EditorUtility.SetDirty(target); // 设置dirty
                        }
                        else
                        {
                            Debug.LogWarning($"{clip.name} already exists in the target list.");
                        }
                    }
                }
                else
                {
                    Debug.LogWarning("No AnimationClip assigned.");
                }
            }
        }
    }
}
