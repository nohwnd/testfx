// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;
using System.Reflection;
using System.Linq;

using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Runtime.CompilerServices;

namespace Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Discovery;

/// <summary>
/// Determines whether a type is a valid test class for this adapter.
/// </summary>
internal class TypeValidator
{
    // Setting this to a string representation instead of a typeof(TestContext).FullName
    // since the later would require a load of the Test Framework extension assembly at this point.
    private const string TestContextFullName = "Microsoft.VisualStudio.TestTools.UnitTesting.TestContext";
    private readonly ReflectHelper _reflectHelper;
    private readonly bool _discoverInternals;
    private readonly Type _type;
    private readonly TypeInfo _typeInfo;
    private readonly IReadOnlyCollection<Attribute> _typeAttributes;

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeValidator"/> class.
    /// </summary>
    /// <param name="reflectHelper">An instance to reflection helper for type information.</param>
    internal TypeValidator(Type type, IReadOnlyCollection<Attribute> typeAttributes, ReflectHelper reflectHelper)
        : this(type, typeAttributes, reflectHelper, false)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TypeValidator"/> class.
    /// </summary>
    /// <param name="reflectHelper">An instance to reflection helper for type information.</param>
    /// <param name="discoverInternals">True to discover test classes which are declared internal in
    /// addition to test classes which are declared public.</param>
    internal TypeValidator(Type type, IReadOnlyCollection<Attribute> typeAttributes, ReflectHelper reflectHelper, bool discoverInternals)
    {
        _type = type;
        _typeInfo = type.GetTypeInfo();
        _typeAttributes = typeAttributes;
        _reflectHelper = reflectHelper;
        _discoverInternals = discoverInternals;
    }

    /// <summary>
    /// Determines if a type is a valid test class for this adapter.
    /// </summary>
    /// <param name="type">The reflected type.</param>
    /// <param name="warnings">Contains warnings if any, that need to be passed back to the caller.</param>
    /// <returns>Return true if it is a valid test class.</returns>
    internal virtual bool IsValidTestClass(ICollection<string> warnings)
    {
        if (!_typeInfo.IsClass
            || (!HasAttribute(typeof(TestClassAttribute))
            && !HasAttributeDerivedFrom(typeof(TestClassAttribute))))
        {
            return false;
        }

        // inaccessible class
        if (!TypeHasValidAccessibility(_discoverInternals))
        {
            var warning = string.Format(CultureInfo.CurrentCulture, Resource.UTA_ErrorNonPublicTestClass, _type.FullName);
            warnings.Add(warning);
            return false;
        }

        // Generic class
        if (_typeInfo.IsGenericTypeDefinition && !_typeInfo.IsAbstract)
        {
            // In IDE generic classes that are not abstract are treated as not runnable. Keep consistence.
            var warning = string.Format(CultureInfo.CurrentCulture, Resource.UTA_ErrorTestClassIsGenericNonAbstract, _type.FullName);
            warnings.Add(warning);
            return false;
        }

        // Class is not valid if the testContext property is incorrect
        if (!HasCorrectTestContextSignature())
        {
            var warning = string.Format(CultureInfo.CurrentCulture, Resource.UTA_ErrorInValidTestContextSignature, _type.FullName);
            warnings.Add(warning);
            return false;
        }

        // Abstract test classes can be base classes for derived test classes.
        //   There is no way to see if there are derived test classes.
        //   Thus if a test class is abstract, just ignore all test methods from it
        //   (they will be visible in derived classes). No warnings (such as test method, deployment item,
        //   etc attribute is defined on the class) will be generated for this class:
        // What we do is:
        //   - report the class as "not valid" test class. This will cause to skip enumerating tests from it.
        //   - Do not generate warnings/do not create NOT RUNNABLE tests.
        return !_typeInfo.IsAbstract;
    }

