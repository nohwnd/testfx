// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;

using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Extensions;
using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Helpers;
using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Discovery;

/// <summary>
/// Enumerates through the type looking for Valid Test Methods to execute.
/// </summary>
internal class TypeEnumerator
{
    private static readonly string[] EmptyStringArray = new string[0];

    private readonly Type _type;
    private readonly string _assemblyFilePath;
    private readonly TypeValidator _typeValidator;
    private readonly TestMethodValidator _testMethodValidator;
    private readonly TestIdGenerationStrategy _testIdGenerationStrategy;
    private readonly ReflectHelper _reflectHelper;
    private readonly IReadOnlyCollection<Attribute> _assemblyAttributes;
    private readonly IReadOnlyCollection<Attribute> _typeAttributes;
    private readonly IReadOnlyCollection<TestCategoryBaseAttribute> _assemblyCategories;
    private readonly IReadOnlyCollection<TestCategoryBaseAttribute> _typeCategories;

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeEnumerator"/> class.
    /// </summary>
    /// <param name="type"> The reflected type. </param>
    /// <param name="assemblyFilePath"> The name of the assembly being reflected. </param>
    /// <param name="reflectHelper"> An instance to reflection helper for type information. </param>
    /// <param name="typeValidator"> The validator for test classes. </param>
    /// <param name="testMethodValidator"> The validator for test methods. </param>
    /// <param name="testIdGenerationStrategy"><see cref="TestIdGenerationStrategy"/> to use when generating TestId.</param>
    internal TypeEnumerator(Type type, string assemblyFilePath, ReflectHelper reflectHelper, TypeValidator typeValidator, TestMethodValidator testMethodValidator, TestIdGenerationStrategy testIdGenerationStrategy, IReadOnlyCollection<Attribute> assemblyAttributes, IReadOnlyCollection<Attribute> typeAttributes)
    {
        _type = type;
        _assemblyFilePath = assemblyFilePath;
        _reflectHelper = reflectHelper;
        _typeValidator = typeValidator;
        _testMethodValidator = testMethodValidator;
        _testIdGenerationStrategy = testIdGenerationStrategy;
        _assemblyAttributes = assemblyAttributes;
        _typeAttributes = typeAttributes;
        _typeCategories = typeAttributes.OfType<TestCategoryBaseAttribute>().ToList();
        _assemblyCategories = assemblyAttributes.OfType<TestCategoryBaseAttribute>().ToList();
    }

