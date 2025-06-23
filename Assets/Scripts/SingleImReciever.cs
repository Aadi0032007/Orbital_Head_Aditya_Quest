using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

public class SingleImReceiver : MonoBehaviour
{
    [Header("Tracking")] public Transform camera;

    [Header("Server Settings")]
    public string host = "50.39.109.27";
    public int controlPort = 60065;
    public int videoPort = 60064;

    [Header("Display Targets")]
    public Material leftEyeMaterial;
    public Material rightEyeMaterial;

    private TcpClient controlClient;
    private NetworkStream controlStream;

    private TcpClient videoClient;
    private NetworkStream videoStream;

    private Thread receiveThread;
    private byte[] headerBuf = new byte[5];
    private byte[] payloadBuf = null;
    private object frameLock = new object();
    private Texture2D combinedTex;
    private Texture2D leftTex, rightTex;
    private byte[] latestImageData = null;
    private volatile bool quit = false;

    const byte TYPE_VIDEO = 0x03;
    const byte TYPE_ACK = 0x02;

    void Start()
    {
        videoClient = new TcpClient();
        videoClient.NoDelay = true;
        videoClient.Connect(host, videoPort);
        videoStream = videoClient.GetStream();
        Debug.Log($"Connected video to   {host}:{videoPort}");

        controlClient = new TcpClient();
        controlClient.NoDelay = true;
        controlClient.Connect(host, controlPort);
        controlStream = controlClient.GetStream();
        Debug.Log($"Connected control to {host}:{controlPort}");

        combinedTex = new Texture2D(1280, 480, TextureFormat.RGB24, false);

        // both materials share the same combinedTex,
        // and shader + UV tiling do the split/swizzle for you:
        leftEyeMaterial.SetTexture("_MainTex", combinedTex);
        rightEyeMaterial.SetTexture("_MainTex", combinedTex);

        leftEyeMaterial.mainTextureScale = new Vector2(0.5f, 1f);
        leftEyeMaterial.mainTextureOffset = new Vector2(0.0f, 0f);

        rightEyeMaterial.mainTextureScale = new Vector2(0.5f, 1f);
        rightEyeMaterial.mainTextureOffset = new Vector2(0.5f, 0f);

        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        receiveThread.Start();
    }

    void Update()
    {
        Vector3 raw = camera.rotation.eulerAngles;
        float pitch = raw.x > 180f ? raw.x - 360f : raw.x;
        float yaw = raw.y > 180f ? raw.y - 360f : raw.y;
        float roll = raw.z > 180f ? raw.z - 360f : raw.z;

        string ctrlStr = $"{pitch:F1},{yaw:F1},{roll:F1}";
        byte[] ctrlBytes = System.Text.Encoding.UTF8.GetBytes(ctrlStr);
        byte[] hdr = new byte[5];
        hdr[0] = 0x01;
        int len = ctrlBytes.Length;
        hdr[1] = (byte)((len >> 24) & 0xFF);
        hdr[2] = (byte)((len >> 16) & 0xFF);
        hdr[3] = (byte)((len >> 8) & 0xFF);
        hdr[4] = (byte)(len & 0xFF);

        try
        {
            controlStream.Write(hdr, 0, 5);
            controlStream.Write(ctrlBytes, 0, len);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Control send failed: {ex.Message}");
        }

        byte[] imageData = null;
        lock (frameLock)
        {
            if (latestImageData != null)
            {
                imageData = latestImageData;
                latestImageData = null;
            }
        }
        if (imageData != null && combinedTex.LoadImage(imageData, markNonReadable: true))
        {
            // combinedTex.Apply(updateMipmaps:false, makeNoLongerReadable:true);
        }
    }

    private void ReceiveLoop()
    {
        try
        {
            while (!quit)
            {
                int b = videoStream.ReadByte();
                if (b < 0) throw new Exception("Video socket closed");

                int read = 0;
                while (read < 4)
                {
                    int r = videoStream.Read(headerBuf, 1 + read, 4 - read);
                    if (r <= 0) throw new Exception("Stream closed mid-length");
                    read += r;
                }

                int payloadLen = (headerBuf[1] << 24)
                               | (headerBuf[2] << 16)
                               | (headerBuf[3] << 8)
                               | headerBuf[4];

                if (payloadBuf == null || payloadBuf.Length < payloadLen)
                    payloadBuf = new byte[payloadLen];

                int offset = 0;
                while (offset < payloadLen)
                {
                    int chunk = videoStream.Read(payloadBuf, offset, payloadLen - offset);
                    if (chunk <= 0) throw new Exception("Stream closed mid-payload");
                    offset += chunk;
                }

                // Send ACK immediately before decoding
                byte[] ackPayload = System.Text.Encoding.UTF8.GetBytes("ack");
                byte[] ackHdr = new byte[5];
                ackHdr[0] = TYPE_ACK;
                int aLen = ackPayload.Length;
                ackHdr[1] = (byte)((aLen >> 24) & 0xFF);
                ackHdr[2] = (byte)((aLen >> 16) & 0xFF);
                ackHdr[3] = (byte)((aLen >> 8) & 0xFF);
                ackHdr[4] = (byte)(aLen & 0xFF);
                videoStream.Write(ackHdr, 0, 5);
                videoStream.Write(ackPayload, 0, aLen);

                // Store frame for rendering
                lock (frameLock)
                    latestImageData = (byte[])payloadBuf.Clone();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"ReceiveLoop ended: {ex.Message}");
        }
        finally
        {
            videoStream?.Close();
            videoClient?.Close();
        }
    }

    void OnApplicationQuit()
    {
        quit = true;
        receiveThread?.Join();
        controlStream?.Close();
        controlClient?.Close();
        videoStream?.Close();
        videoClient?.Close();
    }
}
