using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace U3DUtility
{
    /// <summary>
    /// 资源工具类
    /// </summary>
    public class ResUtils
    {
        static string DATA_ROOT_PATH = string.Format("{0}/", Application.streamingAssetsPath);

        public static string BundleName
        {
            get
            {
                RuntimePlatform plat = Application.platform;

                if (plat == RuntimePlatform.WindowsEditor || plat == RuntimePlatform.WindowsPlayer)
                {
                    return "Windows";
                }
                else if (plat == RuntimePlatform.Android)
                {
                    return "Android";
                }
                else if (plat == RuntimePlatform.IPhonePlayer)
                {
                    return "IOS";
                }
                else
                {
                    return "Windows";
                }
            }
        }

        /// <summary>
        /// Bundle 文件发布后优先在PersistPath读取，如果没有则从streamingAsset中读取
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        public static string GetBundleFilePath(string fileName)
        {
            string filePath = Application.persistentDataPath + "/AssetBundles/" + fileName;
            return filePath;
        }
    }

    /// <summary>
    /// 资源加载及管理
    /// </summary>
    public class ResManager
    {
        AssetBundleManifest m_AssetBundleManifest;
        Dictionary<string, AssetBundle> m_LoadedAssetBundles = new Dictionary<string, AssetBundle>();
        static Dictionary<string, string[]> m_Dependencies = new Dictionary<string, string[]>();
        string[] m_Variants = { };

        /// <summary>
        /// 动态加载资源统一接口，如果从bundle里读取不到则从本地包中读取
        /// </summary>
        /// <param name="assetPath">资源路径，相对于Resources目录</param>
        /// <param name="type"></param>
        /// <returns></returns>
        public UnityEngine.Object LoadAsset(string assetPath, System.Type type)
        {
            assetPath = CheckAssetPath(assetPath, type);
            if (assetPath == null)
            {
                return null;
            }

            UnityEngine.Object obj = LoadAssetFromBundle(assetPath, type);
            if (obj == null)
            {
                string path = assetPath.Remove(assetPath.LastIndexOf('.'));
                return Resources.Load(path);
            }

            return null;
        }

        /// <summary>
        /// 切换场景时可以卸载所有的资源
        /// </summary>
        public void CleanAllAsset()
        {
            foreach (var v in m_LoadedAssetBundles)
            {
                v.Value.Unload(false);
            }

            m_LoadedAssetBundles.Clear();

            Resources.UnloadUnusedAssets();
        }

        string CheckAssetPath(string assetPath, System.Type type)
        {
            if (type == typeof(Material))
            {
                if (!assetPath.Contains(".mat"))
                    assetPath += ".mat";
            }
            else if (type == typeof(TextAsset))
            {
                if (!assetPath.Contains(".txt") && !assetPath.Contains(".xml"))
                    assetPath += ".txt";
            }
            else if (type == typeof(Scene))
            {
                if (!assetPath.Contains(".unity"))
                    assetPath += ".unity";
            }
            else if (type == typeof(Shader))
            {
                if (!assetPath.Contains(".shader"))
                    assetPath += ".shader";
            }
            else if (type == typeof(GameObject))
            {
                if (!assetPath.Contains(".prefab"))
                    assetPath += ".prefab";
            }
            else
            {
                Debug.LogWarning("LoadAsset: unsupport type " + type.Name);
                return null;
            }
            return assetPath;
        }

        static string DataPath
        {
            get
            {
                return Application.persistentDataPath + "/AssetBundle/";
            }
        }

        UnityEngine.Object LoadAssetFromBundle(string assetPath, System.Type type)
        {
            if (m_AssetBundleManifest == null)
            {
                AssetBundle manifestBundle = AssetBundle.LoadFromFile(DataPath + ResUtils.BundleName);
                m_AssetBundleManifest = manifestBundle.LoadAsset("AssetBundleManifest", typeof(AssetBundleManifest)) as AssetBundleManifest;
            }

            string assetName = assetPath.Substring(assetPath.LastIndexOf("/") + 1).ToLower();
            string bundleName = assetName + ".unity3d";

            AssetBundle bundleInfo = null;
            if (m_LoadedAssetBundles.TryGetValue(bundleName, out bundleInfo))
            {
                if (!bundleInfo.isStreamedSceneAssetBundle)
                {
                    UnityEngine.Object obj = bundleInfo.LoadAsset(assetName, type);
                    return obj;
                }
                else
                {
                    return null;
                }
            }
            else
            {
                AssetBundle bundle = LoadAssetBundle(bundleName);
                if (!bundle.isStreamedSceneAssetBundle)
                {
                    UnityEngine.Object obj = bundle.LoadAsset(assetName, type);
                    return obj;
                }
                return null;
            }
        }

        string RemapVariantName(string assetBundleName)
        {
            string[] bundlesWithVariant = m_AssetBundleManifest.GetAllAssetBundlesWithVariant();
            if (System.Array.IndexOf(bundlesWithVariant, assetBundleName) < 0)
            {
                return assetBundleName;
            }

            string[] split = assetBundleName.Split('.');

            int bestFit = int.MaxValue;
            int bestFitIndex = -1;
            // Loop all the assetBundles with variant to find the best fit variant assetBundle.
            for (int i = 0; i < bundlesWithVariant.Length; i++)
            {
                string[] curSplit = bundlesWithVariant[i].Split('.');
                if (curSplit[0] != split[0])
                    continue;

                int found = System.Array.IndexOf(m_Variants, curSplit[1]);
                if (found != -1 && found < bestFit)
                {
                    bestFit = found;
                    bestFitIndex = i;
                }
            }
            if (bestFitIndex != -1)
            {
                return bundlesWithVariant[bestFitIndex];
            }
            else
            {
                return assetBundleName;
            }
        }

        public AssetBundle LoadAssetBundle(string assetBundleName)
        {
            assetBundleName = RemapVariantName(assetBundleName);

            AssetBundle bundle = null;
            if (m_LoadedAssetBundles.TryGetValue(assetBundleName, out bundle))
            {
                return bundle;
            }

            LoadDependencies(assetBundleName);
            bundle = LoadAssetBundleSingle(assetBundleName);
            return bundle;
        }

        AssetBundle LoadAssetBundleSingle(string assetBundleName)
        {
            AssetBundle bundleInfo = null;
            if (m_LoadedAssetBundles.TryGetValue(assetBundleName, out bundleInfo))
            {
                return bundleInfo;
            }

            string uri = DataPath + assetBundleName;
            AssetBundle bundle = AssetBundle.LoadFromFile(uri);
            m_LoadedAssetBundles.Add(assetBundleName, bundleInfo);
            return bundleInfo;
        }

        void LoadDependencies(string assetBundleName)
        {
            string[] dependencies = m_AssetBundleManifest.GetAllDependencies(assetBundleName);
            if (dependencies.Length == 0)
            {
                return;
            }

            for (int i = 0, n = dependencies.Length; i < n; i++)
            {
                dependencies[i] = RemapVariantName(dependencies[i]);
            }
            m_Dependencies.Add(assetBundleName, dependencies);

            for (int i = 0, n = dependencies.Length; i < n; i++)
            {
                LoadAssetBundleSingle(dependencies[i]);
            }
        }
    }
}
