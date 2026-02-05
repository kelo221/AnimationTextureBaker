#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Kelo.AnimationTextureBaker;

namespace Kelo.AnimationTextureBaker.Editor
{
    [CustomEditor(typeof(AnimationBaker))]
    public class AnimationBakerEditor : UnityEditor.Editor
    {
        private Texture2D[] bakedTexturesPos;
        private Texture2D[] bakedTexturesNorm;
        private Texture2D[] bakedTexturesTan;
        private Texture2D[] bakedTexturesVel;
        
        // Bone data collected per-animation: [animIndex][frame * boneCount + boneIndex]
        private List<List<AnimationCombinedFrames.BoneFrameData>> bakedBoneData;
        
        // Temporary controller path for masked animation baking
        private string tempControllerPath;
        private AnimatorController tempController;


        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            var script = (AnimationBaker)target;

            EditorGUILayout.LabelField("SOLID Configuration", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("profile"));
            EditorGUILayout.Space(5);

            var settings = script.settings;
            var settingsProp = serializedObject.FindProperty("settings");
            
            // Draw non-array properties normally
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("infoTexGen"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("playShader"));
            
            // Clips section with table headers
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Animation Clips", EditorStyles.boldLabel);
            
            var clipsProp = settingsProp.FindPropertyRelative("clips");
            
            // Draw table headers
            EditorGUILayout.BeginHorizontal();
            float totalWidth = EditorGUIUtility.currentViewWidth - 60f; // Account for foldout and buttons
            float primaryWidth = totalWidth * 0.45f;
            float secondaryWidth = totalWidth * 0.35f;
            float maskWidth = totalWidth * 0.2f;
            
            GUILayout.Space(15); // Indent for foldout
            EditorGUILayout.LabelField("Primary Clip", EditorStyles.miniLabel, GUILayout.Width(primaryWidth));
            EditorGUILayout.LabelField("Secondary (Optional)", EditorStyles.miniLabel, GUILayout.Width(secondaryWidth));
            EditorGUILayout.LabelField("Mask", EditorStyles.miniLabel, GUILayout.Width(maskWidth));
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.PropertyField(clipsProp, new GUIContent("Clips"), true);
            
