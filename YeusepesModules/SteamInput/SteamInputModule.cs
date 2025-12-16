// Copyright (c) VolcanicArts. Licensed under the GPL-3.0 License.
// See the LICENSE file in the repository root for full license text.

using VRCOSC.App.SDK.Modules;
using VRCOSC.App.SDK.Parameters;

namespace YeusepesModules.SteamInput;

[ModuleTitle("SteamVR Input")]
[ModuleDescription("Exports all SteamVR controller input data including stick/pad positions, trigger/grip values, and button states")]
[ModuleType(ModuleType.SteamVR)]
public class SteamInputModule : Module
{
    private float _leftPrevAngleRad = 0f;
    private float _rightPrevAngleRad = 0f;
    private bool _leftInitialized = false;
    private bool _rightInitialized = false;
    private float _leftPrevX = 0f;
    private float _leftPrevY = 0f;
    private float _rightPrevX = 0f;
    private float _rightPrevY = 0f;
    private float _leftPrevMagnitude = 0f;
    private float _rightPrevMagnitude = 0f;
    private bool _leftFlickWasSent = false;
    private bool _rightFlickWasSent = false;
    private int _leftFlickHoldFrames = 0;
    private int _rightFlickHoldFrames = 0;
    private int _leftFlickReturnFrames = 0;
    private int _rightFlickReturnFrames = 0;
    private float _leftAccumulatedRotationDelta = 0f;
    private float _rightAccumulatedRotationDelta = 0f;

    // Rotation tick + direction hold (prevents short pulses being missed downstream)
    private int _leftRotationHoldFrames = 0;
    private int _rightRotationHoldFrames = 0;
    private float _leftRotationHoldDirection = 0f;
    private float _rightRotationHoldDirection = 0f;
    private const int RotationHoldFrames = 3;

    // Flick tracking state (per-hand). Simple angle-based detection.
    private float _leftFlickLastAngle = 0f;
    private float _rightFlickLastAngle = 0f;
    private float _leftFlickLastX = 0f;
    private float _leftFlickLastY = 0f;
    private float _rightFlickLastX = 0f;
    private float _rightFlickLastY = 0f;
    private bool _leftFlickHadDirection = false;
    private bool _rightFlickHadDirection = false;

