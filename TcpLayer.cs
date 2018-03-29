using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.IO;
using System.Net.Sockets;
using UnityEngine;

namespace U3DUtility
{
    public struct Pkt
    {
        public short messId;
        public byte[] data;
    }

    public class TcpLayer : MonoBehaviour
    {
        class AsyncData
        {
            public int pos;
            public short messId;
            public byte[] buff;
        }

        public const int PACK_HEAD_SIZE = 4;
        public const int MSG_ID_SIZE = 2;

        public delegate void OnConnectEvent(bool isSuccess, string msg);
        public delegate void OnDisconnectEvent(string msg);
        public delegate void OnRecvEvent(int msgId, byte[] data);

        private TcpClient m_TcpClient = new TcpClient();
        private NetworkStream m_NetStream = null;
        private bool m_IsConnected = false;
        private OnConnectEvent m_OnConnect;
        private OnDisconnectEvent m_OnDisConnect;
        private OnRecvEvent m_OnRecvPackage;

        private Queue<Pkt> m_RecvPacks = new Queue<Pkt>();

        private static TcpLayer m_Singleton = null;

        public static TcpLayer Singleton
        {
            get
            {
                if (m_Singleton == null)
                {
                    GameObject o = new GameObject();
                    DontDestroyOnLoad(o);
                    m_Singleton = o.AddComponent<TcpLayer>();
                }

                return m_Singleton;
            }
        }

        public void Init (int recvBuffSize, int sendBuffSize)
        {
            m_TcpClient.NoDelay = true;
            m_TcpClient.ReceiveBufferSize = recvBuffSize;
            m_TcpClient.SendBufferSize = sendBuffSize;
        }

        public void Connect(string ip, int port, OnConnectEvent connEvent, OnDisconnectEvent disconnEvent, OnRecvEvent recvEvent)
        {
            m_OnConnect = connEvent;
            m_OnDisConnect = disconnEvent;
            m_OnRecvPackage = recvEvent;

            m_IsConnected = false;
            m_TcpClient.BeginConnect(IPAddress.Parse(ip), port, new AsyncCallback(OnConnectCallback), m_TcpClient);
        }

        public void Disconnect()
        {
            if (m_IsConnected)
            {
                m_NetStream.Close();
                m_TcpClient.Close();
            }
        }

        public void SendPack(short messId, byte[] data)
        {
            int length = data.Length + PACK_HEAD_SIZE + MSG_ID_SIZE;
            MemoryStream dataStream = new MemoryStream(length);
            BinaryWriter binaryWriter = new BinaryWriter(dataStream);

            binaryWriter.Write(data.Length + 2);
            binaryWriter.Write((short)messId);
            binaryWriter.Write(data, 0, (int)data.Length);

            dataStream.Seek((long)0, 0);
            binaryWriter.Close();
            dataStream.Close();

            byte[] sendBytes = dataStream.GetBuffer();

            m_NetStream.Write(sendBytes, 0, length);
        }

        void OnConnectCallback(IAsyncResult asyncResult)
        {
            try
            {
                TcpClient tcpclient = asyncResult.AsyncState as TcpClient;

                if (tcpclient.Client != null)
                {
                    tcpclient.EndConnect(asyncResult);
                }
            }
            catch (Exception ex)
            {
                U3DUtility.Loom.QueueOnMainThread(() =>
                {
                    m_OnConnect?.Invoke(false, ex.ToString());
                });
            }
            finally
            {
                m_NetStream = m_TcpClient.GetStream();

                BeginPackRead();

                U3DUtility.Loom.QueueOnMainThread(() =>
                {
                    m_IsConnected = true;
                    m_OnConnect?.Invoke(true, "");
                });
            }
        }

        void Update()
        {
            if (!m_IsConnected)
            {
                return;
            }

            //处理所有接收的包
            lock(m_RecvPacks)
            {
                for (; m_RecvPacks.Count > 0;)
                {
                    var pkt = m_RecvPacks.Dequeue();
                    m_OnRecvPackage?.Invoke(pkt.messId, pkt.data);
                }
            }
        }

        /// <summary>
        /// 接收包头的异步回调
        /// </summary>
        /// <param name="asyncResult">异步参数</param>
        void ReadAsyncCallBackPackHead(IAsyncResult asyncResult)
        {
            try
            {
                int dataLen = m_NetStream.EndRead(asyncResult);

                AsyncData head_data = (AsyncData)asyncResult.AsyncState;

                if (head_data.pos + dataLen == head_data.buff.Length) //如果包头读取完毕则开始读取数据部分
                {
                    int packLen = new BinaryReader(new MemoryStream(head_data.buff)).ReadInt32();
                    short msgID = new BinaryReader(new MemoryStream(head_data.buff, PACK_HEAD_SIZE, MSG_ID_SIZE)).ReadInt16();

                    AsyncData pack_data = new AsyncData
                    {
                        buff = new byte[packLen],
                        pos = 0,
                        messId = msgID
                    };

                    m_NetStream.BeginRead(pack_data.buff, 0, pack_data.buff.Length, new AsyncCallback(ReadAsyncCallBackPack), pack_data);
                }
                else //没读取完则继续读取
                {
                    head_data.pos += dataLen;

                    m_NetStream.BeginRead(head_data.buff, head_data.pos, head_data.buff.Length - head_data.pos, new AsyncCallback(ReadAsyncCallBackPackHead), head_data);
                }
            }
            catch (Exception ex)
            {
                U3DUtility.Loom.QueueOnMainThread(() =>
                {
                    Disconnect();

                    U3DUtility.Loom.QueueOnMainThread(() =>
                    {
                        if (m_OnDisConnect != null)
                        {
                            m_IsConnected = false;
                            m_OnDisConnect(ex.ToString());
                        }
                    });
                });

                return;
            }
        }

        void ReadAsyncCallBackPack(IAsyncResult asyncResult)
        {
            try
            {
                int dataLen = m_NetStream.EndRead(asyncResult);

                AsyncData data = (AsyncData)asyncResult.AsyncState;

                if (data.pos + dataLen == data.buff.Length) //读取完毕后放入队列，开始读取下一个包
                {
                    Pkt p;
                    p.data = data.buff;
                    p.messId = data.messId;

                    lock(m_RecvPacks)
                    {
                        m_RecvPacks.Enqueue(p);
                    }

                    BeginPackRead();
                }
                else //没读取完需要继续读取
                {
                    data.pos += dataLen;

                    m_NetStream.BeginRead(data.buff, data.pos, data.buff.Length - data.pos, new AsyncCallback(ReadAsyncCallBackPackHead), data);
                }
            }
            catch (Exception ex)
            {
                U3DUtility.Loom.QueueOnMainThread(() =>
                {
                    Disconnect();

                    U3DUtility.Loom.QueueOnMainThread(() =>
                    {
                        if (m_OnDisConnect != null)
                        {
                            m_IsConnected = false;
                            m_OnDisConnect(ex.ToString());
                        }
                    });
                });
            }
        }

        void BeginPackRead()
        {
            AsyncData data = new AsyncData
            {
                buff = new byte[PACK_HEAD_SIZE + MSG_ID_SIZE],
                pos = 0
            };
            m_NetStream.BeginRead(data.buff, 0, data.buff.Length, new AsyncCallback(ReadAsyncCallBackPackHead), data);
        }

    }
}
