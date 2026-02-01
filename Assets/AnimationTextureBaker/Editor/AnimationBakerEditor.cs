#if UNITY_EDITOR
using UnityEditor;
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


        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            
            var script = (AnimationBaker)target;
            var settings = script.settings;

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
            foreach (var clip in settings.clips)
            {
                if (clip == null) continue;
                int frames = BakerEngine.GetFrameCount(clip, settings.frameRate);
                stats += $"{clip.name}: {frames}\n";
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
            }


            for (int i = 0; i < settings.clips.Length; i++)
            {
                var clip = settings.clips[i];
                if (clip == null) continue;
                int frames = BakerEngine.GetFrameCount(clip, settings.frameRate);


                float dt = clip.length / frames;
                var infoList = new List<BakerEngine.VertInfo>();

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
                            clip.SampleAnimation(script.gameObject, sampleTime);
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
                                clip.SampleAnimation(script.gameObject, t + settings.exposure);
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
                            clip.SampleAnimation(script.gameObject, sampleTime);
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
                                clip.SampleAnimation(script.gameObject, t + settings.exposure);
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
                }
                else
                {
                    SaveBakedAssets(script, clip, frames, posTex, normTex, tanTex, velTex, subFolderPath, defaultMesh, folderPath);
                }
                
                DestroyImmediate(pRt);
                DestroyImmediate(nRt);
                DestroyImmediate(tRt);
                DestroyImmediate(vRt);
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
                var clip = settings.clips[i];
                if (clip == null) continue;
                int frames = BakerEngine.GetFrameCount(clip, settings.frameRate);


                frameData.data[i] = new AnimationCombinedFrames.FrameTimings
                {
                    name = AssetDatabaseService.FixPath(clip.name),
                    duration = clip.length,
                    frames = frames,
                    offset = frameOffset
                };
                frameOffset += frames + 1;
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
    }
}
#endif