    protected override void OnPreLoad()
    {
        // Left Controller - Stick
        RegisterParameter<float>(SteamInputParameter.LeftStickX, "SteamInput/LHand/Stick/X", ParameterMode.Write, "Left Stick X", "Left stick X position (-1 to 1)");
        RegisterParameter<float>(SteamInputParameter.LeftStickY, "SteamInput/LHand/Stick/Y", ParameterMode.Write, "Left Stick Y", "Left stick Y position (-1 to 1)");
        RegisterParameter<float>(SteamInputParameter.LeftStickAngle, "SteamInput/LHand/Stick/Angle", ParameterMode.Write, "Left Stick Angle", "Left stick angle in degrees (0 to 360)");
        RegisterParameter<bool>(SteamInputParameter.LeftStickTouch, "SteamInput/LHand/Stick/Touch", ParameterMode.Write, "Left Stick Touch", "Whether the left stick is currently touched");
        RegisterParameter<bool>(SteamInputParameter.LeftStickClick, "SteamInput/LHand/Stick/Click", ParameterMode.Write, "Left Stick Click", "Whether the left stick is currently clicked");

        // Left Controller - Pad
        RegisterParameter<float>(SteamInputParameter.LeftPadX, "SteamInput/LHand/Pad/X", ParameterMode.Write, "Left Pad X", "Left pad X position (-1 to 1)");
        RegisterParameter<float>(SteamInputParameter.LeftPadY, "SteamInput/LHand/Pad/Y", ParameterMode.Write, "Left Pad Y", "Left pad Y position (-1 to 1)");
        RegisterParameter<bool>(SteamInputParameter.LeftPadTouch, "SteamInput/LHand/Pad/Touch", ParameterMode.Write, "Left Pad Touch", "Whether the left pad is currently touched");
        RegisterParameter<bool>(SteamInputParameter.LeftPadClick, "SteamInput/LHand/Pad/Click", ParameterMode.Write, "Left Pad Click", "Whether the left pad is currently clicked");

        // Left Controller - Trigger
        RegisterParameter<float>(SteamInputParameter.LeftTriggerPull, "SteamInput/LHand/Trigger/Pull", ParameterMode.Write, "Left Trigger Pull", "Left trigger pull value (0 to 1)");
        RegisterParameter<bool>(SteamInputParameter.LeftTriggerTouch, "SteamInput/LHand/Trigger/Touch", ParameterMode.Write, "Left Trigger Touch", "Whether the left trigger is currently touched");
        RegisterParameter<bool>(SteamInputParameter.LeftTriggerClick, "SteamInput/LHand/Trigger/Click", ParameterMode.Write, "Left Trigger Click", "Whether the left trigger is currently clicked");

        // Left Controller - Grip
        RegisterParameter<float>(SteamInputParameter.LeftGripPull, "SteamInput/LHand/Grip/Pull", ParameterMode.Write, "Left Grip Pull", "Left grip pull value (0 to 1)");
        RegisterParameter<bool>(SteamInputParameter.LeftGripClick, "SteamInput/LHand/Grip/Click", ParameterMode.Write, "Left Grip Click", "Whether the left grip is currently clicked");

        // Left Controller - Buttons
        RegisterParameter<bool>(SteamInputParameter.LeftPrimaryTouch, "SteamInput/LHand/Primary/Touch", ParameterMode.Write, "Left Primary Touch", "Whether the left primary button is currently touched");
        RegisterParameter<bool>(SteamInputParameter.LeftPrimaryClick, "SteamInput/LHand/Primary/Click", ParameterMode.Write, "Left Primary Click", "Whether the left primary button is currently clicked");
        RegisterParameter<bool>(SteamInputParameter.LeftSecondaryTouch, "SteamInput/LHand/Secondary/Touch", ParameterMode.Write, "Left Secondary Touch", "Whether the left secondary button is currently touched");
        RegisterParameter<bool>(SteamInputParameter.LeftSecondaryClick, "SteamInput/LHand/Secondary/Click", ParameterMode.Write, "Left Secondary Click", "Whether the left secondary button is currently clicked");

        // Left Controller - Skeleton (Finger Curl)
        RegisterParameter<float>(SteamInputParameter.LeftFingerIndex, "SteamInput/LHand/Finger/Index", ParameterMode.Write, "Left Index Finger", "Left index finger curl value (0 to 1)");
        RegisterParameter<float>(SteamInputParameter.LeftFingerMiddle, "SteamInput/LHand/Finger/Middle", ParameterMode.Write, "Left Middle Finger", "Left middle finger curl value (0 to 1)");
        RegisterParameter<float>(SteamInputParameter.LeftFingerRing, "SteamInput/LHand/Finger/Ring", ParameterMode.Write, "Left Ring Finger", "Left ring finger curl value (0 to 1)");
        RegisterParameter<float>(SteamInputParameter.LeftFingerPinky, "SteamInput/LHand/Finger/Pinky", ParameterMode.Write, "Left Pinky Finger", "Left pinky finger curl value (0 to 1)");

        // Left Controller - Rotation Detection
        RegisterParameter<float>(SteamInputParameter.LeftStickRotationDirection, "SteamInput/LHand/Stick/RotationDirection", ParameterMode.Write, "Left Stick Rotation Direction", "Rotation direction: 0 (no rotation), 1 (clockwise), -1 (counter-clockwise)");
        RegisterParameter<bool>(SteamInputParameter.LeftStickRotationTick, "SteamInput/LHand/Stick/RotationTick", ParameterMode.Write, "Left Stick Rotation Tick", "Triggered when a rotation step is detected");
        RegisterParameter<bool>(SteamInputParameter.LeftStickFlick, "SteamInput/LHand/Stick/Flick", ParameterMode.Write, "Left Stick Flick", "Triggered when a flick is detected");

        // Right Controller - Stick
        RegisterParameter<float>(SteamInputParameter.RightStickX, "SteamInput/RHand/Stick/X", ParameterMode.Write, "Right Stick X", "Right stick X position (-1 to 1)");
        RegisterParameter<float>(SteamInputParameter.RightStickY, "SteamInput/RHand/Stick/Y", ParameterMode.Write, "Right Stick Y", "Right stick Y position (-1 to 1)");
        RegisterParameter<float>(SteamInputParameter.RightStickAngle, "SteamInput/RHand/Stick/Angle", ParameterMode.Write, "Right Stick Angle", "Right stick angle in degrees (0 to 360)");
        RegisterParameter<bool>(SteamInputParameter.RightStickTouch, "SteamInput/RHand/Stick/Touch", ParameterMode.Write, "Right Stick Touch", "Whether the right stick is currently touched");
        RegisterParameter<bool>(SteamInputParameter.RightStickClick, "SteamInput/RHand/Stick/Click", ParameterMode.Write, "Right Stick Click", "Whether the right stick is currently clicked");

        // Right Controller - Pad
        RegisterParameter<float>(SteamInputParameter.RightPadX, "SteamInput/RHand/Pad/X", ParameterMode.Write, "Right Pad X", "Right pad X position (-1 to 1)");
        RegisterParameter<float>(SteamInputParameter.RightPadY, "SteamInput/RHand/Pad/Y", ParameterMode.Write, "Right Pad Y", "Right pad Y position (-1 to 1)");
        RegisterParameter<bool>(SteamInputParameter.RightPadTouch, "SteamInput/RHand/Pad/Touch", ParameterMode.Write, "Right Pad Touch", "Whether the right pad is currently touched");
        RegisterParameter<bool>(SteamInputParameter.RightPadClick, "SteamInput/RHand/Pad/Click", ParameterMode.Write, "Right Pad Click", "Whether the right pad is currently clicked");

        // Right Controller - Trigger
        RegisterParameter<float>(SteamInputParameter.RightTriggerPull, "SteamInput/RHand/Trigger/Pull", ParameterMode.Write, "Right Trigger Pull", "Right trigger pull value (0 to 1)");
        RegisterParameter<bool>(SteamInputParameter.RightTriggerTouch, "SteamInput/RHand/Trigger/Touch", ParameterMode.Write, "Right Trigger Touch", "Whether the right trigger is currently touched");
        RegisterParameter<bool>(SteamInputParameter.RightTriggerClick, "SteamInput/RHand/Trigger/Click", ParameterMode.Write, "Right Trigger Click", "Whether the right trigger is currently clicked");

        // Right Controller - Grip
        RegisterParameter<float>(SteamInputParameter.RightGripPull, "SteamInput/RHand/Grip/Pull", ParameterMode.Write, "Right Grip Pull", "Right grip pull value (0 to 1)");
        RegisterParameter<bool>(SteamInputParameter.RightGripClick, "SteamInput/RHand/Grip/Click", ParameterMode.Write, "Right Grip Click", "Whether the right grip is currently clicked");

        // Right Controller - Buttons
        RegisterParameter<bool>(SteamInputParameter.RightPrimaryTouch, "SteamInput/RHand/Primary/Touch", ParameterMode.Write, "Right Primary Touch", "Whether the right primary button is currently touched");
        RegisterParameter<bool>(SteamInputParameter.RightPrimaryClick, "SteamInput/RHand/Primary/Click", ParameterMode.Write, "Right Primary Click", "Whether the right primary button is currently clicked");
        RegisterParameter<bool>(SteamInputParameter.RightSecondaryTouch, "SteamInput/RHand/Secondary/Touch", ParameterMode.Write, "Right Secondary Touch", "Whether the right secondary button is currently touched");
        RegisterParameter<bool>(SteamInputParameter.RightSecondaryClick, "SteamInput/RHand/Secondary/Click", ParameterMode.Write, "Right Secondary Click", "Whether the right secondary button is currently clicked");

        // Right Controller - Skeleton (Finger Curl)
        RegisterParameter<float>(SteamInputParameter.RightFingerIndex, "SteamInput/RHand/Finger/Index", ParameterMode.Write, "Right Index Finger", "Right index finger curl value (0 to 1)");
        RegisterParameter<float>(SteamInputParameter.RightFingerMiddle, "SteamInput/RHand/Finger/Middle", ParameterMode.Write, "Right Middle Finger", "Right middle finger curl value (0 to 1)");
        RegisterParameter<float>(SteamInputParameter.RightFingerRing, "SteamInput/RHand/Finger/Ring", ParameterMode.Write, "Right Ring Finger", "Right ring finger curl value (0 to 1)");
        RegisterParameter<float>(SteamInputParameter.RightFingerPinky, "SteamInput/RHand/Finger/Pinky", ParameterMode.Write, "Right Pinky Finger", "Right pinky finger curl value (0 to 1)");

        // Right Controller - Rotation Detection
        RegisterParameter<float>(SteamInputParameter.RightStickRotationDirection, "SteamInput/RHand/Stick/RotationDirection", ParameterMode.Write, "Right Stick Rotation Direction", "Rotation direction: 0 (no rotation), 1 (clockwise), -1 (counter-clockwise)");
        RegisterParameter<bool>(SteamInputParameter.RightStickRotationTick, "SteamInput/RHand/Stick/RotationTick", ParameterMode.Write, "Right Stick Rotation Tick", "Triggered when a rotation step is detected");
        RegisterParameter<bool>(SteamInputParameter.RightStickFlick, "SteamInput/RHand/Stick/Flick", ParameterMode.Write, "Right Stick Flick", "Triggered when a flick is detected");
    }