            // Draw remaining properties
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("frameRate"));
            
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Performance", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("useBurstBaking"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("texturePrecision"));
            
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("saveToFolder"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("createPrefabs"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("createMeshAsset"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("optimizeMeshOnSave"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("collapseMesh"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("combineTextures"));
            
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Motion Blur", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("bakeVelocity"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("exposure"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("blurSamples"));
            
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("rotate"));
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("boundsScale"));
            
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Bone Attachments", EditorStyles.boldLabel);
            if (GUILayout.Button("Scan Bones", GUILayout.Width(100)))
            {
                script.ScanBones();
                EditorUtility.SetDirty(script);
            }
            if (GUILayout.Button("Save Bones", GUILayout.Width(100)))
            {
                script.SaveBones();
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("animatedBones"), true);
            
            EditorGUILayout.PropertyField(settingsProp.FindPropertyRelative("keywords"), true);
            
            serializedObject.ApplyModifiedProperties();

            GUILayout.Space(10);

            if (settings.clips == null || settings.clips.Length < 1)
            {
                if (GUILayout.Button("Scan"))
                {
                    script.Scan();
                    EditorUtility.SetDirty(script);
                }
                return;
            }

            string stats = "Calculated frame counts per clip: \n";
            foreach (var clipEntry in settings.clips)
            {
                if (clipEntry == null || !clipEntry.IsValid) continue;
                int frames = BakerEngine.GetFrameCount(clipEntry.primary, settings.frameRate);
                stats += $"{clipEntry.Name}: {frames}";
                if (clipEntry.HasMaskedLayer)
                    stats += " [+Masked Layer]";
                stats += "\n";
            }



            EditorGUILayout.HelpBox(stats, MessageType.Info);
            
            if (settings.bakeVelocity == false && settings.playShader != null && settings.playShader.name.Contains("MotionVectors"))
            {
                EditorGUILayout.HelpBox("Velocity baking is disabled. The 'VAT_MotionVectors' shader requires Velocity textures for Motion Blur and Velocity Smear effects.", MessageType.Warning);
            }

            if (GUILayout.Button("Bake Textures"))
            {
                Bake(script);
            }
            
            serializedObject.ApplyModifiedProperties();
        }

        private void Bake(AnimationBaker script)
        {
            var settings = script.settings;
            bool bakeCombined = settings.combineTextures && settings.clips.Length > 1;

            var skin = script.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skin == null) { Debug.LogError("No SkinnedMeshRenderer found!"); return; }

            var defaultMesh = skin.sharedMesh;
            int vCount = defaultMesh.vertexCount;
            int texWidth = Mathf.NextPowerOfTwo(vCount);
            Mesh tempMesh = new Mesh();
            Mesh tempMeshVel = new Mesh();


            string folderName = AssetDatabaseService.FixPath(settings.saveToFolder);
            string folderPath = AssetDatabaseService.EnsureFolder("Assets", folderName);
            string subFolder = AssetDatabaseService.FixPath(script.name);
            string subFolderPath = AssetDatabaseService.EnsureFolder(folderPath, subFolder);

            if (settings.createMeshAsset)
            {
                string meshAssetPath = Path.Combine(subFolderPath, $"{AssetDatabaseService.FixPath(script.name)}.mesh.asset");
                defaultMesh = Instantiate(defaultMesh);
                
                if (settings.collapseMesh && defaultMesh.vertexCount > 2)
                {
                    Vector3 min = Vector3.one * float.PositiveInfinity;
                    Vector3 max = Vector3.one * float.NegativeInfinity;
                    foreach (var v in defaultMesh.vertices)
                    {
                        min = Vector3.Min(v, min);
                        max = Vector3.Max(v, max);
                    }
                    var newVerts = new Vector3[defaultMesh.vertexCount];
                    newVerts[0] = min;
                    newVerts[1] = max;
                    defaultMesh.SetVertices(newVerts);
                    defaultMesh.RecalculateBounds();
                }
                else if (settings.optimizeMeshOnSave)
                {
                    MeshUtility.Optimize(defaultMesh);
                }

                defaultMesh.bounds = new Bounds(defaultMesh.bounds.center, defaultMesh.bounds.size * settings.boundsScale);
                AssetDatabaseService.CreateAsset(defaultMesh, meshAssetPath);
                AssetDatabaseService.SaveAndRefresh();
                defaultMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
            }

            if (bakeCombined)
            {
                bakedTexturesPos = new Texture2D[settings.clips.Length];
                bakedTexturesNorm = new Texture2D[settings.clips.Length];
                bakedTexturesTan = new Texture2D[settings.clips.Length];
                bakedTexturesVel = new Texture2D[settings.clips.Length];
                
                // Initialize bone data collection
                bakedBoneData = new List<List<AnimationCombinedFrames.BoneFrameData>>();
            }

            // Store original animator controller to restore after baking
            var animator = script.GetComponent<Animator>();
            RuntimeAnimatorController originalController = animator != null ? animator.runtimeAnimatorController : null;
            
            for (int i = 0; i < settings.clips.Length; i++)
            {
                var clipEntry = settings.clips[i];
                if (clipEntry == null || !clipEntry.IsValid) continue;
                
                // Create temp controller for masked animations
                if (clipEntry.HasMaskedLayer)
                {
                    CreateTempMaskedController(clipEntry, subFolderPath);
                    if (animator != null)
                    {
                        animator.runtimeAnimatorController = tempController;
                    }
                }

                var clip = clipEntry.primary;
                int frames = BakerEngine.GetFrameCount(clip, settings.frameRate);

                float dt = clip.length / frames;
                var infoList = new List<BakerEngine.VertInfo>();
                
                // Per-animation bone data list
                var animBoneData = bakeCombined && settings.animatedBones != null && settings.animatedBones.Length > 0
                    ? new List<AnimationCombinedFrames.BoneFrameData>()
                    : null;

                var pRt = new RenderTexture(texWidth, frames, 0, RenderTextureFormat.ARGBHalf) { name = $"{script.name}.Pos.{clip.name}.{frames}F" };
                var nRt = new RenderTexture(texWidth, frames, 0, RenderTextureFormat.ARGBHalf) { name = $"{script.name}.Nml.{clip.name}.{frames}F" };
                var tRt = new RenderTexture(texWidth, frames, 0, RenderTextureFormat.ARGBHalf) { name = $"{script.name}.Tan.{clip.name}.{frames}F" };
                var vRt = new RenderTexture(texWidth, frames, 0, RenderTextureFormat.ARGBHalf) { name = $"{script.name}.Vel.{clip.name}.{frames}F" };

                foreach (var rt in new[] { pRt, nRt, tRt, vRt })
                {
                    rt.enableRandomWrite = true;
                    rt.Create();
                    RenderTexture.active = rt;
                    GL.Clear(true, true, Color.clear);
                }


                for (int f = 0; f < frames; f++)
                {
                    float t = dt * f;
                    int samples = Mathf.Max(1, settings.blurSamples);
                    
                    // Sample animation at this frame for bone capture
                    SampleAnimation(script.gameObject, clipEntry, t, animator);
                    
                    // Capture bone transforms at this frame (use first sample only)
                    if (animBoneData != null)
                    {
                        Quaternion bakeRotation = Quaternion.Euler(settings.rotate);
                        foreach (var boneEntry in settings.animatedBones)
                        {
                            if (boneEntry != null && boneEntry.IsValid)
                            {
                                Vector3 localPos;
                                Quaternion localRot;
                                
                                if (boneEntry.IsLimbPair)
                                {
                                    // Limb pair: compute midpoint and axis-aligned rotation
                                    Vector3 posA = boneEntry.boneA.position;
                                    Vector3 posB = boneEntry.boneB.position;
                                    
                                    // Midpoint in root-local space
                                    Vector3 midpointWorld = (posA + posB) * 0.5f;
                                    localPos = script.transform.InverseTransformPoint(midpointWorld);
                                    
                                    // Calculate rotation to align capsule axis (Y-up) with limb direction
                                    Vector3 limbDirection = posB - posA;
                                    if (limbDirection.sqrMagnitude > 0.0001f)
                                    {
                                        Quaternion lookRot = Quaternion.LookRotation(limbDirection.normalized);
                                        Quaternion limbRotationWorld = lookRot * Quaternion.Euler(90f, 0f, 0f);
                                        localRot = Quaternion.Inverse(script.transform.rotation) * limbRotationWorld;
                                    }
                                    else
                                    {
                                        localRot = Quaternion.identity;
                                    }
                                }
                                else
                                {
                                    // Single bone: use root-relative position and rotation
                                    localPos = script.transform.InverseTransformPoint(boneEntry.boneA.position);
                                    localRot = Quaternion.Inverse(script.transform.rotation) * boneEntry.boneA.rotation;
                                }
                                
                                // Apply additional bake rotation
                                localPos = bakeRotation * localPos;
                                localRot = bakeRotation * localRot;
                                
                                animBoneData.Add(new AnimationCombinedFrames.BoneFrameData
                                {
                                    position = localPos,
                                    rotation = localRot
                                });
                            }
                            else
                            {
                                animBoneData.Add(default);
                            }
                        }
                    }

                    if (settings.useBurstBaking)
                    {
                        // Burst-accelerated path using NativeArrays and Jobs
                        using var accPos = new NativeArray<float3>(vCount, Allocator.TempJob);
                        using var accNrm = new NativeArray<float3>(vCount, Allocator.TempJob);
                        using var accTan = new NativeArray<float4>(vCount, Allocator.TempJob);
                        var velArray = new NativeArray<float3>(vCount, Allocator.TempJob); // No 'using' - we need to modify elements
                        using var output = new NativeArray<BurstBakerEngine.VertInfoNative>(vCount, Allocator.TempJob);

                        for (int s = 0; s < samples; s++)
                        {
                            float sampleTime = t + (samples > 1 ? (s / (float)(samples - 1)) * settings.exposure : 0);
                            SampleAnimation(script.gameObject, clipEntry, sampleTime, animator);
                            skin.BakeMesh(tempMesh);

                            var meshDataArray = Mesh.AcquireReadOnlyMeshData(tempMesh);
                            var meshData = meshDataArray[0];
                            
                            using var srcPos = new NativeArray<float3>(vCount, Allocator.TempJob);
                            using var srcNrm = new NativeArray<float3>(vCount, Allocator.TempJob);
                            using var srcTan = new NativeArray<float4>(vCount, Allocator.TempJob);
                            
                            meshData.GetVertices(srcPos.Reinterpret<Vector3>());
                            meshData.GetNormals(srcNrm.Reinterpret<Vector3>());
                            meshData.GetTangents(srcTan.Reinterpret<Vector4>());
                            meshDataArray.Dispose();

                            var accJob = new BurstBakerEngine.AccumulateVertexSamplesJob
                            {
                                SourcePositions = srcPos,
                                SourceNormals = srcNrm,
                                SourceTangents = srcTan,
                                AccumulatedPositions = accPos,
                                AccumulatedNormals = accNrm,
                                AccumulatedTangents = accTan
                            };
                            accJob.Schedule(vCount, 64).Complete();

                            if (s == 0 && settings.bakeVelocity)
                            {
                                SampleAnimation(script.gameObject, clipEntry, t + settings.exposure, animator);
                                skin.BakeMesh(tempMeshVel);
                                var velMeshData = Mesh.AcquireReadOnlyMeshData(tempMeshVel);
                                using var nextPos = new NativeArray<float3>(vCount, Allocator.TempJob);
                                velMeshData[0].GetVertices(nextPos.Reinterpret<Vector3>());
                                velMeshData.Dispose();
                                for (int vi = 0; vi < vCount; vi++)
                                    velArray[vi] = nextPos[vi] - srcPos[vi];
                            }
                        }

                        var avgJob = new BurstBakerEngine.AverageVertexSamplesJob
                        {
                            AccumulatedPositions = accPos,
                            AccumulatedNormals = accNrm,
                            AccumulatedTangents = accTan,
                            Velocities = velArray,
                            SampleCount = samples,
                            RotateEuler = float3.zero, // Rotation handled by compute shader
                            Output = output
                        };
                        avgJob.Schedule(vCount, 64).Complete();

                        foreach (var info in output)
                        {
                            infoList.Add(new BakerEngine.VertInfo
                            {
                                position = info.position,
                                normal = info.normal,
                                tangent = info.tangent,
                                velocity = info.velocity
                            });
                        }
                        
                        velArray.Dispose(); // Manual dispose since we couldn't use 'using'
                    }

                    else
                    {
                        // Original managed path
                        Vector3[] accumulatedV = new Vector3[vCount];
                        Vector3[] accumulatedN = new Vector3[vCount];
                        Vector4[] accumulatedT = new Vector4[vCount];
                        Vector3[] vel = new Vector3[vCount];

                        for (int s = 0; s < samples; s++)
                        {
                            float sampleTime = t + (samples > 1 ? (s / (float)(samples - 1)) * settings.exposure : 0);
                            SampleAnimation(script.gameObject, clipEntry, sampleTime, animator);
                            skin.BakeMesh(tempMesh);

                            var v = tempMesh.vertices;
                            var n = tempMesh.normals;
                            var tan = tempMesh.tangents;

                            for (int vi = 0; vi < vCount; vi++)
                            {
                                accumulatedV[vi] += v[vi];
                                accumulatedN[vi] += n[vi];
                                accumulatedT[vi] += tan[vi];
                            }

                            if (s == 0 && settings.bakeVelocity)
                            {
                                SampleAnimation(script.gameObject, clipEntry, t + settings.exposure, animator);
                                skin.BakeMesh(tempMeshVel);
                                var vNext = tempMeshVel.vertices;
                                for (int vi = 0; vi < vCount; vi++)
                                    vel[vi] = (vNext[vi] - v[vi]);
                            }
                        }

                        for (int vi = 0; vi < vCount; vi++)
                        {
                            infoList.Add(new BakerEngine.VertInfo
                            {
                                position = accumulatedV[vi] / samples,
                                normal = (accumulatedN[vi] / samples).normalized,
                                tangent = (accumulatedT[vi] / samples).normalized,
                                velocity = vel[vi]
                            });
                        }
                    }
                }




                var buffer = new ComputeBuffer(infoList.Count, System.Runtime.InteropServices.Marshal.SizeOf(typeof(BakerEngine.VertInfo)));
                buffer.SetData(infoList.ToArray());

                int kernel = settings.infoTexGen.FindKernel("CSMain");
                settings.infoTexGen.GetKernelThreadGroupSizes(kernel, out uint tx, out uint ty, out uint tz);
                settings.infoTexGen.SetInt("VertCount", vCount);
                settings.infoTexGen.SetBuffer(kernel, "Info", buffer);
                settings.infoTexGen.SetTexture(kernel, "OutPosition", pRt);
                settings.infoTexGen.SetTexture(kernel, "OutNormal", nRt);
                settings.infoTexGen.SetTexture(kernel, "OutTangent", tRt);
                settings.infoTexGen.SetTexture(kernel, "OutVelocity", vRt);
                settings.infoTexGen.SetVector("RotateEuler", settings.rotate);
                settings.infoTexGen.Dispatch(kernel, vCount / (int)tx + 1, frames / (int)ty + 1, 1);
                buffer.Release();

                var posTex = BakerEngine.ConvertRTToTexture2D(pRt);
                var normTex = BakerEngine.ConvertRTToTexture2D(nRt);
                var tanTex = BakerEngine.ConvertRTToTexture2D(tRt);
                var velTex = BakerEngine.ConvertRTToTexture2D(vRt);

                if (bakeCombined)
                {
                    bakedTexturesPos[i] = posTex;
                    bakedTexturesNorm[i] = normTex;
                    bakedTexturesTan[i] = tanTex;
                    bakedTexturesVel[i] = velTex;
                    
                    // Store bone data for this animation
                    if (animBoneData != null)
                    {
                        bakedBoneData.Add(animBoneData);
                    }
                }
                else
                {
                    SaveBakedAssets(script, clipEntry.primary, frames, posTex, normTex, tanTex, velTex, subFolderPath, defaultMesh, folderPath);
                }
                
                DestroyImmediate(pRt);
                DestroyImmediate(nRt);
                DestroyImmediate(tRt);
                DestroyImmediate(vRt);
                
                // Cleanup temp controller after processing this clip
                if (clipEntry.HasMaskedLayer)
                {
                    CleanupTempController();
                }
            }
            
            // Restore original animator controller
            if (animator != null)
            {
                animator.runtimeAnimatorController = originalController;
            }


            if (bakeCombined)
            {
                FinalizeCombinedBake(script, subFolderPath, defaultMesh, folderPath);
            }

            AssetDatabaseService.SaveAndRefresh();
        }

