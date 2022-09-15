// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.MSTestAdapter.UnitTests.Discovery
{
    extern alias FrameworkV1;
    extern alias FrameworkV2;
    extern alias FrameworkV2CoreExtension;

    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter;
    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Discovery;
    using Microsoft.VisualStudio.TestPlatform.MSTestAdapter.UnitTests.TestableImplementations;
    using Moq;
    using Assert = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
    using CollectionAssert = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.CollectionAssert;
    using TestClass = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute;
    using TestCleanup = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanupAttribute;
    using TestInitialize = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.TestInitializeAttribute;
    using TestMethod = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute;
    using UTF = FrameworkV2::Microsoft.VisualStudio.TestTools.UnitTesting;

    [TestClass]
    public class AssemblyEnumeratorWrapperTests
    {
        private AssemblyEnumeratorWrapper testableAssemblyEnumeratorWrapper;

        private ICollection<string> warnings;

        private TestablePlatformServiceProvider testablePlatformServiceProvider;

        [TestInitialize]
        public void TestInit()
        {
            this.testableAssemblyEnumeratorWrapper = new AssemblyEnumeratorWrapper();
            this.warnings = new List<string>();

            this.testablePlatformServiceProvider = new TestablePlatformServiceProvider();
            PlatformServiceProvider.Instance = this.testablePlatformServiceProvider;
        }

        [TestCleanup]
        public void Cleanup()
        {
            PlatformServiceProvider.Instance = null;
        }

        #region private helpers

        private void SetupMocks(string assemblyName, bool doesFileExist, bool isAssemblyReferenced)
        {
            this.testablePlatformServiceProvider.MockFileOperations.Setup(fo => fo.GetFullFilePath(assemblyName))
                .Returns(assemblyName);
            this.testablePlatformServiceProvider.MockFileOperations.Setup(fo => fo.DoesFileExist(assemblyName))
                .Returns(doesFileExist);
            this.testablePlatformServiceProvider.MockTestSourceValidator.Setup(
                tsv => tsv.IsAssemblyReferenced(It.IsAny<AssemblyName>(), assemblyName)).Returns(isAssemblyReferenced);
            this.testablePlatformServiceProvider.MockFileOperations.Setup(fo => fo.LoadAssembly(assemblyName, It.IsAny<bool>()))
                .Returns(Assembly.GetExecutingAssembly());
            this.testablePlatformServiceProvider.MockTestSourceHost.Setup(
                ih => ih.CreateInstanceForType(typeof(AssemblyEnumerator), It.IsAny<object[]>()))
                .Returns(new AssemblyEnumerator());
        }

        #endregion

        #region dummy implementations.

        [UTF.TestClass]
        public class ValidTestClass
        {
            // This is just a dummy method for test validation.
            [UTF.TestMethod]
            public void ValidTestMethod()
            {
            }
        }

        #endregion
    }
}
