// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Discovery
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;

    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Extensions;
    using Microsoft.VisualStudio.TestPlatform.MSTest.TestAdapter.Helpers;
    using Microsoft.VisualStudio.TestTools.UnitTesting;

    /// <summary>
    /// Determines if a method is a valid test method.
    /// </summary>
    internal class TestMethodValidator
    {
        private readonly ReflectHelper reflectHelper;
        private readonly bool discoverInternals;
        private readonly Type type;
        private readonly TypeInfo typeInfo;
        private readonly IReadOnlyCollection<Attribute> typeAttributes;

        /// <summary>
        /// Initializes a new instance of the <see cref="TestMethodValidator"/> class.
        /// </summary>
        /// <param name="type">The type to enumerate.</param
        /// <param name="typeAttributes">Type attributed defined on this assembly.</param>
        /// <param name="reflectHelper">An instance to reflection helper for type information.</param>
        /// <param name="discoverInternals">True to discover methods which are declared internal in addition to methods
        /// which are declared public.</param>
        internal TestMethodValidator(Type type, IReadOnlyCollection<Attribute> typeAttributes, ReflectHelper reflectHelper, bool discoverInternals)
        {
            this.type = type;
            this.typeInfo = type.GetTypeInfo();
            this.typeAttributes = typeAttributes;
            this.reflectHelper = reflectHelper;
            this.discoverInternals = discoverInternals;
        }

        /// <summary>
        /// Determines if a method is a valid test method.
        /// </summary>
        /// <param name="testMethodInfo"> The reflected method. </param>
        /// <param name="methodAttributes"> Method attributes. </param>
        /// <param name="type"> The reflected type. </param>
        /// <param name="warnings"> Contains warnings if any, that need to be passed back to the caller. </param>
        /// <returns> Return true if a method is a valid test method. </returns>
        internal virtual bool IsValidTestMethod(MethodInfo testMethodInfo, IReadOnlyCollection<Attribute> methodAttributes, Type type, ICollection<string> warnings)
        {
            type = this.type;
            if (!this.HasAttribute(methodAttributes,  typeof(TestMethodAttribute))
                && !this.HasAttributeDerivedFrom(methodAttributes, typeof(TestMethodAttribute)))
            {
                return false;
            }

            // Generic method Definitions are not valid.
            if (testMethodInfo.IsGenericMethodDefinition)
            {
                var message = string.Format(CultureInfo.CurrentCulture, Resource.UTA_ErrorGenericTestMethod, testMethodInfo.DeclaringType.FullName, testMethodInfo.Name);
                warnings.Add(message);
                return false;
            }

            var isAccessible = testMethodInfo.IsPublic
                || (this.discoverInternals && testMethodInfo.IsAssembly);

            // Todo: Decide whether parameter count matters.
            // The isGenericMethod check below id to verify that there are no closed generic methods slipping through.
            // Closed generic methods being GenericMethod<int> and open being GenericMethod<T>.
            var isValidTestMethod = isAccessible && !testMethodInfo.IsAbstract && !testMethodInfo.IsStatic
                                    && !testMethodInfo.IsGenericMethod
                                    && testMethodInfo.IsVoidOrTaskReturnType();

            if (!isValidTestMethod)
            {
                var message = string.Format(CultureInfo.CurrentCulture, Resource.UTA_ErrorIncorrectTestMethodSignature, type.FullName, testMethodInfo.Name);
                warnings.Add(message);
                return false;
            }

            return true;
        }

        internal bool HasAttribute(IReadOnlyCollection<Attribute> attributes, Type attributeType)
        {
            foreach (var attribute in attributes)
            {
                if (attribute.GetType() == attributeType)
                {
                    return true;
                }
            }

            return false;
        }

        internal bool HasAttributeDerivedFrom(IReadOnlyCollection<Attribute> attributes, Type attributeType)
        {
            var attributeTypeInfo = attributeType.GetTypeInfo();
            foreach (var attribute in this.typeAttributes)
            {
                if (attributeTypeInfo.IsSubclassOf(attribute.GetType()))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