    /// <summary>
    /// Determines if the type has a valid TestContext property definition.
    /// </summary>
    /// <param name="type">The reflected type.</param>
    /// <returns>Returns true if type has a valid TestContext property definition.</returns>
    internal bool HasCorrectTestContextSignature()
    {
        DebugEx.Assert(_type != null, "HasCorrectTestContextSignature type is null");

        var propertyInfoEnumerable = _typeInfo.DeclaredProperties;
        var propertyInfo = new List<PropertyInfo>();

        foreach (var pinfo in propertyInfoEnumerable)
        {
            // PropertyType.FullName can be null if the property is a generic type.
            if (TestContextFullName.Equals(pinfo.PropertyType.FullName, StringComparison.Ordinal))
            {
                propertyInfo.Add(pinfo);
            }
        }

        if (propertyInfo.Count == 0)
        {
            return true;
        }

        foreach (var pinfo in propertyInfo)
        {
            var setInfo = pinfo.SetMethod;
            if (setInfo == null)
            {
                // we have a getter, but not a setter.
                return false;
            }

            if (setInfo.IsPrivate || setInfo.IsStatic || setInfo.IsAbstract)
            {
                return false;
            }
        }

        return true;
    }

    internal bool TypeHasValidAccessibility(bool discoverInternals)
    {
        if (_typeInfo.IsVisible)
        {
            // The type is public or a public nested class of entirely public container classes.
            return true;
        }

        if (!discoverInternals)
        {
            // The type is not externally visible and internal test classes are not to be discovered.
            return false;
        }

        // Either the type is not public or it is a nested class and itself or one of its containers is not public.
        if (!_typeInfo.IsNested)
        {
            // The type is not public and is not nested. Non-nested types can be only public or internal
            // so this type must be internal.
            return true;
        }

        // Assembly is CLR term for internal visibility:
        // Private == private,
        // FamilyANDAssembly == private protected,
        // Assembly == internal,
        // Family == protected,
        // FamilyORAssembly == protected internal,
        // Public == public.
        // So this reads IsNestedInternal || IsNestedPublic:
        var isNestedPublicOrInternal = _typeInfo.IsNestedAssembly || _typeInfo.IsNestedPublic;

        if (!isNestedPublicOrInternal)
        {
            // This type is nested, but is not public or internal.
            return false;
        }

        // The type itself is nested and is public, or internal, but could be in hierarchy of types
        // where some of the parent types is private (or other modifier that is not public and is not internal)
        // if we looked for just public types we could just look at IsVisible, but internal type nested in internal type
        // is not Visible, so we need to check all the parents and make sure they are all either public or internal.
        var parentsArePublicOrInternal = true;
        var declaringType = _typeInfo.DeclaringType;
        while (declaringType != null && parentsArePublicOrInternal)
        {
            var declaringTypeIsPublicOrInternal =

                // Declaring type is non-nested type, and we are looking for internal or public, which are the only
                // two valid options that non-nested type can be.
                !declaringType.IsNested

                // Or the type is nested internal, or nested public type, but not any other
                // like nested protected internal type, or nested private type.
                || declaringType.GetTypeInfo().IsNestedAssembly || declaringType.GetTypeInfo().IsNestedPublic;

            if (!declaringTypeIsPublicOrInternal)
            {
                parentsArePublicOrInternal = false;
                break;
            }

            declaringType = declaringType.DeclaringType;
        }

        return parentsArePublicOrInternal;
    }

    internal bool HasAttribute(Type attributeType)
    {
        foreach (var attribute in _typeAttributes)
        {
            if (attribute.GetType() == attributeType)
            {
                return true;
            }
        }

        return false;
    }

    internal bool HasAttributeDerivedFrom(Type attributeType)
    {
        var attributeTypeInfo = attributeType.GetTypeInfo();
        foreach (var attribute in _typeAttributes)
        {
            if (attributeTypeInfo.IsSubclassOf(attribute.GetType()))
            {
                return true;
            }
        }

        return false;
    }
}
