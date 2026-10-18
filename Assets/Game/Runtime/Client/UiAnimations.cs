using UnityEngine;
using UnityEngine.UI;

namespace Nasledie.Client
{
    /// <summary>Lightweight, unscaled sprite animation for map and city interface artwork.</summary>
    public sealed class UiSpriteLoop : MonoBehaviour
    {
        private Image image;
        private Sprite[] frames;
        private float frameDuration;
        private float elapsed;
        private int frameIndex;

        public void Initialize(Image target, Sprite[] animationFrames, float framesPerSecond)
        {
            image = target;
            frames = animationFrames;
            frameDuration = 1f / Mathf.Max(1f, framesPerSecond);
            elapsed = 0f;
            frameIndex = 0;
            if (image != null && frames != null && frames.Length > 0) image.sprite = frames[0];
            enabled = image != null && frames != null && frames.Length > 1;
        }

        private void Update()
        {
            if (image == null || frames == null || frames.Length < 2) return;
            elapsed += Time.unscaledDeltaTime;
            if (elapsed < frameDuration) return;
            int framesToAdvance = Mathf.Min(4, Mathf.FloorToInt(elapsed / frameDuration));
            elapsed -= framesToAdvance * frameDuration;
            frameIndex = (frameIndex + framesToAdvance) % frames.Length;
            image.sprite = frames[frameIndex];
        }
    }

    /// <summary>Small deterministic bob used to keep single-pose unit and building sprites alive.</summary>
    public sealed class UiIdleMotion : MonoBehaviour
    {
        private RectTransform rect;
        private Vector2 restPosition;
        private float phase;
        private float amplitude = 2f;
        private float period = 2.3f;

        public void Initialize(float verticalPixels, float cycleSeconds, float phaseOffset)
        {
            rect = transform as RectTransform;
            amplitude = Mathf.Max(0f, verticalPixels);
            period = Mathf.Max(0.5f, cycleSeconds);
            phase = phaseOffset;
            if (rect != null) restPosition = rect.anchoredPosition;
        }

        private void LateUpdate()
        {
            if (rect == null) rect = transform as RectTransform;
            if (rect == null) return;
            rect.anchoredPosition = restPosition + Vector2.up * (Mathf.Sin((Time.unscaledTime / period + phase) * Mathf.PI * 2f) * amplitude);
        }
    }

    /// <summary>Gentle glow on active construction markers and progress strips.</summary>
    public sealed class UiConstructionPulse : MonoBehaviour
    {
        private Image image;
        private Color baseColor;
        private float phase;

        public void Initialize(Image target, float phaseOffset)
        {
            image = target;
            phase = phaseOffset;
            if (image != null) baseColor = image.color;
        }

        private void Update()
        {
            if (image == null) return;
            float pulse = 0.76f + Mathf.Sin((Time.unscaledTime * 1.6f + phase) * Mathf.PI * 2f) * 0.18f;
            Color color = baseColor;
            color.a *= pulse;
            image.color = color;
        }
    }
}
