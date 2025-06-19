using System.Collections;
using UnityEngine;
using Unity.WebRTC;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine.Experimental.Rendering;
using System;

public class WebRTCMultiCameraPublisher : MonoBehaviour

{
    public int videoWidth = 1280;
    public int videoHeight = 720;

    private class CameraStream
    {
        public RTCPeerConnection pc;
        public VideoStreamTrack videoTrack;
        public RenderTexture renderTexture;
    }

    private readonly List<CameraStream> cameraStreams = new();


    void Start()
    {
        StartCoroutine(WebRTC.Update());

        Camera[] cameras = GetComponentsInChildren<Camera>();
        if (cameras.Length == 0)
        {
            Debug.LogError("No cameras found.");
            return;
        }

        foreach (Camera cam in cameras)
        {
            string streamName = cam.name.ToLower();
            StartCoroutine(StartCameraStream(cam, streamName));
        }
    }

    private IEnumerator StartCameraStream(Camera camera, string streamName)
    {
        var rt = new RenderTexture(videoWidth, videoHeight, 0)
        {
            graphicsFormat = GraphicsFormat.B8G8R8A8_SRGB
        };
        rt.Create();
        camera.targetTexture = rt;

        yield return new WaitForSeconds(0.1f);

        var videoTrack = new VideoStreamTrack(rt);
        var pc = new RTCPeerConnection();
        pc.AddTrack(videoTrack);

        cameraStreams.Add(new CameraStream
        {
            pc = pc,
            videoTrack = videoTrack,
            renderTexture = rt
        });

        var offerOp = pc.CreateOffer();
        yield return offerOp;

        if (offerOp.IsError)
        {
            Debug.LogError($"CreateOffer failed for camera {camera.name}");
            yield break;
        }

        var offer = offerOp.Desc;
        yield return pc.SetLocalDescription(ref offer);

        string whipHost = Environment.GetEnvironmentVariable("WHIP_HOST") ?? "http://localhost:8889";
        string whipUrl = $"{whipHost}/{streamName}/whip";



        var content = new StringContent(offer.sdp);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/sdp");

        var client = new HttpClient();
        var task = Task.Run(async () =>
        {
            Debug.Log($"Sending WHIP POST to: {whipUrl}");
            Debug.Log(content);
            var response = await client.PostAsync(whipUrl, content);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        });

        yield return new WaitUntil(() => task.IsCompleted);

        if (task.Exception != null)
        {
            Debug.LogError($"{whipUrl}");
            Debug.LogError($"WHIP POST failed for {camera.name}: {task.Exception?.GetBaseException().Message}");
            yield break;
        }

        var answer = new RTCSessionDescription
        {
            type = RTCSdpType.Answer,
            sdp = task.Result
        };
        yield return pc.SetRemoteDescription(ref answer);

        Debug.Log($"Stream started for camera: {camera.name}");
    }

    //метод дл€ начала трансл€ции программно созданных в рантайме камер
    public void PublishCamera(Camera camera)
    {
        string streamName = camera.name.ToLower();
        StartCoroutine(StartCameraStream(camera, streamName));
    }

    void OnDestroy()
    {
        foreach (var cs in cameraStreams)
        {
            cs.pc.Close();
            cs.pc.Dispose();
            cs.videoTrack.Dispose();
            cs.renderTexture.Release();
        }
    }

    public static WebRTCMultiCameraPublisher Instance;

    void Awake()
    {
        Instance = this;
    }
}
