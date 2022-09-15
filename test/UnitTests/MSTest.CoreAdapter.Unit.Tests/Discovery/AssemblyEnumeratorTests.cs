// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.MSTestAdapter.UnitTests.Discovery
{
    extern alias FrameworkV1;
    extern alias FrameworkV2;

    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading.Tasks;
    using System.Xml;
    using Castle.Core.Internal;
    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter;
    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Discovery;
    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Helpers;
    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.ObjectModel;
    using Microsoft.VisualStudio.TestPlatform.MSTestAdapter.UnitTests.TestableImplementations;

    using Moq;

    using Assert = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
    using CollectionAssert = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.CollectionAssert;
    using TestClass = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.TestClassAttribute;
    using TestCleanup = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.TestCleanupAttribute;
    using TestInitialize = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.TestInitializeAttribute;
    using TestMethodV1 = FrameworkV1::Microsoft.VisualStudio.TestTools.UnitTesting.TestMethodAttribute;

    [TestClass]
    public class AssemblyEnumeratorTests
    {
        private AssemblyEnumerator assemblyEnumerator;
        private ICollection<string> warnings;
        private TestablePlatformServiceProvider testablePlatformServiceProvider;

        [TestInitialize]
        public void TestInit()
        {
            this.assemblyEnumerator = new AssemblyEnumerator();
            this.warnings = new List<string>();

            this.testablePlatformServiceProvider = new TestablePlatformServiceProvider();
        }

        [TestCleanup]
        public void Cleanup()
        {
            PlatformServiceProvider.Instance = null;
        }

        #region  Constructor tests

        [TestMethodV1]
        public void ConstructorShouldPopulateSettings()
        {
            string runSettingsXml =
                 @"<RunSettings>
                     <MSTest>
                        <ForcedLegacyMode>True</ForcedLegacyMode>
                        <SettingsFile>DummyPath\TestSettings1.testsettings</SettingsFile>
                     </MSTest>
                   </RunSettings>";

            this.testablePlatformServiceProvider.MockSettingsProvider.Setup(sp => sp.Load(It.IsAny<XmlReader>()))
                .Callback((XmlReader actualReader) =>
                {
                    if (actualReader != null)
                    {
                        actualReader.Read();
                        actualReader.ReadInnerXml();
                    }
                });

            MSTestSettings adapterSettings = MSTestSettings.GetSettings(runSettingsXml, MSTestSettings.SettingsName);
            var assemblyEnumerator = new AssemblyEnumerator(adapterSettings);
            assemblyEnumerator.RunSettingsXml = runSettingsXml;

            Assert.IsTrue(MSTestSettings.CurrentSettings.ForcedLegacyMode);
            Assert.AreEqual("DummyPath\\TestSettings1.testsettings", MSTestSettings.CurrentSettings.TestSettingsFile);
        }

        #endregion

        #region GetTypes tests

        [TestMethodV1]
        public void GetTypesShouldReturnEmptyArrayWhenNoDeclaredTypes()
        {
            Mock<TestableAssembly> mockAssembly = new Mock<TestableAssembly>();

            // Setup mocks
            mockAssembly.Setup(a => a.DefinedTypes).Returns(new List<TypeInfo>());

            Assert.AreEqual(0, this.assemblyEnumerator.GetTypes(mockAssembly.Object, string.Empty, this.warnings).Length);
        }

        [TestMethodV1]
        public void GetTypesShouldReturnSetOfDefinedTypes()
        {
            Mock<TestableAssembly> mockAssembly = new Mock<TestableAssembly>();

            var expectedTypes = new List<TypeInfo>() { typeof(DummyTestClass).GetTypeInfo(), typeof(DummyTestClass).GetTypeInfo() };

            // Setup mocks
            mockAssembly.Setup(a => a.DefinedTypes).Returns(expectedTypes);

            var types = this.assemblyEnumerator.GetTypes(mockAssembly.Object, string.Empty, this.warnings);
            CollectionAssert.AreEqual(expectedTypes, types);
        }

        [TestMethodV1]
        public void GetTypesShouldHandleReflectionTypeLoadException()
        {
            Mock<TestableAssembly> mockAssembly = new Mock<TestableAssembly>();

            // Setup mocks
            mockAssembly.Setup(a => a.DefinedTypes).Throws(new ReflectionTypeLoadException(null, null));

            this.assemblyEnumerator.GetTypes(mockAssembly.Object, string.Empty, this.warnings);
        }

        [TestMethodV1]
        public void GetTypesShouldReturnReflectionTypeLoadExceptionTypesOnException()
        {
            Mock<TestableAssembly> mockAssembly = new Mock<TestableAssembly>();
            var reflectedTypes = new Type[] { typeof(DummyTestClass) };

            // Setup mocks
            mockAssembly.Setup(a => a.DefinedTypes).Throws(new ReflectionTypeLoadException(reflectedTypes, null));

            var types = this.assemblyEnumerator.GetTypes(mockAssembly.Object, string.Empty, this.warnings);

            Assert.IsNotNull(types);
            CollectionAssert.AreEqual(reflectedTypes, types);
        }

        [TestMethodV1]
        public void GetTypesShouldLogWarningsWhenReflectionFailsWithLoaderExceptions()
        {
            Mock<TestableAssembly> mockAssembly = new Mock<TestableAssembly>();
            var exceptions = new Exception[] { new Exception("DummyLoaderException") };

            // Setup mocks
            mockAssembly.Setup(a => a.DefinedTypes).Throws(new ReflectionTypeLoadException(null, exceptions));

            var types = this.assemblyEnumerator.GetTypes(mockAssembly.Object, "DummyAssembly", this.warnings);

            Assert.AreEqual(1, this.warnings.Count);
            CollectionAssert.Contains(
                this.warnings.ToList(),
                string.Format(CultureInfo.CurrentCulture, Resource.TypeLoadFailed, "DummyAssembly", "System.Exception: DummyLoaderException\r\n"));

            this.testablePlatformServiceProvider.MockTraceLogger.Verify(tl => tl.LogWarning("{0}", exceptions[0]), Times.Once);
        }

        #endregion

        #region GetLoadExceptionDetails tests

        [TestMethodV1]
        public void GetLoadExceptionDetailsShouldReturnExceptionMessageIfLoaderExceptionsIsNull()
        {
            Assert.AreEqual(
                "DummyMessage\r\n",
                this.assemblyEnumerator.GetLoadExceptionDetails(
                    new ReflectionTypeLoadException(null, null, "DummyMessage")));
        }

        [TestMethodV1]
        public void GetLoadExceptionDetailsShouldReturnLoaderExceptionMessage()
        {
            var loaderException = new AccessViolationException("DummyLoaderExceptionMessage2");
            var exceptions = new ReflectionTypeLoadException(null, new Exception[] { loaderException });

            Assert.AreEqual(
                string.Concat(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resource.EnumeratorLoadTypeErrorFormat,
                        loaderException.GetType(),
                        loaderException.Message),
                    "\r\n"),
                this.assemblyEnumerator.GetLoadExceptionDetails(exceptions));
        }

        [TestMethodV1]
        public void GetLoadExceptionDetailsShouldReturnLoaderExceptionMessagesForMoreThanOneException()
        {
            var loaderException1 = new ArgumentNullException("DummyLoaderExceptionMessage1", (Exception)null);
            var loaderException2 = new AccessViolationException("DummyLoaderExceptionMessage2");
            var exceptions = new ReflectionTypeLoadException(
                null,
                new Exception[] { loaderException1, loaderException2 });
            StringBuilder errorDetails = new StringBuilder();

            errorDetails.AppendFormat(
                    CultureInfo.CurrentCulture,
                    Resource.EnumeratorLoadTypeErrorFormat,
                    loaderException1.GetType(),
                    loaderException1.Message).AppendLine();
            errorDetails.AppendFormat(
                    CultureInfo.CurrentCulture,
                    Resource.EnumeratorLoadTypeErrorFormat,
                    loaderException2.GetType(),
                    loaderException2.Message).AppendLine();

            Assert.AreEqual(errorDetails.ToString(), this.assemblyEnumerator.GetLoadExceptionDetails(exceptions));
        }

        [TestMethodV1]
        public void GetLoadExceptionDetailsShouldLogUniqueExceptionsOnly()
        {
            var loaderException = new AccessViolationException("DummyLoaderExceptionMessage2");
            var exceptions = new ReflectionTypeLoadException(null, new Exception[] { loaderException, loaderException });

            Assert.AreEqual(
                string.Concat(
                    string.Format(
                        CultureInfo.CurrentCulture,
                        Resource.EnumeratorLoadTypeErrorFormat,
                        loaderException.GetType(),
                        loaderException.Message),
                    "\r\n"),
                this.assemblyEnumerator.GetLoadExceptionDetails(exceptions));
        }

        #endregion

        #region EnumerateAssembly tests

        [TestMethodV1]
        public void EnumearateTypePerf()
        {
            // var asm = @"C:\t\TestProject13_for_mstest\TestProject1\bin\Debug\net452\TestProject1.dll";
            var asm = @"C:\t\TestProject13_for_mstest\TestProject5\bin\Debug\net472\TestProject5.dll";
            var assembly = Assembly.LoadFrom(asm);

            var groups = assembly.GetTypes().Where(n => n.Name.StartsWith("UnitTest")).Select(t => new { Type = t, Methods = t.GetMethods().Where(mm => mm.Name.StartsWith("TestMethod")).ToList() }).ToList();

            var rh = new ReflectHelper();
            var warnings = new List<string>();
            var sw = Stopwatch.StartNew();
            var c = 0;
            var assemblyAttributes = assembly.GetCustomAttributes().ToList();

            foreach (var g in groups)
            {
                var typeAttributes = g.Type.GetCustomAttributes().ToList();
                var te = new TypeEnumerator(typeof(A), typeof(A).Assembly.FullName, rh, new TypeValidator(g.Type, typeAttributes, rh, discoverInternals: false), new TestMethodValidator(g.Type, typeAttributes, rh, false), assemblyAttributes, typeAttributes);

                foreach (var m in g.Methods)
                {
                    c++;

                    te.GetTestFromMethod(m, true, warnings);
                }
            }

            Console.WriteLine(c);
            Console.WriteLine(sw.Elapsed);
        }

        private static Mock<TestableAssembly> CreateMockTestableAssembly()
        {
            var mockAssembly = new Mock<TestableAssembly>();

            // The mock must be configured with a return value for GetCustomAttributes for this attribute type, but the
            // actual return value is irrelevant for these tests.
            mockAssembly
                .Setup(a => a.GetCustomAttributes(
                    typeof(FrameworkV2::Microsoft.VisualStudio.TestTools.UnitTesting.DiscoverInternalsAttribute),
                    true))
                .Returns(new Attribute[0]);

            mockAssembly
                .Setup(a => a.GetCustomAttributes(
                    typeof(FrameworkV2::Microsoft.VisualStudio.TestTools.UnitTesting.TestDataSourceDiscoveryAttribute),
                    true))
                .Returns(new Attribute[0]);

            return mockAssembly;
        }

        #endregion
    }

    #region Testable Implementations

    public class TestableAssembly : Assembly
    {
    }

    internal class TestableAssemblyEnumerator : AssemblyEnumerator
    {
        internal TestableAssemblyEnumerator()
        {
            var reflectHelper = new Mock<ReflectHelper>();
            var typeValidator = new Mock<TypeValidator>(reflectHelper.Object);
            var testMethodValidator = new Mock<TestMethodValidator>(reflectHelper.Object);
            this.MockTypeEnumerator = new Mock<TypeEnumerator>(
                typeof(DummyTestClass),
                "DummyAssembly",
                reflectHelper.Object,
                typeValidator.Object,
                testMethodValidator.Object);
        }

        internal Mock<TypeEnumerator> MockTypeEnumerator { get; set; }

        internal override TypeEnumerator GetTypeEnumerator(IReadOnlyCollection<Attribute> assemblyAttributes, string assemblyFileName, Type type, IReadOnlyCollection<Attribute> typeAttributes, bool discoverInternals)
        {
            return this.MockTypeEnumerator.Object;
        }
    }

    #endregion

    [FrameworkV2::Microsoft.VisualStudio.TestTools.UnitTesting.TestClass]
#pragma warning disable SA1202 // Elements must be ordered by access
    public class A

#pragma warning restore SA1202 // Elements must be ordered by access
    {
        [FrameworkV2::Microsoft.VisualStudio.TestTools.UnitTesting.TestMethod]
        public Task B()
        {
            return Task.FromResult(0);
        }
    }
}