    // Higher update rate improves responsiveness for angle-delta based features (rotation direction / flick).
    // VRCOSC custom update intervals are in milliseconds.
    [ModuleUpdate(ModuleUpdateMode.Custom, true, 1000f / 260f)]
    private void updateParameters()
    {
        var manager = GetOpenVRManager();
        
        if (!manager.Initialised) return;

        var leftController = manager.GetLeftController();
        var rightController = manager.GetRightController();

        if (leftController is not null && leftController.IsConnected)
        {
            var leftInput = leftController.Input;

            // Left Stick
            float leftX = leftInput.Stick.Position.X;
            float leftY = leftInput.Stick.Position.Y;
            SendParameter(SteamInputParameter.LeftStickX, leftX);
            SendParameter(SteamInputParameter.LeftStickY, leftY);
            var leftAngle = (float)(System.Math.Atan2(leftY, leftX) * 180.0 / System.Math.PI);
            if (leftAngle < 0) leftAngle += 360f;
            SendParameter(SteamInputParameter.LeftStickAngle, leftAngle);
            SendParameter(SteamInputParameter.LeftStickTouch, leftInput.Stick.Touch);
            SendParameter(SteamInputParameter.LeftStickClick, leftInput.Stick.Click);

            // Left Stick - Rotation Detection (pulse + tick)
            float leftPulseDirection = CalculateRotationDirection(leftX, leftY, ref _leftPrevAngleRad, ref _leftInitialized, ref _leftAccumulatedRotationDelta);
            if (leftPulseDirection != 0f)
            {
                _leftRotationHoldFrames = RotationHoldFrames;
                _leftRotationHoldDirection = leftPulseDirection;
            }

            if (_leftRotationHoldFrames > 0)
            {
                SendParameter(SteamInputParameter.LeftStickRotationDirection, _leftRotationHoldDirection);
                SendParameter(SteamInputParameter.LeftStickRotationTick, true);
                _leftRotationHoldFrames--;
                if (_leftRotationHoldFrames == 0)
                {
                    SendParameter(SteamInputParameter.LeftStickRotationDirection, 0f);
                    SendParameter(SteamInputParameter.LeftStickRotationTick, false);
                }
            }
            else
            {
                SendParameter(SteamInputParameter.LeftStickRotationDirection, 0f);
                SendParameter(SteamInputParameter.LeftStickRotationTick, false);
            }

            // Left Stick - Flick Detection
            float leftMagnitude = (float)System.Math.Sqrt(leftX * leftX + leftY * leftY);
            bool leftFlick = DetectFlick(
                leftX, leftY,
                _leftPrevX, _leftPrevY,
                _leftPrevMagnitude,
                ref _leftFlickLastAngle,
                ref _leftFlickLastX,
                ref _leftFlickLastY,
                ref _leftFlickHadDirection,
                ref _leftFlickReturnFrames,
                "Left"
            );
            
            // Handle flick parameter with hold frames
            if (leftFlick)
            {
                LogDebug($"[Left] FLICK DETECTED: Starting hold period");
                _leftFlickHoldFrames = 3; // Hold for 3 frames
                _leftFlickWasSent = true;
            }
            
            if (_leftFlickHoldFrames > 0)
            {
                LogDebug($"[Left] SENDING FLICK PARAMETER: true (hold frames remaining: {_leftFlickHoldFrames})");
                SendParameter(SteamInputParameter.LeftStickFlick, true);
                _leftFlickHoldFrames--;
                if (_leftFlickHoldFrames == 0)
                {
                    LogDebug($"[Left] SENDING FLICK PARAMETER: false (hold period ended)");
                    SendParameter(SteamInputParameter.LeftStickFlick, false);
                    _leftFlickWasSent = false;
                }
            }
            _leftPrevX = leftX;
            _leftPrevY = leftY;
            _leftPrevMagnitude = leftMagnitude;

            // Left Pad
            SendParameter(SteamInputParameter.LeftPadX, leftInput.Pad.Position.X);
            SendParameter(SteamInputParameter.LeftPadY, leftInput.Pad.Position.Y);
            SendParameter(SteamInputParameter.LeftPadTouch, leftInput.Pad.Touch);
            SendParameter(SteamInputParameter.LeftPadClick, leftInput.Pad.Click);

            // Left Trigger
            SendParameter(SteamInputParameter.LeftTriggerPull, leftInput.Trigger.Pull);
            SendParameter(SteamInputParameter.LeftTriggerTouch, leftInput.Trigger.Touch);
            SendParameter(SteamInputParameter.LeftTriggerClick, leftInput.Trigger.Click);

            // Left Grip
            SendParameter(SteamInputParameter.LeftGripPull, leftInput.Grip.Pull);
            SendParameter(SteamInputParameter.LeftGripClick, leftInput.Grip.Click);

            // Left Buttons
            SendParameter(SteamInputParameter.LeftPrimaryTouch, leftInput.Primary.Touch);
            SendParameter(SteamInputParameter.LeftPrimaryClick, leftInput.Primary.Click);
            SendParameter(SteamInputParameter.LeftSecondaryTouch, leftInput.Secondary.Touch);
            SendParameter(SteamInputParameter.LeftSecondaryClick, leftInput.Secondary.Click);

            // Left Skeleton
            SendParameter(SteamInputParameter.LeftFingerIndex, leftInput.Skeleton.Index);
            SendParameter(SteamInputParameter.LeftFingerMiddle, leftInput.Skeleton.Middle);
            SendParameter(SteamInputParameter.LeftFingerRing, leftInput.Skeleton.Ring);
            SendParameter(SteamInputParameter.LeftFingerPinky, leftInput.Skeleton.Pinky);
        }

        if (rightController is not null && rightController.IsConnected)
        {
            var rightInput = rightController.Input;

            // Right Stick
            float rightX = rightInput.Stick.Position.X;
            float rightY = rightInput.Stick.Position.Y;
            SendParameter(SteamInputParameter.RightStickX, rightX);
            SendParameter(SteamInputParameter.RightStickY, rightY);
            var rightAngle = (float)(System.Math.Atan2(rightY, rightX) * 180.0 / System.Math.PI);
            if (rightAngle < 0) rightAngle += 360f;
            SendParameter(SteamInputParameter.RightStickAngle, rightAngle);
            SendParameter(SteamInputParameter.RightStickTouch, rightInput.Stick.Touch);
            SendParameter(SteamInputParameter.RightStickClick, rightInput.Stick.Click);

            // Right Stick - Rotation Detection (pulse + tick)
            float rightPulseDirection = CalculateRotationDirection(rightX, rightY, ref _rightPrevAngleRad, ref _rightInitialized, ref _rightAccumulatedRotationDelta);
            if (rightPulseDirection != 0f)
            {
                _rightRotationHoldFrames = RotationHoldFrames;
                _rightRotationHoldDirection = rightPulseDirection;
            }

            if (_rightRotationHoldFrames > 0)
            {
                SendParameter(SteamInputParameter.RightStickRotationDirection, _rightRotationHoldDirection);
                SendParameter(SteamInputParameter.RightStickRotationTick, true);
                _rightRotationHoldFrames--;
                if (_rightRotationHoldFrames == 0)
                {
                    SendParameter(SteamInputParameter.RightStickRotationDirection, 0f);
                    SendParameter(SteamInputParameter.RightStickRotationTick, false);
                }
            }
            else
            {
                SendParameter(SteamInputParameter.RightStickRotationDirection, 0f);
                SendParameter(SteamInputParameter.RightStickRotationTick, false);
            }

            // Right Stick - Flick Detection
            float rightMagnitude = (float)System.Math.Sqrt(rightX * rightX + rightY * rightY);
            bool rightFlick = DetectFlick(
                rightX, rightY,
                _rightPrevX, _rightPrevY,
                _rightPrevMagnitude,
                ref _rightFlickLastAngle,
                ref _rightFlickLastX,
                ref _rightFlickLastY,
                ref _rightFlickHadDirection,
                ref _rightFlickReturnFrames,
                "Right"
            );
            
            // Handle flick parameter with hold frames
            if (rightFlick)
            {
                LogDebug($"[Right] FLICK DETECTED: Starting hold period");
                _rightFlickHoldFrames = 3; // Hold for 3 frames
                _rightFlickWasSent = true;
            }
            
            if (_rightFlickHoldFrames > 0)
            {
                LogDebug($"[Right] SENDING FLICK PARAMETER: true (hold frames remaining: {_rightFlickHoldFrames})");
                SendParameter(SteamInputParameter.RightStickFlick, true);
                _rightFlickHoldFrames--;
                if (_rightFlickHoldFrames == 0)
                {
                    LogDebug($"[Right] SENDING FLICK PARAMETER: false (hold period ended)");
                    SendParameter(SteamInputParameter.RightStickFlick, false);
                    _rightFlickWasSent = false;
                }
            }
            _rightPrevX = rightX;
            _rightPrevY = rightY;
            _rightPrevMagnitude = rightMagnitude;

            // Right Pad
            SendParameter(SteamInputParameter.RightPadX, rightInput.Pad.Position.X);
            SendParameter(SteamInputParameter.RightPadY, rightInput.Pad.Position.Y);
            SendParameter(SteamInputParameter.RightPadTouch, rightInput.Pad.Touch);
            SendParameter(SteamInputParameter.RightPadClick, rightInput.Pad.Click);

            // Right Trigger
            SendParameter(SteamInputParameter.RightTriggerPull, rightInput.Trigger.Pull);
            SendParameter(SteamInputParameter.RightTriggerTouch, rightInput.Trigger.Touch);
            SendParameter(SteamInputParameter.RightTriggerClick, rightInput.Trigger.Click);

            // Right Grip
            SendParameter(SteamInputParameter.RightGripPull, rightInput.Grip.Pull);
            SendParameter(SteamInputParameter.RightGripClick, rightInput.Grip.Click);

            // Right Buttons
            SendParameter(SteamInputParameter.RightPrimaryTouch, rightInput.Primary.Touch);
            SendParameter(SteamInputParameter.RightPrimaryClick, rightInput.Primary.Click);
            SendParameter(SteamInputParameter.RightSecondaryTouch, rightInput.Secondary.Touch);
            SendParameter(SteamInputParameter.RightSecondaryClick, rightInput.Secondary.Click);

            // Right Skeleton
            SendParameter(SteamInputParameter.RightFingerIndex, rightInput.Skeleton.Index);
            SendParameter(SteamInputParameter.RightFingerMiddle, rightInput.Skeleton.Middle);
            SendParameter(SteamInputParameter.RightFingerRing, rightInput.Skeleton.Ring);
            SendParameter(SteamInputParameter.RightFingerPinky, rightInput.Skeleton.Pinky);
        }
    }

