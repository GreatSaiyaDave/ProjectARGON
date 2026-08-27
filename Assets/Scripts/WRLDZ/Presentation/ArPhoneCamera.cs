using System;
using System.Collections;
using UnityEngine;
using WRLDZ.Core;

namespace WRLDZ.Presentation
{
    /// <summary>
    /// Phone AR camera session for <b>S23 Ultra</b> (and Editor webcam preview).
    /// Handles Android permission, rear-camera pick, play, and UV / plane orientation.
    /// No headset / OpenXR dependency.
    /// </summary>
    public static class ArPhoneCamera
    {
        public sealed class Session
        {
            public WebCamTexture Texture;
            public string DeviceName;
            public bool Live;
            public string Status = "idle";
        }

        /// <summary>
        /// Request permission, open preferred rear camera, wait until playing.
        /// Returns a session (Live=false if unavailable — spatial sim still works).
        /// </summary>
        public static IEnumerator Open(Session session, int width = 1280, int height = 720, int fps = 30)
        {
            if (session == null) yield break;
            session.Live = false;
            session.Status = "requesting permission";

            yield return WrldzLab.RequestLabPermissions();

            if (!WrldzLab.HasCameraPermission && !Application.isEditor)
            {
                session.Status = "camera permission denied";
                Debug.LogWarning("[WRLDZ] Phone AR: camera permission denied.");
                yield break;
            }

            // One frame after permission so device list refreshes on Android
            yield return null;
            yield return null;

            WebCamDevice[] devices;
            try
            {
                devices = WebCamTexture.devices;
            }
            catch (Exception ex)
            {
                session.Status = "device list failed: " + ex.Message;
                Debug.LogWarning("[WRLDZ] Phone AR: " + session.Status);
                yield break;
            }

            if (devices == null || devices.Length == 0)
            {
                session.Status = "no camera devices";
                Debug.Log("[WRLDZ] Phone AR: no camera — spatial sim only.");
                yield break;
            }

            // Prefer rear camera on S23 / phones
            var deviceName = devices[0].name;
            foreach (var d in devices)
            {
                if (!d.isFrontFacing)
                {
                    deviceName = d.name;
                    break;
                }
            }

            session.DeviceName = deviceName;
            session.Status = "starting " + deviceName;
            Debug.Log($"[WRLDZ] Phone AR: opening '{deviceName}' {width}x{height}@{fps}");

            WebCamTexture tex;
            try
            {
                tex = new WebCamTexture(deviceName, width, height, fps);
                tex.Play();
            }
            catch (Exception ex)
            {
                session.Status = "play failed: " + ex.Message;
                Debug.LogWarning("[WRLDZ] Phone AR: " + session.Status);
                yield break;
            }

            session.Texture = tex;

            // Wait until first frame or timeout (S23 can take a moment)
            var wait = 0f;
            const float maxWait = 6f;
            while (wait < maxWait)
            {
                if (tex.isPlaying && tex.width > 16)
                    break;
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!tex.isPlaying || tex.width <= 16)
            {
                session.Status = "camera did not start";
                Debug.LogWarning("[WRLDZ] Phone AR: camera failed to produce frames.");
                try
                {
                    tex.Stop();
                    UnityEngine.Object.Destroy(tex);
                }
                catch { /* ignore */ }
                session.Texture = null;
                yield break;
            }

            session.Live = true;
            session.Status = $"LIVE · {deviceName} · {tex.width}x{tex.height}";
            Debug.Log($"[WRLDZ] Phone AR LIVE · {deviceName} · {tex.width}x{tex.height} · rot={tex.videoRotationAngle}");
        }

        /// <summary>
        /// Apply Android/iOS webcam rotation + mirror to a world-space Quad.
        /// S23 reports videoRotationAngle (often 90) — without this the room is sideways.
        /// </summary>
        public static void ApplyOrientationToPlane(Transform plane, WebCamTexture tex, float baseWidth, float baseHeight)
        {
            if (plane == null || tex == null || !tex.isPlaying) return;

            var rot = tex.videoRotationAngle; // 0 / 90 / 180 / 270
            var mirror = tex.videoVerticallyMirrored;
            var tw = Mathf.Max(16, tex.width);
            var th = Mathf.Max(16, tex.height);
            // After rotation, effective aspect for the plane
            var landscape = (float)tw / th;
            float sx, sy;
            if (rot == 90 || rot == 270)
            {
                // Buffer is landscape but sensor is held portrait — plane taller
                sy = baseHeight;
                sx = sy / landscape;
            }
            else
            {
                sy = baseHeight;
                sx = sy * landscape;
            }

            // Keep roughly baseWidth footprint
            var scale = baseWidth / Mathf.Max(0.01f, Mathf.Abs(sx));
            sx *= scale * 0.85f;
            sy *= scale * 0.85f;
            if (mirror) sx = -Mathf.Abs(sx);
            else sx = Mathf.Abs(sx);

            plane.localScale = new Vector3(sx, sy, 1f);
            // Face stage camera (+ looking from +Z): Y 180, then sensor roll
            plane.localRotation = Quaternion.Euler(0f, 180f, -rot);
        }

        public static void Stop(Session session)
        {
            if (session?.Texture == null) return;
            try
            {
                if (session.Texture.isPlaying)
                    session.Texture.Stop();
                UnityEngine.Object.Destroy(session.Texture);
            }
            catch { /* ignore */ }
            session.Texture = null;
            session.Live = false;
            session.Status = "stopped";
        }
    }
}
