﻿using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace TimeoutTestProject
{

    [TestClass]
    public class UnitTest2
    {
        private static readonly Random rng = new Random();

        public TestContext TestContext { get; set; }

        [ClassInitialize()]
        public static void ClassInitialize(TestContext _)
        {
            WriteLines("UnitTest2 - ClassInitialize");
        }

        [TestInitialize]
        public void TestInitialize()
        {
            WriteLines("UnitTest2 - TestInitialize");
        }

        [TestCleanup]
        public void TestCleanup()
        {
            WriteLines("UnitTest2 - TestCleanup");
        }


        [ClassCleanup()]
        public static void ClassCleanup()
        {
            WriteLines($"UnitTest2 - ClassCleanup");
        }

        [TestMethod]
        public async Task TestMethod1()
        {
            WriteLines("UnitTest2 - TestMethod1");
            await Task.Delay(rng.Next(0,3));
            WriteLines("UnitTest2 - TestMethod1");
            await Task.Delay(rng.Next(0, 3));
            WriteLines("UnitTest2 - TestMethod1");
        }

        [TestMethod]
        public async Task TestMethod2()
        {
            WriteLines("UnitTest2 - TestMethod2");
            await Task.Delay(rng.Next(0, 3));
            WriteLines("UnitTest2 - TestMethod2");
            await Task.Delay(rng.Next(0, 3));
            WriteLines("UnitTest2 - TestMethod2");
        }

        [TestMethod]
        public async Task TestMethod3()
        {
            WriteLines("UnitTest2 - TestMethod3");
            await Task.Delay(rng.Next(0, 3));
            WriteLines("UnitTest2 - TestMethod3");
            await Task.Delay(rng.Next(0, 3));
            WriteLines("UnitTest2 - TestMethod3");
        }

        private static void WriteLines(string message)
        {
            Debug.WriteLine(message);
            Console.WriteLine(message);
            Console.Error.WriteLine(message);
        }
    }
}