    private enum SteamInputParameter
    {
        // Left Controller
        LeftStickX,
        LeftStickY,
        LeftStickAngle,
        LeftStickTouch,
        LeftStickClick,
        LeftPadX,
        LeftPadY,
        LeftPadTouch,
        LeftPadClick,
        LeftTriggerPull,
        LeftTriggerTouch,
        LeftTriggerClick,
        LeftGripPull,
        LeftGripClick,
        LeftPrimaryTouch,
        LeftPrimaryClick,
        LeftSecondaryTouch,
        LeftSecondaryClick,
        LeftFingerIndex,
        LeftFingerMiddle,
        LeftFingerRing,
        LeftFingerPinky,
        LeftStickRotationDirection,
        LeftStickRotationTick,
        LeftStickFlick,

        // Right Controller
        RightStickX,
        RightStickY,
        RightStickAngle,
        RightStickTouch,
        RightStickClick,
        RightPadX,
        RightPadY,
        RightPadTouch,
        RightPadClick,
        RightTriggerPull,
        RightTriggerTouch,
        RightTriggerClick,
        RightGripPull,
        RightGripClick,
        RightPrimaryTouch,
        RightPrimaryClick,
        RightSecondaryTouch,
        RightSecondaryClick,
        RightFingerIndex,
        RightFingerMiddle,
        RightFingerRing,
        RightFingerPinky,
        RightStickRotationDirection,
        RightStickRotationTick,
        RightStickFlick
    }

