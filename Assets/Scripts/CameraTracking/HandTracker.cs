// HandTracker.cs
// Reutiliza el Bootstrap del plugin MediaPipe (webcam + modelo + glog) y expone
// la posicion de la palma (normalizada) y la apertura de la mano (puño abierto/cerrado).

using System;
using System.Collections;
using Stopwatch = System.Diagnostics.Stopwatch;
using Mediapipe;
using Mediapipe.Unity;
using Mediapipe.Unity.Experimental;
using Mediapipe.Unity.Sample;
using Mediapipe.Unity.Sample.HandLandmarkDetection;
using Mediapipe.Tasks.Vision.HandLandmarker;
using UnityEngine;
using UnityEngine.Rendering;

namespace NuiGrab
{
  public class HandTracker : MonoBehaviour
  {
    [SerializeField] private GameObject _bootstrapPrefab;
    [SerializeField, Range(0.5f, 3.0f)] private float _closeThreshold = 1.4f;
    [SerializeField] private bool _mirrorX = false;
    [SerializeField] private bool _mirrorY = true;
    [SerializeField] private bool _logOpenness;

    private readonly HandLandmarkDetectionConfig _config = new HandLandmarkDetectionConfig();

    private Bootstrap _bootstrap;
    private HandLandmarker _handLandmarker;
    private TextureFramePool _textureFramePool;
    private ImageSource _imageSource;
    private readonly Stopwatch _stopwatch = new Stopwatch();
    private Coroutine _runCoroutine;

    private volatile bool _handDetected;
    private volatile float _palmX;
    private volatile float _palmY;
    private volatile float _openness;
    private float _nextLogTime;

    public bool IsHandDetected => _handDetected;
    public bool IsHandClosed => _handDetected && _openness < _closeThreshold;
    public float Openness => _openness;

    public bool MirrorX => _mirrorX;

    /// <summary>Textura actual de la camara (para previsualizacion en UI).</summary>
    public Texture PreviewTexture => _imageSource != null && _imageSource.isPrepared ? _imageSource.GetCurrentTexture() : null;

    /// <summary>Posicion de la palma normalizada (0..1) en coordenadas de la imagen (x izquierda-derecha, y arriba-abajo).</summary>
    public Vector2 HandPosition01 => new Vector2(_palmX, _palmY);

    /// <summary>Posicion de la mano en coordenadas de viewport de Unity (0..1, y hacia arriba), con espejo aplicado.</summary>
    public Vector2 HandViewportPosition
    {
      get
      {
        var x = _mirrorX ? 1f - _palmX : _palmX;
        var y = _mirrorY ? 1f - _palmY : _palmY;
        return new Vector2(Mathf.Clamp01(x), Mathf.Clamp01(y));
      }
    }

    private IEnumerator Start()
    {
      _bootstrap = FindBootstrap();
      yield return new WaitUntil(() => _bootstrap.isFinished);

      _runCoroutine = StartCoroutine(Run());
    }

    private Bootstrap FindBootstrap()
    {
      var bootstrapObj = GameObject.Find("Bootstrap");

      if (bootstrapObj == null)
      {
        if (_bootstrapPrefab == null)
        {
          _bootstrapPrefab = Resources.Load<GameObject>("Bootstrap");
        }

        if (_bootstrapPrefab == null)
        {
          throw new InvalidOperationException("Bootstrap prefab is not assigned and not found under a Resources folder");
        }

        Debug.Log("Initializing the Bootstrap GameObject");
        bootstrapObj = Instantiate(_bootstrapPrefab);
        bootstrapObj.name = "Bootstrap";
        DontDestroyOnLoad(bootstrapObj);
      }

      return bootstrapObj.GetComponent<Bootstrap>();
    }

