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
    }

    [ModuleUpdate(ModuleUpdateMode.Custom, true, 1000f / 60f)]
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
            SendParameter(SteamInputParameter.LeftStickX, leftInput.Stick.Position.X);
            SendParameter(SteamInputParameter.LeftStickY, leftInput.Stick.Position.Y);
            var leftAngle = (float)(System.Math.Atan2(leftInput.Stick.Position.Y, leftInput.Stick.Position.X) * 180.0 / System.Math.PI);
            if (leftAngle < 0) leftAngle += 360f;
            SendParameter(SteamInputParameter.LeftStickAngle, leftAngle);
            SendParameter(SteamInputParameter.LeftStickTouch, leftInput.Stick.Touch);
            SendParameter(SteamInputParameter.LeftStickClick, leftInput.Stick.Click);

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
            SendParameter(SteamInputParameter.RightStickX, rightInput.Stick.Position.X);
            SendParameter(SteamInputParameter.RightStickY, rightInput.Stick.Position.Y);
            var rightAngle = (float)(System.Math.Atan2(rightInput.Stick.Position.Y, rightInput.Stick.Position.X) * 180.0 / System.Math.PI);
            if (rightAngle < 0) rightAngle += 360f;
            SendParameter(SteamInputParameter.RightStickAngle, rightAngle);
            SendParameter(SteamInputParameter.RightStickTouch, rightInput.Stick.Touch);
            SendParameter(SteamInputParameter.RightStickClick, rightInput.Stick.Click);

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
        RightFingerPinky
    }
}