    private float CalculateRotationDirection(float currentX, float currentY, ref float prevAngleRad, ref bool initialized, ref float accumulatedDelta, float deadzone = 0.2f, float minDeltaRad = 0.001f, float pulseThresholdRad = 0.02f)
    {
        float r = (float)System.Math.Sqrt(currentX * currentX + currentY * currentY);
        
        if (r < deadzone)
        {
            initialized = false;
            accumulatedDelta = 0f;
            return 0f;
        }
        
        float angle = (float)System.Math.Atan2(currentY, currentX);
        
        if (!initialized)
        {
            prevAngleRad = angle;
            initialized = true;
            accumulatedDelta = 0f;
            return 0f;
        }
        
        float delta = angle - prevAngleRad;
        
        if (delta > System.Math.PI)
        {
            delta -= (float)(2.0 * System.Math.PI);
        }
        else if (delta < -System.Math.PI)
        {
            delta += (float)(2.0 * System.Math.PI);
        }
        
        prevAngleRad = angle;
        
        // Ignore extremely small deltas (noise filtering)
        if (System.Math.Abs(delta) < minDeltaRad)
        {
            return 0f;
        }
        
        // Accumulate delta
        accumulatedDelta += delta;
        
        // Send pulse when accumulated delta exceeds threshold
        if (System.Math.Abs(accumulatedDelta) >= pulseThresholdRad)
        {
            float result = accumulatedDelta > 0 ? -1f : 1f;
            accumulatedDelta = 0f; // Reset accumulator
            return result;
        }
        
        return 0f;
    }

