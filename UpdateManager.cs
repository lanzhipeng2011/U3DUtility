using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using System.IO;

namespace U3DUtility
{
    public class UpdateManager : MonoBehaviour
    {
        private sealed class AssetBundleInfo
        {
            public readonly AssetBundle m_AssetBundle;
            public int m_ReferencedCount;

            public AssetBundleInfo(AssetBundle assetBundle)
            {
                m_AssetBundle = assetBundle;
                m_ReferencedCount = 1;
            }
        }

        public delegate void ProcessCompleteEvent();

        [SerializeField] string m_HttpAddress = "http://169.46.139.57:82/AssetBundles/";
        [SerializeField] string m_IndexFileName = "list.txt";

        static UpdateManager m_Singleton;
        List<BundleItem> m_DownloadingList = new List<BundleItem>();
        int m_TotalDownloadBytes = 0;
        int m_CurrentDownloadIdx = 0;
        int m_AlreadyDownloadBytes = 0;
        float m_TotalProgess = 0;
        WWW m_www = null;
        string m_NewIndexContent;
        Dictionary<string, byte[]> m_LuaTables = new Dictionary<string, byte[]>();

        public static UpdateManager Singleton
        {
            get
            {
                if (m_Singleton == null)
                {
                    GameObject o = new GameObject("Update Manager");
                    m_Singleton = o.AddComponent<UpdateManager>();
                }
                return m_Singleton;
            }
        }

        public float DownloadingProgress
        {
            get
            {
                int currentBytes = 0;
                if (m_www != null && m_CurrentDownloadIdx < m_DownloadingList.Count)
                {
                    currentBytes = (int)(m_DownloadingList[m_CurrentDownloadIdx].m_FileSize * m_www.progress);
                }

                if (m_TotalDownloadBytes > 0)
                {
                    return (float)(m_AlreadyDownloadBytes + currentBytes) / (float)m_TotalDownloadBytes;
                }
                    
                return 0;
            }
        }

        public float TotalProgress
        {
            get { return m_TotalProgess;  }
        }

        public void StartUpdate()
        {
            Debug.Log("start update resource...");

            m_TotalProgess = 0;

            StartCoroutine(AsyncCheckDownloadingList(OnCompleteCheckDownloadList));
        }

        void OnCompleteCheckDownloadList()
        {
            m_TotalProgess = 0.1f;

            StartCoroutine(AsyncDownloading(OnCompleteDownloading));
        }

        void OnCompleteDownloading()
        {
            m_TotalProgess = 0.9f;

            StartCoroutine(AsyncLoadLua(OnCompleteLoadLua));
        }

        void OnCompleteLoadLua()
        {
            m_TotalProgess = 1;

            Debug.Log("update resource complete...");
        }

        //从服务器得到资源列表并对比出需要更新的包列表
        IEnumerator AsyncCheckDownloadingList(ProcessCompleteEvent ev)
        {
            if (Application.isMobilePlatform)
            {
                //读取本地的idx和apk里的idx文件
                Dictionary<string, BundleItem> localBundlesDict = new Dictionary<string, BundleItem>();
                Dictionary<string, BundleItem> apkBundlesDict = new Dictionary<string, BundleItem>();
                string persistPath = Application.persistentDataPath + "/AssetBundles/";
                string localIndexPath = persistPath + m_IndexFileName;

                if (File.Exists(localIndexPath))
                {
                    string indexContent = File.ReadAllText(localIndexPath);
                    if (indexContent != null)
                    {
                        IdxFile file = new IdxFile();
                        List<BundleItem> list = file.Load(indexContent);
                        foreach (var v in list)
                        {
                            localBundlesDict[v.m_Name] = v;
                        }
                    }
                }
                else
                {
                    UnityEngine.Debug.Log("local idx not found");
                }

                WWW www = null;
                string apkIndexPath = "jar:file://" + Application.dataPath + "!/assets/AssetBundle/" + m_IndexFileName;
                www = new WWW(apkIndexPath);
                yield return www;
                if (www.error == null)
                {
                    string indexContent = www.text;
                    IdxFile file = new IdxFile();
                    List<BundleItem> list = file.Load(indexContent);
                    foreach (var v in list)
                    {
                        apkBundlesDict[v.m_Name] = v;
                    }
                }
                else
                {
                    UnityEngine.Debug.Log("apk idx read error " + www.error);
                }

                UnityEngine.Debug.LogFormat("local bundles dict count {0}, apk bundles dict count {1} ", localBundlesDict.Count, apkBundlesDict.Count);

                //下载网上的idx文件
                www = new WWW(m_HttpAddress + ResUtils.BundleName + "/" + m_IndexFileName);
                yield return www;
                if (www.error != null)
                    UnityEngine.Debug.Log("remote idx read error " + www.error);

                m_DownloadingList.Clear();

                if (www.error == null)
                {
                    m_NewIndexContent = www.text;
                    IdxFile file = new IdxFile();
                    List<BundleItem> list = file.Load(m_NewIndexContent);
                    foreach (var v in list)
                    {
                        int localVer = 0;
                        int apkVer = 0;
                        int netVer = v.m_Version;
                        BundleItem apkItem = null;
                        BundleItem localItem = null;
                        if (apkBundlesDict.TryGetValue(v.m_Name, out apkItem))
                            apkVer = apkItem.m_Version;
                        if (localBundlesDict.TryGetValue(v.m_Name, out localItem))
                            localVer = localItem.m_Version;

                        if (netVer > apkVer && netVer > localVer)
                        { //网上的资源较新则重新下载到本地
                            m_DownloadingList.Add(v);
                        }
                        else if (localVer <= apkVer)
                        { //apk里的资源比较新，则无需下载网上资源，并且要删除本地的资源，这样就可以让后续读取apk里的资源
                            if (File.Exists(persistPath + v.m_Name))
                            {
                                File.Delete(persistPath + v.m_Name);
                            }
                        } //其他情况是本地的资源较新，这也无需从网上下载，保持从本地读取即可
                    }

                    UnityEngine.Debug.LogFormat("download idx file success! new bundles count {0}, downloading {1}", list.Count, m_DownloadingList.Count);
                }
                else
                {
                    UnityEngine.Debug.LogFormat("download idx file error! {0}", www.error);
                }
            }
            else
            {
                yield return new WaitForSeconds(1);
            }

            ev?.Invoke();

            yield return null;
        }