        private void SaveBakedAssets(AnimationBaker script, AnimationClip clip, int frames, Texture2D pos, Texture2D norm, Texture2D tan, Texture2D vel, string subFolderPath, Mesh mesh, string folderPath)
        {
            var settings = script.settings;
            var mat = new Material(settings.playShader);
            var skin = script.GetComponentInChildren<SkinnedMeshRenderer>();
            
            mat.SetTexture(settings.keywords.mainTexName, skin.sharedMaterial.mainTexture);
            mat.SetTexture(settings.keywords.posTexName, pos);
            mat.SetTexture(settings.keywords.normTexName, norm);
            mat.SetTexture(settings.keywords.tanTexName, tan);
            if (settings.bakeVelocity)
                mat.SetTexture(settings.keywords.velTexName, vel);

            string safeName = AssetDatabaseService.FixPath(script.name);
            string safeClipName = AssetDatabaseService.FixPath(clip.name);
            
            AssetDatabase.CreateAsset(pos, Path.Combine(subFolderPath, $"{safeName}.Pos.{safeClipName}.asset"));
            AssetDatabase.CreateAsset(norm, Path.Combine(subFolderPath, $"{safeName}.Nml.{safeClipName}.asset"));
            AssetDatabase.CreateAsset(tan, Path.Combine(subFolderPath, $"{safeName}.Tan.{safeClipName}.asset"));
            if (settings.bakeVelocity)
                AssetDatabase.CreateAsset(vel, Path.Combine(subFolderPath, $"{safeName}.Vel.{safeClipName}.asset"));
            
            AssetDatabase.CreateAsset(mat, Path.Combine(subFolderPath, $"{safeName}.mat.{safeClipName}.{frames}F.mat"));


            if (settings.createPrefabs)
            {
                CreatePrefab(script.name + "." + clip.name, mat, mesh, script.transform.position, folderPath);
            }
        }

