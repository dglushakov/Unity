using UnityEngine;
using System;
using System.Net;
using System.Threading;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Text;

public class UnityHttpListener : MonoBehaviour
{
    private HttpListener listener;
    private Thread listenerThread;

    //очередь для обмена между потоками
    private ConcurrentQueue<Action> mainThreadActions = new();

    public int port = 8080;

    private Dictionary<string, Func<HttpListenerRequest, string>> routes;

    void Start()
    {
        routes = new Dictionary<string, Func<HttpListenerRequest, string>>
        {
            { "/", HomeHandler },
            { "/cameras", CamerasList }, //вернуть список камер
            { "/rotate", RotateCameraHandler }, //повернут ькамеру  http://localhost:8080/rotate?name=MainCamera&axis=y&angle=45
            { "/create-camera", CreateCameraHandler }, //создаем новую камеру http://localhost:8080/create-camera?name=Cam2&x=1&y=2&z=3
        };

        listener = new HttpListener();
        listener.Prefixes.Add($"http://*:{port}/");
        listener.Start();

        listenerThread = new Thread(HandleRequests);
        listenerThread.Start();

        Debug.Log($"HTTP Router started at http://localhost:{port}/");
    }

    void Update()
    {
        // Выполнение действий, требующих главный поток
        while (mainThreadActions.TryDequeue(out var action))
        {
            action.Invoke();
        }
    }

    private void HandleRequests()
    {
        while (listener.IsListening)
        {
            try
            {
                var context = listener.GetContext();
                var request = context.Request;
                var response = context.Response;

                string path = request.Url.AbsolutePath.ToLower();
                string responseText = "404 Not Found";

                var resetEvent = new ManualResetEvent(false);

                mainThreadActions.Enqueue(() =>
                {
                    if (routes.TryGetValue(path, out var handler))
                    {
                        responseText = handler(request);
                        response.StatusCode = 200;
                    }
                    else
                    {
                        response.StatusCode = 404;
                    }

                    resetEvent.Set();
                });

                resetEvent.WaitOne();

                byte[] buffer = Encoding.UTF8.GetBytes(responseText);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                Debug.LogError($"HTTP Listener error: {ex.Message}");
            }
        }
    }

    // Обработчики маршрутов
    private string HomeHandler(HttpListenerRequest req)
    {
        return "Welcome to Unity HTTP Server";
    }

    private string CamerasList(HttpListenerRequest req)
    {
        Camera[] cameras = Camera.allCameras;
        List<string> cameraNames = new();

        foreach (var cam in cameras)
        {
            cameraNames.Add(cam.name);
        }

        return "Cameras:\n" + string.Join("\n", cameraNames);
    }

    private string RotateCameraHandler(HttpListenerRequest req)
    {
        string name = req.QueryString["name"];
        string axis = req.QueryString["axis"];
        string angleStr = req.QueryString["angle"];

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(axis) || string.IsNullOrEmpty(angleStr))
            return "Missing parameters: name, axis, or angle.";

        if (!float.TryParse(angleStr, out float angle))
            return "Invalid angle value.";

        Camera cam = Array.Find(Camera.allCameras, c => c.name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (cam == null)
            return $"Camera '{name}' not found.";

        Vector3 rotationAxis;
        switch (axis.ToLower())
        {
            case "x": rotationAxis = Vector3.right; break;
            case "y": rotationAxis = Vector3.up; break;
            case "z": rotationAxis = Vector3.forward; break;
            default: return "Invalid axis. Use x, y or z.";
        }

        cam.transform.Rotate(rotationAxis, angle);
        return $"Rotated camera '{name}' around {axis}-axis by {angle} degrees.";
    }

    private string CreateCameraHandler(HttpListenerRequest req)
    {
        string name = req.QueryString["name"];
        string xStr = req.QueryString["x"];
        string yStr = req.QueryString["y"];
        string zStr = req.QueryString["z"];

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(xStr) || string.IsNullOrEmpty(yStr) || string.IsNullOrEmpty(zStr))
            return "Missing parameters: name, x, y, or z.";

        if (!float.TryParse(xStr, out float x) ||
            !float.TryParse(yStr, out float y) ||
            !float.TryParse(zStr, out float z))
            return "Invalid coordinate values.";

        // Проверка на существование камеры с таким именем
        bool cameraExists = false;
        Camera[] allCameras = Camera.allCameras;
        foreach (var cam in allCameras)
        {
            if (cam.name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                cameraExists = true;
                break;
            }
        }

        if (cameraExists)
        {
            return $"Camera with name '{name}' already exists.";
        }

        // Создание камеры в главном потоке
        mainThreadActions.Enqueue(() =>
        {
            GameObject parent = GameObject.Find("Cameras");
            if (parent == null)
                parent = new GameObject("Cameras");

            GameObject camObject = new GameObject(name);
            camObject.transform.parent = parent.transform;
            camObject.transform.position = new Vector3(x, y, z);
            Camera cam = camObject.AddComponent<Camera>();

            Debug.Log($"Created camera '{name}' at ({x}, {y}, {z})");

            // Запустить WebRTC трансляцию
            WebRTCMultiCameraPublisher.Instance?.PublishCamera(cam);
        });

        return $"Creating camera '{name}' at ({x}, {y}, {z})...";
    }


    void OnApplicationQuit()
    {
        listener?.Stop();
        listenerThread?.Abort();
    }
}
