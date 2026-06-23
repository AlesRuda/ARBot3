using ARBot.Common.Algorithms.Statistic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;

namespace UnitTests
{
    [TestClass]
    public class StatisticsTest
    {
        [TestMethod]
        public void MovingStatTest()
        {
            AggregateStat ms = new AggregateStat();
            ms.Add(1);
            Assert.AreEqual(1, ms.Mean);
            Assert.AreEqual(0, ms.STD);
            ms.Add(1);
            Assert.AreEqual(1, ms.Mean);
            Assert.AreEqual(0, ms.STD);
            ms.Add(4);
            Assert.AreEqual(2, ms.Mean);
            Assert.AreEqual(3, ms.Variance);
            ms.Remove(1);
            Assert.AreEqual(2.5, ms.Mean);
            Assert.AreEqual(4.5, ms.Variance);
        }
        [TestMethod]
        public void StatisticDataFusorTest()
        {
            var f = new StatisticDataFusor();
            Assert.AreEqual(2, f.Fusion(2));
            Assert.AreEqual(1, f.Fusion(1, 1, 1));
            Assert.AreEqual(1.5, f.Fusion(1, 2));
            Assert.AreEqual(2.5, f.Fusion(1, 1, 10));
        }
        [TestMethod]
        public void MedianDataFusorTest()
        {
            var f = new MedianDataFusor();
            Assert.AreEqual(2, f.Fusion(2));
            Assert.AreEqual(1, f.Fusion(1, 1, 1));
            Assert.AreEqual(1.5, f.Fusion(1, 2));
            Assert.AreEqual(2, f.Fusion(1, 2, 10));
            Assert.AreEqual(2.5, f.Fusion(1, 2, 3, 10));
        }
    }
}