        private void FinalizeCombinedBake(AnimationBaker script, string subFolderPath, Mesh mesh, string folderPath)
        {
            var settings = script.settings;
            string safeName = AssetDatabaseService.FixPath(script.name);
            
            var combinedP = TextureCombiner.Combine(bakedTexturesPos, "Pos", safeName, subFolderPath);
            var combinedN = TextureCombiner.Combine(bakedTexturesNorm, "Nor", safeName, subFolderPath);
            var combinedT = TextureCombiner.Combine(bakedTexturesTan, "Tan", safeName, subFolderPath);
            Texture2D combinedV = null;
            if (settings.bakeVelocity)
                combinedV = TextureCombiner.Combine(bakedTexturesVel, "Vel", safeName, subFolderPath);


            var frameData = ScriptableObject.CreateInstance<AnimationCombinedFrames>();
            frameData.data = new AnimationCombinedFrames.FrameTimings[settings.clips.Length];
            int frameOffset = 0;

            for (int i = 0; i < settings.clips.Length; i++)
            {
                var clipEntry = settings.clips[i];
                if (clipEntry == null || !clipEntry.IsValid) continue;
                int frames = BakerEngine.GetFrameCount(clipEntry.primary, settings.frameRate);

                frameData.data[i] = new AnimationCombinedFrames.FrameTimings
                {
                    name = AssetDatabaseService.FixPath(clipEntry.Name),
                    duration = clipEntry.Duration,
                    frames = frames,
                    offset = frameOffset
                };
                frameOffset += frames + 1;
            }
            
            // Store bone data if we have animated bones
            if (settings.animatedBones != null && settings.animatedBones.Length > 0 && bakedBoneData != null)
            {
                frameData.boneCount = settings.animatedBones.Length;
                frameData.boneNames = new string[settings.animatedBones.Length];
                frameData.isLimbPair = new bool[settings.animatedBones.Length];
                for (int b = 0; b < settings.animatedBones.Length; b++)
                {
                    var entry = settings.animatedBones[b];
                    if (entry != null && entry.IsValid)
                    {
                        frameData.boneNames[b] = entry.IsLimbPair 
                            ? $"{entry.boneA.name} <-> {entry.boneB.name}" 
                            : entry.boneA.name;
                        frameData.isLimbPair[b] = entry.IsLimbPair;
                    }
                    else
                    {
                        frameData.boneNames[b] = "";
                        frameData.isLimbPair[b] = false;
                    }
                }
                
                // Flatten all animation bone data into a single array
                var allBoneFrames = new List<AnimationCombinedFrames.BoneFrameData>();
                int totalFrames = 0;
                foreach (var animData in bakedBoneData)
                {
                    allBoneFrames.AddRange(animData);
                    totalFrames += animData.Count / settings.animatedBones.Length;
                }
                frameData.boneFrames = allBoneFrames.ToArray();
                frameData.totalFrameCount = totalFrames;
            }

            AssetDatabase.CreateAsset(frameData, Path.Combine(subFolderPath, $"{safeName}.framedata.asset"));

            var mat = new Material(settings.playShader);
            var skin = script.GetComponentInChildren<SkinnedMeshRenderer>();
            mat.SetTexture(settings.keywords.mainTexName, skin.sharedMaterial.mainTexture);
            mat.SetTexture(settings.keywords.posTexName, combinedP);
            mat.SetTexture(settings.keywords.normTexName, combinedN);
            mat.SetTexture(settings.keywords.tanTexName, combinedT);
            if (settings.bakeVelocity)
                mat.SetTexture(settings.keywords.velTexName, combinedV);


            AssetDatabase.CreateAsset(mat, Path.Combine(subFolderPath, $"{safeName}.combined.mat"));

            if (settings.createPrefabs)
            {
                var go = CreatePrefab(script.name + ".combined", mat, mesh, script.transform.position, folderPath);
                go.AddComponent<AnimationFramePlayer>().frameData = frameData;
                PrefabUtility.SaveAsPrefabAsset(go, Path.Combine(folderPath, AssetDatabaseService.FixPath(go.name) + ".prefab"));
            }
        }