        IEnumerator AsyncDownloading(ProcessCompleteEvent ev)
        {
            if (Application.isMobilePlatform)
            {
                m_TotalDownloadBytes = 0;
                m_CurrentDownloadIdx = 0;
                m_AlreadyDownloadBytes = 0;
                foreach (var v in m_DownloadingList)
                {
                    m_TotalDownloadBytes += v.m_FileSize;
                }

                string persistPath = Application.persistentDataPath + "/AssetBundles/";
                foreach (var v in m_DownloadingList)
                {
                    string url = m_HttpAddress + ResUtils.BundleName + "/" + v.m_Name;
                    UnityEngine.Debug.LogFormat("downloading {0} size {1}", v.m_Name, v.m_FileSize);
                    WWW www = new WWW(url);
                    m_www = www;
                    yield return www;
                    if (www.error == null)
                    {
                        string fileName = persistPath + v.m_Name;
                        string dir = fileName.Substring(0, fileName.LastIndexOf('/'));
                        Directory.CreateDirectory(dir);
                        File.WriteAllBytes(fileName, www.bytes);
                    }
                    else
                    {
                        UnityEngine.Debug.LogErrorFormat("downloading {0} error {1}", v.m_Name, www.error);
                    }
                    m_AlreadyDownloadBytes += v.m_FileSize;
                    m_CurrentDownloadIdx++;
                }

                //全部下载成功后，再写入索引文件
                Directory.CreateDirectory(persistPath);
                if (m_NewIndexContent != null)
                {
                    File.WriteAllText(persistPath + m_IndexFileName, m_NewIndexContent);
                    m_NewIndexContent = null;
                }
            }
            else
            {
                yield return new WaitForSeconds(1);
            }

            ev?.Invoke();

            yield return null;
        }

        IEnumerator AsyncLoadLua(ProcessCompleteEvent ev)
        {
            if (Application.isMobilePlatform)
            {
                AssetBundleInfo bundleInfo = new AssetBundleInfo(AssetBundle.LoadFromFile(ResUtils.GetBundleFilePath("lua.unity3d")));
                AssetBundleRequest request = bundleInfo.m_AssetBundle.LoadAllAssetsAsync();
                yield return request;
                var list = request.allAssets;
                TextAsset text = null;
                foreach (var assetObj in list)
                {
                    text = assetObj as TextAsset;
                    if (text)
                    {
                        m_LuaTables[text.name] = text.bytes;
                    }
                }
            }

            ev?.Invoke();

            yield return null;
        }

        public byte[] GetLuaBytes(string name)
        {
            name = name.Replace(".", "/");

            if (Application.isMobilePlatform)
            {
                var subName = name.Substring(name.LastIndexOf('/') + 1);
                if (m_LuaTables.ContainsKey(subName))
                {
                    return m_LuaTables[subName];
                }

                if (m_LuaTables.ContainsKey(name))
                {
                    return m_LuaTables[name];
                }
            }
            else
            {
                try
                {
                    var fileInfo = File.ReadAllText(Application.dataPath + "/Resources/" + name + ".lua");
                    return Encoding.UTF8.GetBytes(fileInfo);
                }
                catch (Exception e)
                {
                    Debug.LogError("editor not find lua file: " + name + e.ToString());
                }
            }

            return null;
        }
    }
}