    private IEnumerator Run()
    {
      Debug.Log($"Delegate = {_config.Delegate}");
      Debug.Log($"Image Read Mode = {_config.ImageReadMode}");
      Debug.Log($"Running Mode = {_config.RunningMode}");

      yield return AssetLoader.PrepareAssetAsync(_config.ModelPath);

      var options = _config.GetHandLandmarkerOptions(OnHandLandmarkOutput);
      _handLandmarker = HandLandmarker.CreateFromOptions(options, GpuManager.GpuResources);
      _imageSource = ImageSourceProvider.ImageSource;

      yield return _imageSource.Play();

      if (!_imageSource.isPrepared)
      {
        Debug.LogError("Failed to start ImageSource, exiting...");
        yield break;
      }

      _textureFramePool = new TextureFramePool(_imageSource.textureWidth, _imageSource.textureHeight, TextureFormat.RGBA32, 10);

      var transformationOptions = _imageSource.GetTransformationOptions();
      var flipHorizontally = transformationOptions.flipHorizontally;
      var flipVertically = transformationOptions.flipVertically;
      var imageProcessingOptions = new Mediapipe.Tasks.Vision.Core.ImageProcessingOptions(rotationDegrees: (int)transformationOptions.rotationAngle);

      AsyncGPUReadbackRequest req = default;
      var waitUntilReqDone = new WaitUntil(() => req.done);
      var waitForEndOfFrame = new WaitForEndOfFrame();

      _stopwatch.Restart();

      while (true)
      {
        if (!_textureFramePool.TryGetTextureFrame(out var textureFrame))
        {
          yield return waitForEndOfFrame;
          continue;
        }

        req = textureFrame.ReadTextureAsync(_imageSource.GetCurrentTexture(), flipHorizontally, flipVertically);
        yield return waitUntilReqDone;

        if (req.hasError)
        {
          Debug.LogWarning("Failed to read texture from the image source");
          continue;
        }

        var image = textureFrame.BuildCPUImage();
        textureFrame.Release();

        _handLandmarker.DetectAsync(image, GetCurrentTimestampMillisec(), imageProcessingOptions);
      }
    }

    private long GetCurrentTimestampMillisec() => _stopwatch.ElapsedTicks / TimeSpan.TicksPerMillisecond;

    private void OnHandLandmarkOutput(HandLandmarkerResult result, Image image, long timestamp)
    {
      if (result.handLandmarks == null || result.handLandmarks.Count == 0)
      {
        _handDetected = false;
        return;
      }

      var landmarks = result.handLandmarks[0].landmarks;

      if (landmarks == null || landmarks.Count < 21)
      {
        _handDetected = false;
        return;
      }

      var wrist = landmarks[0];
      var palmX = (landmarks[0].x + landmarks[5].x + landmarks[9].x + landmarks[13].x + landmarks[17].x) / 5f;
      var palmY = (landmarks[0].y + landmarks[5].y + landmarks[9].y + landmarks[13].y + landmarks[17].y) / 5f;

      var scale = Dist(landmarks[9].x, landmarks[9].y, wrist.x, wrist.y);

      if (scale <= 0f)
      {
        _handDetected = false;
        return;
      }

      var avgTip = (Dist(landmarks[8].x, landmarks[8].y, wrist.x, wrist.y) +
                    Dist(landmarks[12].x, landmarks[12].y, wrist.x, wrist.y) +
                    Dist(landmarks[16].x, landmarks[16].y, wrist.x, wrist.y) +
                    Dist(landmarks[20].x, landmarks[20].y, wrist.x, wrist.y)) / 4f;

      _palmX = palmX;
      _palmY = palmY;
      _openness = avgTip / scale;
      _handDetected = true;
    }

    private static float Dist(float ax, float ay, float bx, float by)
    {
      var dx = ax - bx;
      var dy = ay - by;
      return Mathf.Sqrt(dx * dx + dy * dy);
    }

    private void Update()
    {
      if (_logOpenness && _handDetected && Time.time >= _nextLogTime)
      {
        Debug.Log($"openness={_openness:F2} closed={IsHandClosed}");
        _nextLogTime = Time.time + 0.5f;
      }
    }

    private void OnDestroy()
    {
      if (_runCoroutine != null)
      {
        StopCoroutine(_runCoroutine);
      }

      _imageSource?.Stop();
      _textureFramePool?.Dispose();
      _handLandmarker?.Close();
    }
  }
}