    /// <summary>
    /// Walk through all methods in the type, and find out the test methods.
    /// </summary>
    /// <param name="warnings"> Contains warnings if any, that need to be passed back to the caller. </param>
    /// <returns> list of test cases.</returns>
    internal virtual ICollection<UnitTestElement>? Enumerate(out ICollection<string> warnings)
    {
        warnings = new Collection<string>();

        if (!_typeValidator.IsValidTestClass(warnings))
        {
            return null;
        }

        // If test class is valid, then get the tests
        return GetTests(warnings);
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
        foreach (var method in _type.GetRuntimeMethods())
        {
            var isMethodDeclaredInTestTypeAssembly = _reflectHelper.IsMethodDeclaredInSameAssemblyAsType(method, _type);
            var enableMethodsFromOtherAssemblies = MSTestSettings.CurrentSettings.EnableBaseClassTestMethodsFromOtherAssemblies;

            if (!isMethodDeclaredInTestTypeAssembly && !enableMethodsFromOtherAssemblies)
            {
                continue;
            }

            var methodAttributes = method.GetCustomAttributes().ToList();
            if (_testMethodValidator.IsValidTestMethod(method, methodAttributes, _type, warnings))
            {
                foundDuplicateTests = foundDuplicateTests || !foundTests.Add(method.Name);
                var testMethod = GetTestFromMethod(method, methodAttributes, isMethodDeclaredInTestTypeAssembly, warnings);

                tests.Add(testMethod);
            }
        }

        if (!foundDuplicateTests)
        {
            return tests;
        }

        // Remove duplicate test methods by taking the first one of each name
        // that is declared closest to the test class in the hierarchy.
        var inheritanceDepths = new Dictionary<string, int>();
        var currentType = _type;
        int currentDepth = 0;

        while (currentType != null)
        {
            inheritanceDepths[currentType.FullName!] = currentDepth;
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
    internal UnitTestElement GetTestFromMethod(MethodInfo method, IReadOnlyCollection<Attribute> methodAttributes, bool isDeclaredInTestTypeAssembly, ICollection<string> warnings)
    {
        // null if the current instance represents a generic type parameter.
        DebugEx.Assert(_type.AssemblyQualifiedName != null, "AssemblyQualifiedName for method is null.");

        // This allows void returning async test method to be valid test method. Though they will be executed similar to non-async test method.
        var isAsync = ReflectHelper.MatchReturnType(method, typeof(Task));

        var testMethod = new TestMethod(method, method.Name, _type.FullName!, _assemblyFilePath, isAsync, _testIdGenerationStrategy);

        if (method.DeclaringType != _type || !string.Equals(method.DeclaringType!.FullName, _type.FullName, StringComparison.Ordinal))
        {
            testMethod.DeclaringClassFullName = method.DeclaringType.FullName;
        }

        if (!isDeclaredInTestTypeAssembly)
        {
            testMethod.DeclaringAssemblyName =
                PlatformServiceProvider.Instance.FileOperations.GetAssemblyPath(
                    method.DeclaringType.GetTypeInfo().Assembly);
        }

        IReadOnlyCollection<Attribute> methodNonInheritedAttributes = methodAttributes;
        IReadOnlyCollection<Attribute> methodInheritedAttributes = CustomAttributeExtensions.GetCustomAttributes(method, inherit: true).ToList();

        var testElement = new UnitTestElement(testMethod)
        {
            // Get compiler generated type name for async test method (either void returning or task returning).
            AsyncTypeName = isAsync ? method.GetAsyncTypeName() : null,
            TestCategory = GetCategories(methodNonInheritedAttributes),
            DoNotParallelize = HasAttribute(methodNonInheritedAttributes, typeof(DoNotParallelizeAttribute)) || HasAttribute(_typeAttributes, typeof(DoNotParallelizeAttribute)),
            Priority = GetSingleAttributeOrNull<PriorityAttribute>(methodInheritedAttributes)?.Priority,
            Ignored = GetSingleAttributeOrNull<IgnoreAttribute>(methodNonInheritedAttributes) != null,

            // TODO: is this very expensive?
            // DeploymentItems = PlatformServiceProvider.Instance.TestDeployment.GetDeploymentItems(method, _type, warnings),
        };

        var traits = GetTestPropertiesAsTraits(methodInheritedAttributes);

        var ownerTrait = GetTestOwnerAsTraits(methodInheritedAttributes);
        if (ownerTrait != null)
        {
            traits.Add(ownerTrait);
        }

        var priorityTrait = _reflectHelper.GetTestPriorityAsTraits(testElement.Priority);
        if (priorityTrait != null)
        {
            traits.Add(priorityTrait);
        }

        testElement.Traits = traits.ToArray();

        if (GetSingleAttributeOrNull<CssIterationAttribute>(methodInheritedAttributes) is CssIterationAttribute cssIteration)
        {
            testElement.CssIteration = cssIteration.CssIteration;
        }

        if (GetSingleAttributeOrNull<CssProjectStructureAttribute>(methodInheritedAttributes) is CssProjectStructureAttribute cssProjectStructure)
        {
            testElement.CssProjectStructure = cssProjectStructure.CssProjectStructure;
        }

        if (GetSingleAttributeOrNull<DescriptionAttribute>(methodInheritedAttributes) is DescriptionAttribute descriptionAttribute)
        {
            testElement.Description = descriptionAttribute.Description;
        }

        var workItemAttributes = methodInheritedAttributes.OfType<WorkItemAttribute>().ToArray();
        if (workItemAttributes.Length != 0)
        {
            testElement.WorkItemIds = workItemAttributes.Select(x => x.Id.ToString(CultureInfo.InvariantCulture)).ToArray();
        }

        // get DisplayName from TestMethodAttribute (or any inherited attribute)
        var displayName = GetSingleAttributeOrNull<TestMethodAttribute>(methodInheritedAttributes)?.DisplayName;
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
        if (methodCategories.Count == 0 && _typeCategories.Count == 0 && _assemblyCategories.Count == 0)
        {
            return EmptyStringArray;
        }

        var categories = new List<string>(methodCategories.Count + _typeCategories.Count + _assemblyCategories.Count);

        foreach (TestCategoryBaseAttribute category in methodCategories)
        {
            categories.AddRange(category.TestCategories);
        }

        foreach (TestCategoryBaseAttribute category in _typeCategories)
        {
            categories.AddRange(category.TestCategories);
        }

        foreach (TestCategoryBaseAttribute category in _assemblyCategories)
        {
            categories.AddRange(category.TestCategories);
        }

        return categories.ToArray();
    }
}
