// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Discovery
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Diagnostics;
    using System.Linq;
    using System.Reflection;
    using System.Threading.Tasks;
    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Extensions;
    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Helpers;
    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.ObjectModel;
    using Microsoft.VisualStudio.TestPlatform.ObjectModel;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Enumerates through the type looking for Valid Test Methods to execute.
    /// </summary>
    internal class TypeEnumerator
    {
        private static readonly string[] EmptyStringArray = new string[0];

        private readonly Type type;
        private readonly string assemblyName;
        private readonly TypeValidator typeValidator;
        private readonly TestMethodValidator testMethodValidator;
        private readonly ReflectHelper reflectHelper;
        private readonly IReadOnlyCollection<Attribute> assemblyAttributes;
        private readonly IReadOnlyCollection<Attribute> typeAttributes;
        private readonly IReadOnlyCollection<TestCategoryBaseAttribute> assemblyCategories;
        private readonly IReadOnlyCollection<TestCategoryBaseAttribute> typeCategories;

        /// <summary>
        /// Initializes a new instance of the <see cref="TypeEnumerator"/> class.
        /// </summary>
        /// <param name="type"> The reflected type. </param>
        /// <param name="assemblyName"> The name of the assembly being reflected. </param>
        /// <param name="reflectHelper"> An instance to reflection helper for type information. </param>
        /// <param name="typeValidator"> The validator for test classes. </param>
        /// <param name="testMethodValidator"> The validator for test methods. </param>
        /// <param name="assemblyAttributes"> The attributes that are defined on the assembly that contains this type. </param>
        /// <param name="typeAttributes"> The attributes that are defined on this type. </param>
        internal TypeEnumerator(Type type, string assemblyName, ReflectHelper reflectHelper, TypeValidator typeValidator, TestMethodValidator testMethodValidator, IReadOnlyCollection<Attribute> assemblyAttributes, IReadOnlyCollection<Attribute> typeAttributes)
        {
            this.type = type;
            this.assemblyName = assemblyName;
            this.reflectHelper = reflectHelper;
            this.typeValidator = typeValidator;
            this.testMethodValidator = testMethodValidator;
            this.assemblyAttributes = assemblyAttributes;
            this.typeAttributes = typeAttributes;
            this.typeCategories = typeAttributes.OfType<TestCategoryBaseAttribute>().ToList();
            this.assemblyCategories = assemblyAttributes.OfType<TestCategoryBaseAttribute>().ToList();
        }

        /// <summary>
        /// Walk through all methods in the type, and find out the test methods
        /// </summary>
        /// <param name="warnings"> Contains warnings if any, that need to be passed back to the caller. </param>
        /// <returns> list of test cases.</returns>
        internal virtual ICollection<UnitTestElement> Enumerate(out ICollection<string> warnings)
        {
            warnings = new Collection<string>();

            if (!this.typeValidator.IsValidTestClass(warnings))
            {
                return null;
            }

            // If test class is valid, then get the tests
            return this.GetTests(warnings);
        }

        /// <summary>
        /// Gets a list of valid tests in a type.
        /// </summary>
        /// <param name="warnings"> Contains warnings if any, that need to be passed back to the caller. </param>
        /// <returns> List of Valid Tests. </returns>
        internal Collection<UnitTestElement> GetTests(ICollection<string> warnings)
        {
            bool foundDuplicateTests = false;
            var foundTests = new HashSet<string>();
            var tests = new Collection<UnitTestElement>();

            // Test class is already valid. Verify methods.
            foreach (var method in this.type.GetRuntimeMethods())
            {
                var isMethodDeclaredInTestTypeAssembly = this.reflectHelper.IsMethodDeclaredInSameAssemblyAsType(method, this.type);
                var enableMethodsFromOtherAssemblies = MSTestSettings.CurrentSettings.EnableBaseClassTestMethodsFromOtherAssemblies;

                if (!isMethodDeclaredInTestTypeAssembly && !enableMethodsFromOtherAssemblies)
                {
                    continue;
                }

                var methodAttributes = method.GetCustomAttributes().ToList();
                if (this.testMethodValidator.IsValidTestMethod(method, methodAttributes, this.type, warnings))
                {
                    foundDuplicateTests = foundDuplicateTests || !foundTests.Add(method.Name);
                    var test = this.GetTestFromMethod(method, isMethodDeclaredInTestTypeAssembly, warnings);

                    tests.Add(test);
                }
            }

            if (!foundDuplicateTests)
            {
                return tests;
            }

            // Remove duplicate test methods by taking the first one of each name
            // that is declared closest to the test class in the hierarchy.
            var inheritanceDepths = new Dictionary<string, int>();
            var currentType = this.type;
            int currentDepth = 0;

            while (currentType != null)
            {
                inheritanceDepths[currentType.FullName] = currentDepth;
                ++currentDepth;
                currentType = currentType.GetTypeInfo().BaseType;
            }

            return new Collection<UnitTestElement>(
                tests.GroupBy(
                    t => t.TestMethod.Name,
                    (_, elements) =>
                        elements.OrderBy(t => inheritanceDepths[t.TestMethod.DeclaringClassFullName ?? t.TestMethod.FullClassName]).First())
                    .ToList());
        }

        /// <summary>
        /// Gets a UnitTestElement from a MethodInfo object filling it up with appropriate values.
        /// </summary>
        /// <param name="method">The reflected method.</param>
        /// <param name="isDeclaredInTestTypeAssembly">True if the reflected method is declared in the same assembly as the current type.</param>
        /// <param name="warnings">Contains warnings if any, that need to be passed back to the caller.</param>
        /// <returns> Returns a UnitTestElement.</returns>
        internal UnitTestElement GetTestFromMethod(MethodInfo method, bool isDeclaredInTestTypeAssembly, ICollection<string> warnings)
        {
            // null if the current instance represents a generic type parameter.
            Debug.Assert(this.type.AssemblyQualifiedName != null, "AssemblyQualifiedName for method is null.");

            // This allows void returning async test method to be valid test method. Though they will be executed similar to non-async test method.
            var isAsync = ReflectHelper.MatchReturnType(method, typeof(Task));

            var testMethod = new TestMethod(method, method.Name, this.type.FullName, this.assemblyName, isAsync);

            if (method.DeclaringType != this.type)
            {
                testMethod.DeclaringClassFullName = method.DeclaringType.FullName;
            }

            if (!isDeclaredInTestTypeAssembly)
            {
                testMethod.DeclaringAssemblyName =
                    PlatformServiceProvider.Instance.FileOperations.GetAssemblyPath(
                        method.DeclaringType.GetTypeInfo().Assembly);
            }

            var testElement = new UnitTestElement(testMethod);

            // Get compiler generated type name for async test method (either void returning or task returning).
            var asyncTypeName = isAsync ? method.GetAsyncTypeName() : null;
            testElement.AsyncTypeName = asyncTypeName;

            IReadOnlyCollection<Attribute> methodNonInheritedAttributes = method.GetCustomAttributes().ToList();
            IReadOnlyCollection<Attribute> methodInheritedAttributes = method.GetCustomAttributes(true).ToList();
            testElement.TestCategory = this.GetCategories(methodNonInheritedAttributes);

            testElement.DoNotParallelize = this.HasAttribute(methodNonInheritedAttributes, typeof(DoNotParallelizeAttribute)) || this.HasAttribute(this.typeAttributes, typeof(DoNotParallelizeAttribute));

            List<Trait> traits = this.GetTestPropertiesAsTraits(methodInheritedAttributes);

            var ownerTrait = this.GetTestOwnerAsTraits(methodInheritedAttributes);
            if (ownerTrait != null)
            {
                traits.Add(ownerTrait);
            }

            testElement.Priority = this.GetSingleAttributeOrNull<PriorityAttribute>(methodInheritedAttributes)?.Priority;

            // this method just converts int? to trait object, it is okay how it is implemented in reflect helper
            var priorityTrait = this.reflectHelper.GetTestPriorityAsTraits(testElement.Priority);
            if (priorityTrait != null)
            {
                traits.Add(priorityTrait);
            }

            testElement.Traits = traits.ToArray();

            testElement.CssIteration = this.GetSingleAttributeOrNull<CssIterationAttribute>(methodInheritedAttributes)?.CssIteration;
            testElement.CssProjectStructure = this.GetSingleAttributeOrNull<CssProjectStructureAttribute>(methodInheritedAttributes)?.CssProjectStructure;
            testElement.Description = this.GetSingleAttributeOrNull<DescriptionAttribute>(methodInheritedAttributes)?.Description;

            testElement.WorkItemIds = methodInheritedAttributes.OfType<WorkItemAttribute>().Select(a => a.Id.ToString()).ToArray();

            testElement.Ignored = method.IsDefined(typeof(IgnoreAttribute), false);

            // Get Deployment items if any.
            testElement.DeploymentItems = PlatformServiceProvider.Instance.TestDeployment.GetDeploymentItems(method, this.type, warnings);

            // get DisplayName from TestMethodAttribute
            var displayName = this.GetSingleAttributeOrNull<TestMethodAttribute>(methodInheritedAttributes)?.DisplayName;
            testElement.DisplayName = displayName ?? method.Name;

            return testElement;
        }

        private T GetSingleAttributeOrNull<T>(IReadOnlyCollection<Attribute> methodInheritedAttributes)
            where T : Attribute
        {
            // Optimized to search only for first 2, and then return.
            T found = null;
            foreach (var attribute in methodInheritedAttributes)
            {
                if (attribute is T a)
                {
                    if (found != null)
                    {
                        // we found second one, and we want to return null when there are more than 1;
                        return null;
                    }

                    // we found the first one, save it
                    found = a;
                }
            }

            // Either the first one or null.
            return found;
        }

        private int? GetPriority(IReadOnlyCollection<Attribute> methodInheritedAttributes)
        {
            var priorityAttributes = methodInheritedAttributes.OfType<PriorityAttribute>().ToList();

            if (priorityAttributes.Count != 1)
            {
                return null;
            }

            return priorityAttributes[0].Priority;
        }

        private Trait GetTestOwnerAsTraits(IReadOnlyCollection<Attribute> methodInheritedAttributes)
        {
            var ownerAttributes = methodInheritedAttributes.OfType<OwnerAttribute>().ToList();
            if (ownerAttributes.Count != 1)
            {
                return null;
            }

            string owner = ownerAttributes[0].Owner;
            return new Trait("Owner", owner);
        }

        private List<Trait> GetTestPropertiesAsTraits(IReadOnlyCollection<Attribute> methodInheritedAttributes)
        {
            var testPropertyAttributes = methodInheritedAttributes.OfType<TestPropertyAttribute>().ToList();
            var properties = new List<Trait>(testPropertyAttributes.Count);

            foreach (TestPropertyAttribute testProperty in testPropertyAttributes)
            {
                if (testProperty.Name == null)
                {
                    properties.Add(new Trait(string.Empty, testProperty.Value));
                }
                else
                {
                    properties.Add(new Trait(testProperty.Name, testProperty.Value));
                }
            }

            return properties;
        }

        private bool HasAttribute(IReadOnlyCollection<Attribute> attributeCollection, Type type)
        {
            foreach (var attribute in attributeCollection)
            {
                if (attribute.GetType() == type)
                {
                    return true;
                }
            }

            return false;
        }

        private string[] GetCategories(IReadOnlyCollection<Attribute> methodAttributes)
        {
            var methodCategories = methodAttributes.OfType<TestCategoryBaseAttribute>().ToList();
            if (methodCategories.Count == 0 && this.typeCategories.Count == 0 && this.assemblyCategories.Count == 0)
            {
                return EmptyStringArray;
            }

            var categories = new List<string>(methodCategories.Count + this.typeCategories.Count + this.assemblyCategories.Count);

            foreach (TestCategoryBaseAttribute category in methodCategories)
            {
                categories.AddRange(category.TestCategories);
            }

            foreach (TestCategoryBaseAttribute category in this.typeCategories)
            {
                categories.AddRange(category.TestCategories);
            }

            foreach (TestCategoryBaseAttribute category in this.assemblyCategories)
            {
                categories.AddRange(category.TestCategories);
            }

            return categories.ToArray();
        }
    }
}
