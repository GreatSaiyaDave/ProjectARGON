using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace WRLDZ.Presentation.ArInteraction
{
    /// <summary>One tracked hand, world space (metres on device).</summary>
    public struct ArHandPose
    {
        public bool Tracked;
        /// <summary>Grip (palm) pose, controller-grip convention.</summary>
        public Pose Grip;
        /// <summary>Pointing ray (far selection).</summary>
        public Pose Aim;
        /// <summary>Index fingertip (touch).</summary>
        public Pose Poke;
        /// <summary>Between the thumb and index tips (pinch).</summary>
        public Pose Pinch;
        /// <summary>0 = open, 1 = thumb and index closed.</summary>
        public float PinchValue;
        public bool PinchReady;
    }

    /// <summary>
    /// Real hands for mixed reality. The Spirit Dueler rides the LEFT forearm (the left hand's grip), the fan rides
    /// the RIGHT palm, and either hand selects: a fingertip over a card peeks it, a pinch takes it, a quick
    /// pinch-release taps it (the card menu), and a pinch carried to a disk zone plays it (ArCardDragSystem).
    /// Implemented by <see cref="XrWorldArmTracker"/> (OpenXR) and <see cref="SimulatedHandInput"/> (Editor tests).
    /// </summary>
    public interface IArHandInput
    {
        bool TryGetHand(bool left, out ArHandPose hand);
    }

    /// <summary>
    /// OpenXR Hand Interaction profile (XR_EXT_hand_interaction): hand tracking on Quest 3 and Android XR headsets
    /// and glasses, no extra package. Enable "Hand Interaction Profile" in Project Settings → XR Plug-in Management
    /// → OpenXR (Android). Poses come in tracking space; <paramref name="trackingSpace"/> is the XR Origin.
    /// </summary>
    public static class OpenXrHands
    {
        public static bool TryRead(bool left, Transform trackingSpace, out ArHandPose hand)
        {
            hand = default;
            HandInteractionProfile.HandInteraction dev;
            try
            {
                dev = InputSystem.GetDevice<HandInteractionProfile.HandInteraction>(
                    left ? UnityEngine.InputSystem.CommonUsages.LeftHand : UnityEngine.InputSystem.CommonUsages.RightHand);
            }
            catch (System.Exception)
            {
                return false;
            }

            if (dev == null || !dev.added || dev.isTracked == null || !dev.isTracked.isPressed) return false;
            Pose P(UnityEngine.InputSystem.Controls.Vector3Control p, UnityEngine.InputSystem.Controls.QuaternionControl r)
            {
                var lp = p != null ? p.ReadValue() : Vector3.zero;
                var lr = r != null ? r.ReadValue() : Quaternion.identity;
                if (lr.x == 0f && lr.y == 0f && lr.z == 0f && lr.w == 0f) lr = Quaternion.identity;
                return trackingSpace != null
                    ? new Pose(trackingSpace.TransformPoint(lp), trackingSpace.rotation * lr)
                    : new Pose(lp, lr);
            }

            hand.Tracked = true;
            hand.Grip = P(dev.devicePosition, dev.deviceRotation);
            hand.Aim = P(dev.pointerPosition, dev.pointerRotation);
            hand.Poke = P(dev.pokePosition, dev.pokeRotation);
            hand.Pinch = P(dev.pinchPosition, dev.pinchRotation);
            hand.PinchValue = dev.pinchValue != null ? dev.pinchValue.ReadValue() : 0f;
            hand.PinchReady = dev.pinchReady != null && dev.pinchReady.isPressed;
            return true;
        }
    }

    /// <summary>
    /// Hands set from code: Editor verification of the real-hand path (pinch, carry, flip, release) without a
    /// headset, and a stand-in for recorded or networked hand data.
    /// </summary>
    public sealed class SimulatedHandInput : IArHandInput
    {
        public ArHandPose Left;
        public ArHandPose Right;

        public bool TryGetHand(bool left, out ArHandPose hand)
        {
            hand = left ? Left : Right;
            return hand.Tracked;
        }
    }
}
