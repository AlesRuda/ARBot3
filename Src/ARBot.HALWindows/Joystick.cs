using ARBot.HAL;
using SharpDX.DirectInput;

using SharpDX.XInput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HALWindows
{
    public class Joystick : IJoystick
    {

        Controller controller;
        DirectInput directInput = new DirectInput();
        Guid joystickGuid = Guid.Empty;
        SharpDX.DirectInput.Joystick joystick;

        public Joystick()
        {
            if(!ConnectXBox())
                Connect(directInput.GetDevices(SharpDX.DirectInput.DeviceType.Gamepad, DeviceEnumerationFlags.AllDevices).FirstOrDefault()?.InstanceGuid ?? joystickGuid);
        }

        public bool ConnectXBox()
        {
            controller = new Controller(UserIndex.One);
            if(!controller.IsConnected)
            {
                controller = null;
                return false;
            }
            return true;
        }

        public void Connect(Guid guid)
        {
            joystickGuid = guid;
            if (joystickGuid != Guid.Empty)
            {
                Debug.WriteLine("Found Joystick/Gamepad with GUID: {0}", joystickGuid);

                joystick = new SharpDX.DirectInput.Joystick(directInput, joystickGuid);
                joystick.Properties.BufferSize = 128;

                foreach (DeviceObjectInstance doi in joystick.GetObjects(DeviceObjectTypeFlags.Axis))
                {
                    joystick.GetObjectPropertiesById(doi.ObjectId).Range=new InputRange(-1000, 1000);
                }

                joystick.Acquire();
            }
            else
                Debug.WriteLine("Joystick/Gamepad not found");
        }

        public double RotationVelocity { get; set ; }
        public double ForwardVelocity { get; set ; }
        public bool Button1 { get; set; }
        public bool Button2 { get; set; }
        public bool Button3 { get; set; }
        public bool Button4 { get; set; }

        public void Read()
        {
            if(controller!=null)
            {
                var gamepad = controller.GetState().Gamepad;

                ForwardVelocity = ((double)gamepad.LeftTrigger) / byte.MaxValue;
                RotationVelocity = ((double)gamepad.LeftThumbX) / short.MaxValue;

                Button1 = (gamepad.Buttons & GamepadButtonFlags.A)== GamepadButtonFlags.A;
                Button2 = (gamepad.Buttons & GamepadButtonFlags.B) == GamepadButtonFlags.B;
                Button3 = (gamepad.Buttons & GamepadButtonFlags.X) == GamepadButtonFlags.X;
                Button4 = (gamepad.Buttons & GamepadButtonFlags.Y) == GamepadButtonFlags.Y;

//                Debug.WriteLine(string.Format("Buttons={0}", gamepad.Buttons));

            }
            else if (joystick != null)
            {
                joystick.Poll();

                var datas = joystick.GetBufferedData();

                if (datas != null)
                {
//                    Debug.WriteLine(string.Format("Data.Count={0}", datas.Length));
                    foreach (var s in datas)
                    {
                        if (s.Offset == JoystickOffset.Z)
                            ForwardVelocity = s.Value / 1000.0;
                        else if (s.Offset == JoystickOffset.X)
                            RotationVelocity = s.Value / 1000.0;
                        else if (s.Offset == JoystickOffset.Buttons0)
                            Button1 = s.Value != 0;
                        else if (s.Offset == JoystickOffset.Buttons1)
                            Button2 = s.Value != 0;
                        else if (s.Offset == JoystickOffset.Buttons2)
                            Button3 = s.Value != 0;
                        else if (s.Offset == JoystickOffset.Buttons3)
                            Button4 = s.Value != 0;
                    }
                }
            }
        }
        public override string ToString()
        {
            return string.Format(@"Rotation: {0:N2}, Forward: {1:N2}, Button1: {2}, Button2: {3}, Button3: {4}, Button4: {5}", RotationVelocity, ForwardVelocity, Button1, Button2, Button3, Button4);
        }
    }
}
