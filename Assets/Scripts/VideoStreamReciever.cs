using System;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using System.Text;
using UnityEngine.XR;

public class VideoStreamReceiver : MonoBehaviour
{
    [Header ("Tracking")]
    public Transform camera;

    [Header("Server Settings")]
    public string host = "10.214.160.209";
    public int port = 5900;

    [Header("Display Targets")]
    public Material leftEyeMaterial;
    public Material rightEyeMaterial;

    [Header("Optional UI")]
    public RawImage leftRawImage;
    public RawImage rightRawImage;

    private TcpClient client;
    private NetworkStream netStream;
    private Thread receiveThread;

    // For reading headers: 1 byte type + 4 bytes length
    private byte[] headerBuf = new byte[5];
    private byte[] payloadBuf = null;
    private object frameLock = new object();

    // Two Texture2Ds, one per eye
    private Texture2D leftTex;
    private Texture2D rightTex;

    // Buffers for the latest JPEGs
    private byte[] latestLeftData = null;
    private byte[] latestRightData = null;

    void Start()
    {
        leftTex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        rightTex = new Texture2D(2, 2, TextureFormat.RGB24, false);

        // Connect to the combined control+video server
        client = new TcpClient();
        client.Connect(host, port);
        netStream = client.GetStream();
        Debug.Log($"Connected to stereo server at {host}:{port}");

        // recieve loop
        receiveThread = new Thread(ReceiveLoop) { IsBackground = true };
        receiveThread.Start();
    }

    void Update()
    {
        Vector3 raw = camera.rotation.eulerAngles;
        float pitch = raw.x > 180f ? raw.x - 360f : raw.x;
        float yaw = raw.y > 180f ? raw.y - 360f : raw.y;
        float roll = raw.z > 180f ? raw.z - 360f : raw.z;

        pitch = -pitch;
        yaw = -yaw;

        string ctrlStr = $"{pitch:F1},{yaw:F1},{roll:F1}";
        byte[] ctrlBytes = Encoding.UTF8.GetBytes(ctrlStr);
        byte[] ctrlHeader = new byte[5];
        ctrlHeader[0] = 0x01; // TYPE_CONTROL
        int clen = ctrlBytes.Length;
        ctrlHeader[1] = (byte)((clen >> 24) & 0xFF);
        ctrlHeader[2] = (byte)((clen >> 16) & 0xFF);
        ctrlHeader[3] = (byte)((clen >> 8) & 0xFF);
        ctrlHeader[4] = (byte)(clen & 0xFF);

        try
        {
            netStream.Write(ctrlHeader, 0, 5);
            netStream.Write(ctrlBytes, 0, clen);
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to send control: {ex.Message}");
        }

        // Check if the receive loop has queued up a new left/right JPEG
        byte[] leftData = null;
        byte[] rightData = null;
        lock (frameLock)
        {
            if (latestLeftData != null)
            {
                leftData = latestLeftData;
                latestLeftData = null;
            }
            if (latestRightData != null)
            {
                rightData = latestRightData;
                latestRightData = null;
            }
        }

        // If we got a left-eye JPEG, decode & assign to leftEyeMaterial
        if (leftData != null)
        {
            if (leftTex.LoadImage(leftData))
            {
                var pixels = leftTex.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte tmp = pixels[i].r;
                    pixels[i].r = pixels[i].b;
                    pixels[i].b = tmp;
                }
                leftTex.SetPixels32(pixels);
                leftTex.Apply();

                if (leftEyeMaterial != null) leftEyeMaterial.mainTexture = leftTex;
                if (leftRawImage != null) leftRawImage.texture = leftTex;
            }
            else
            {
                Debug.LogWarning("Failed to decode LEFT eye JPEG");
            }
        }

        // If we got a right-eye JPEG, decode & assign to rightEyeMaterial
        if (rightData != null)
        {

            if (rightTex.LoadImage(rightData))
            {
                var pixels = rightTex.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                {
                    byte tmp = pixels[i].r;
                    pixels[i].r = pixels[i].b;
                    pixels[i].b = tmp;
                }
                rightTex.SetPixels32(pixels);
                rightTex.Apply();

                if (rightEyeMaterial != null) rightEyeMaterial.mainTexture = rightTex;
                if (rightRawImage != null) rightRawImage.texture = rightTex;
            }
            else
            {
                Debug.LogWarning("Failed to decode RIGHT eye JPEG");
            }
        }
    }

    private void ReceiveLoop()
    {
        try
        {
            while (true)
            {
                // read 1 byte for message type
                int b = netStream.ReadByte();
                if (b == -1) throw new Exception("Server closed connection");
                byte msgType = (byte)b;

                // read next 4 bytes for payload length
                int read = 0;
                while (read < 4)
                {
                    int r = netStream.Read(headerBuf, 1 + read, 4 - read);
                    if (r <= 0) throw new Exception("Stream closed mid-length");
                    read += r;
                }
                int payloadLen =
                    (headerBuf[1] << 24) |
                    (headerBuf[2] << 16) |
                    (headerBuf[3] << 8) |
                    (headerBuf[4]);

                // read exactly 'payloadLen' bytes
                if (payloadBuf == null || payloadBuf.Length < payloadLen)
                    payloadBuf = new byte[payloadLen];
                int offset = 0;
                while (offset < payloadLen)
                {
                    int chunk = netStream.Read(payloadBuf, offset, payloadLen - offset);
                    if (chunk <= 0) throw new Exception("Stream closed mid-payload");
                    offset += chunk;
                }

                // dispatch based on msgType
                if (msgType == 0x03) // TYPE_VIDEO_LEFT
                {
                    Debug.Log($"Received left video frame");
                    byte[] copy = new byte[payloadLen];
                    Buffer.BlockCopy(payloadBuf, 0, copy, 0, payloadLen);
                    lock (frameLock)
                    {
                        latestLeftData = copy;
                    }
                }
                else if (msgType == 0x04) // TYPE_VIDEO_RIGHT
                {
                    Debug.Log($"Received right video frame");
                    byte[] copy = new byte[payloadLen];
                    Buffer.BlockCopy(payloadBuf, 0, copy, 0, payloadLen);
                    lock (frameLock)
                    {
                        latestRightData = copy;
                    }
                }
                else if (msgType == 0x02) // TYPE_ACK
                {
                    // we can ignore
                }
                else
                {
                    Debug.LogWarning($"Unknown msgType {msgType}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"ReceiveLoop ended: {ex.Message}");
        }
        finally
        {
            netStream.Close();
            client.Close();
        }
    }

    void OnApplicationQuit()
    {
        if (receiveThread != null && receiveThread.IsAlive)
            receiveThread.Abort();
        if (netStream != null) netStream.Close();
        if (client != null) client.Close();
    }
}
