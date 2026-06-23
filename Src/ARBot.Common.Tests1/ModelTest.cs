using ARBot.Common.Algorithms.Statistic;
using ARBot.Common.Models;
using HAL;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.Windows.Media.Media3D;

namespace UnitTests
{
    [TestClass]
    public class ModelTest
    {
        [TestMethod]
        public void Test()
        {
            StateBase s = new StateBase(0.4);
            SimpleModel m = new SimpleModel(1, s.Rozchod, 0);

            ModelState ms = m.CurrentState.Clone();
            ms.Orientation = 0;
            ms.X = 0;
            ms.Y = 0;
            m.CurrentState = ms;

            s.TrackingCameraState = new IMUState() { Translation = new Vector3D(), Confidence = 1, Rotation = new Quaternion(new Vector3D(0, 1, 0), 0) };
            s.YPR = new YawPitchRoll(0, 0, 0);
            s.Motor = new MotorStateBase(false, 0, 0, 12, 0, 0);

            m.Update(s);

            Assert.AreEqual(0, m.CurrentState.X);
            Assert.AreEqual(0, m.CurrentState.Y);
            Assert.AreEqual(0, m.CurrentState.Orientation);


            s.TrackingCameraState = new IMUState() { Translation = new Vector3D(), Confidence = 1, Rotation = new Quaternion(new Vector3D(0, 1, 0), -18) };
            s.YPR = new YawPitchRoll(Math.PI / 10, 0, 0);
            s.Motor = new MotorStateBase(false, 0, 0, 12, 0, 0);

            m.Update(s);

            Assert.AreEqual(0, m.CurrentState.X);
            Assert.AreEqual(0, m.CurrentState.Y);
            Assert.AreEqual(-0.313, m.CurrentState.Orientation, 0.001);

        }
        [TestMethod]
        public void Test2()
        {
            double a = 1;
            double b = double.NaN;
            var c = a - b;


            StateBase s = new StateBase(0.4);
            EKFModel2 m = new EKFModel2(s, s.Rozchod);

            var ms = m.CreateState();
            ms.Orientation = 0;
            ms.X = 0;
            ms.Y = 0;
            m.Step.CurrentState = ms;

            s.TrackingCameraState = new IMUState() { Translation = new Vector3D(), Confidence = 1, Rotation = new Quaternion(new Vector3D(0, 1, 0), 0) };
            s.YPR = new YawPitchRoll(0, 0, 0);
            s.Motor = new MotorStateBase(false, 0, 0, 12, 0, 0);

            m.Update();

            Assert.AreEqual(0, m.Step.CurrentState.X);
            Assert.AreEqual(0, m.Step.CurrentState.Y);
            Assert.AreEqual(0, m.Step.CurrentState.Orientation);


            s.TrackingCameraState = new IMUState() { Translation = new Vector3D(), Confidence = 1, Rotation = new Quaternion(new Vector3D(0, 1, 0), -18) };
            s.YPR = new YawPitchRoll(Math.PI / 10, 0, 0);
            s.Motor = new MotorStateBase(false, 0, 0, 12, 0, 0);

            m.Update();

            Assert.AreEqual(0, m.Step.CurrentState.X);
            Assert.AreEqual(0, m.Step.CurrentState.Y);
            Assert.AreEqual(-0.313, m.Step.CurrentState.Orientation, 0.001);

        }
    }
}