    private bool DetectFlick(
        float currentX,
        float currentY,
        float prevX,
        float prevY,
        float prevMagnitude,
        ref float flickLastAngle,
        ref float flickLastX,
        ref float flickLastY,
        ref bool flickHadDirection,
        ref int flickReturnFrames,
        string handName,
        float pushThreshold = 0.5f,
        float centerThreshold = 0.15f,
        float axisTolerance = 0.7f,
        int maxReturnFrames = 6
    )
    {
        float r = (float)System.Math.Sqrt(currentX * currentX + currentY * currentY);
        float prevR = prevMagnitude;
        
        // If stick is pushed out (magnitude > threshold), record the direction ONCE
        // Only record if we don't already have a direction (first time crossing threshold)
        if (r > pushThreshold && !flickHadDirection)
        {
            float angle = (float)System.Math.Atan2(currentY, currentX);
            float angleDeg = angle * 180f / (float)System.Math.PI;
            flickLastAngle = angle;
            flickLastX = currentX;
            flickLastY = currentY;
            flickHadDirection = true;
            flickReturnFrames = 0;
            LogDebug($"[{handName}] FLICK CANDIDATE: Direction recorded - X={currentX:F3}, Y={currentY:F3}, Angle={angleDeg:F1}°, Magnitude={r:F3}");
            return false;
        }
        
        // Debug: Log current state periodically when tracking (but not recording new direction)
        if (flickHadDirection && r > centerThreshold)
        {
            LogDebug($"[{handName}] FLICK TRACKING: Waiting for return to center - Current(X={currentX:F3}, Y={currentY:F3}, R={r:F3}), LastRecorded(X={flickLastX:F3}, Y={flickLastY:F3})");
        }
        
        // If we had a direction and stick is now at center, check for flick
        if (flickHadDirection && r < centerThreshold)
        {
            // Previous frame had direction, current frame is at center
            // Check if previous frame was also at center (already centered, don't trigger)
            if (prevR < centerThreshold)
            {
                // Already at center, reset
                LogDebug($"[{handName}] FLICK REJECTED: Already at center (prevR={prevR:F3}, currentR={r:F3})");
                flickHadDirection = false;
                flickReturnFrames = 0;
                return false;
            }
            
            // Previous was pushed out, now at center - check if it was a valid flick
            bool isValidFlick = false;
            string rejectionReason = "";
            
            // Check if it was a vertical flick (up or down)
            // Vertical means: -0.7 < x < 0.7 (mostly vertical, not diagonal)
            bool isVertical = System.Math.Abs(flickLastX) < axisTolerance;
            
            // Check if it was a horizontal flick (left or right)
            // Horizontal means: -0.7 < y < 0.7 (mostly horizontal, not diagonal)
            bool isHorizontal = System.Math.Abs(flickLastY) < axisTolerance;
            
            if (!isVertical && !isHorizontal)
            {
                rejectionReason = $"Diagonal movement (X={flickLastX:F3}, Y={flickLastY:F3}) - |X|={System.Math.Abs(flickLastX):F3} >= {axisTolerance} AND |Y|={System.Math.Abs(flickLastY):F3} >= {axisTolerance}";
            }
            
            // Valid flick if it was either vertical OR horizontal (not diagonal)
            if (isVertical || isHorizontal)
            {
                isValidFlick = true;
                string direction = isVertical ? "VERTICAL" : "HORIZONTAL";
                LogDebug($"[{handName}] FLICK TRIGGERED: {direction} - LastPos(X={flickLastX:F3}, Y={flickLastY:F3}), CurrentR={r:F3}, PrevR={prevR:F3}");
            }
            else
            {
                LogDebug($"[{handName}] FLICK REJECTED: {rejectionReason}");
            }
            
            // Reset state
            flickHadDirection = false;
            flickReturnFrames = 0;
            
            return isValidFlick;
        }
        
        // Return-to-center window:
        // Once you leave the "pushed" zone (r <= pushThreshold), you must reach center within N frames.
        // This avoids false cancels caused by higher update rates capturing intermediate radii.
        if (flickHadDirection && r <= pushThreshold)
        {
            if (r > centerThreshold)
            {
                flickReturnFrames++;
                LogDebug($"[{handName}] FLICK RETURNING: r={r:F3} (frame {flickReturnFrames}/{maxReturnFrames})");

                if (flickReturnFrames > maxReturnFrames)
                {
                    LogDebug($"[{handName}] FLICK CANCELLED: Did not return to center fast enough (last r={r:F3})");
                    flickHadDirection = false;
                    flickReturnFrames = 0;
                }
            }
            else
            {
                // Center case is handled above; keep state consistent.
                flickReturnFrames = 0;
            }
        }
        else if (flickHadDirection && r > pushThreshold)
        {
            // Still fully pushed: reset return timer.
            flickReturnFrames = 0;
        }
        
        return false;
    }
}