        private GameObject CreatePrefab(string name, Material mat, Mesh mesh, Vector3 pos, string folderPath)
        {
            var go = new GameObject(name);
            go.transform.position = pos + Vector3.left;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            string path = Path.Combine(folderPath, AssetDatabaseService.FixPath(name) + ".prefab");
            return PrefabUtility.SaveAsPrefabAssetAndConnect(go, path, InteractionMode.AutomatedAction);
        }
        
        private AnimatorController CreateTempMaskedController(ClipEntry clipEntry, string folderPath)
        {
            // Create controller and persist to disk (Unity requires serialized state machines for proper layer blending)
            tempControllerPath = Path.Combine(folderPath, "_TempMaskedController.controller");
            
            // Delete existing temp controller if present
            if (File.Exists(tempControllerPath))
            {
                AssetDatabase.DeleteAsset(tempControllerPath);
            }
            
            var controller = AnimatorController.CreateAnimatorControllerAtPath(tempControllerPath);
            controller.name = "_TempMaskedController";
            
            // Remove default layer created by CreateAnimatorControllerAtPath
            while (controller.layers.Length > 0)
            {
                controller.RemoveLayer(0);
            }
            
            // Base Layer - primary animation
            var baseStateMachine = new AnimatorStateMachine
            {
                name = "BaseStateMachine",
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(baseStateMachine, controller);
            
            var baseLayer = new AnimatorControllerLayer
            {
                name = "Base",
                defaultWeight = 1f,
                stateMachine = baseStateMachine
            };
            controller.AddLayer(baseLayer);
            
            var baseState = baseStateMachine.AddState(clipEntry.primary.name);
            baseState.motion = clipEntry.primary;
            baseState.speed = 1f;
            baseStateMachine.defaultState = baseState;

            // Masked Layer - secondary animation with avatar mask override
            var maskedStateMachine = new AnimatorStateMachine
            {
                name = "MaskedStateMachine",
                hideFlags = HideFlags.HideInHierarchy
            };
            AssetDatabase.AddObjectToAsset(maskedStateMachine, controller);
            
            var maskedLayer = new AnimatorControllerLayer
            {
                name = "Masked",
                defaultWeight = 1f,
                avatarMask = clipEntry.mask,
                blendingMode = AnimatorLayerBlendingMode.Override,
                stateMachine = maskedStateMachine
            };
            controller.AddLayer(maskedLayer);
            
            var maskedState = maskedStateMachine.AddState(clipEntry.secondary.name + "_Masked");
            maskedState.motion = clipEntry.secondary;
            maskedState.speed = 1f;
            maskedStateMachine.defaultState = maskedState;

            // Save the controller asset - critical for layer blending to work
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            
            tempController = controller;
            return controller;
        }
        
        private void CleanupTempController()
        {
            if (!string.IsNullOrEmpty(tempControllerPath) && File.Exists(tempControllerPath))
            {
                AssetDatabase.DeleteAsset(tempControllerPath);
                tempControllerPath = null;
                tempController = null;
            }
        }

        private void SampleAnimation(GameObject target, ClipEntry clipEntry, float time, Animator animator)
        {
            if (clipEntry.HasMaskedLayer)
            {
                // Use Animator layer blending with persisted controller (like Turbo Animator)
                if (animator == null)
                {
                    Debug.LogError("Animator component required for masked animation blending!");
                    clipEntry.primary.SampleAnimation(target, time);
                    return;
                }
                
                // Ensure temp controller is assigned
                if (tempController != null && animator.runtimeAnimatorController != tempController)
                {
                    animator.runtimeAnimatorController = tempController;
                }
                
                // Calculate normalized times for both clips
                float primaryNormalized = time / clipEntry.primary.length;
                float secondaryNormalized = (time / clipEntry.primary.length) * (clipEntry.primary.length / clipEntry.secondary.length);
                secondaryNormalized = Mathf.Clamp01(secondaryNormalized);
                
                // Play both animations on their respective layers
                animator.Play(clipEntry.primary.name, 0, primaryNormalized);
                animator.Play(clipEntry.secondary.name + "_Masked", 1, secondaryNormalized);
                
                // Explicitly set layer weight (critical for blending)
                animator.SetLayerWeight(1, 1f);
                
                // Force evaluation
                animator.Update(0f);
            }
            else if (animator != null && animator.runtimeAnimatorController != null)
            {
                // Single animation without mask - use Animator for consistency
                float normalizedTime = time / clipEntry.primary.length;
                animator.Play(clipEntry.primary.name, 0, normalizedTime);
                animator.Update(0f);
            }
            else
            {
                // Fallback for objects without animator
                clipEntry.primary.SampleAnimation(target, time);
            }
        }
        
        // Manual blending methods removed - now using Animator layer blending with persisted controllers
    }
}
#endif
