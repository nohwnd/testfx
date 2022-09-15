// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.MSTestAdapter.UnitTests.Discovery
{
    extern alias FrameworkV1;
    extern alias FrameworkV2;

    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Reflection;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter;
    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Discovery;
    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Helpers;
    using Moq;
    using Assert = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
    using CollectionAssert = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.CollectionAssert;
    using TestClass = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute;
    using TestInitialize = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.TestInitializeAttribute;
    using TestMethod = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute;
    using UTF = FrameworkV2::Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class TestMethodValidatorTests
    {
        private TestMethodValidator testMethodValidator;
        private Mock<ReflectHelper> mockReflectHelper;
        private List<string> warnings;

        private Mock<MethodInfo> mockMethodInfo;
        private Type type;

        [TestInitialize]
        public void TestInit()
        {
            this.mockReflectHelper = new Mock<ReflectHelper>();
            this.testMethodValidator = new TestMethodValidator(typeof(object),  new List<Attribute>(), this.mockReflectHelper.Object, false);
            this.warnings = new List<string>();

            this.mockMethodInfo = new Mock<MethodInfo>();
            this.type = typeof(TestMethodValidatorTests);
        }

        #region Discovery of internals enabled

        #endregion

        private void SetupTestMethod()
        {
            this.mockReflectHelper.Setup(
                rh => rh.IsAttributeDefined(It.IsAny<MemberInfo>(), typeof(UTF.TestMethodAttribute), false)).Returns(true);
        }
    }

    #region Dummy types

    public class DummyTestClassWithGenericMethods
    {
        public void GenericMethod<T>()
        {
        }
    }

    internal abstract class DummyTestClass
    {
        public static void StaticTestMethod()
        {
        }

        public abstract void AbstractTestMethod();

        public async void AsyncMethodWithVoidReturnType()
        {
            await Task.FromResult(true);
        }

        public async Task AsyncMethodWithTaskReturnType()
        {
            await Task.Delay(TimeSpan.Zero);
        }

        public Task MethodWithTaskReturnType()
        {
            return Task.Delay(TimeSpan.Zero);
        }

        public int MethodWithIntReturnType()
        {
            return 0;
        }

        public void MethodWithVoidReturnType()
        {
        }

        internal void InternalTestMethod()
        {
        }

        private void PrivateTestMethod()
        {
        }
    }

    #endregion
}
