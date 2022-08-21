﻿// Copyright (c) Autofac Project. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Reflection;

namespace Autofac.Core.Activators.Reflection;

/// <summary>
/// Finds constructors that match a finder function.
/// </summary>
public class DefaultConstructorFinder : IConstructorFinder
{
    private readonly Func<Type, ReflectionCache, ConstructorInfo[]> _finder;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultConstructorFinder" /> class.
    /// </summary>
    /// <remarks>
    /// Default to selecting all public constructors.
    /// </remarks>
    public DefaultConstructorFinder()
      : this(GetDefaultPublicConstructors)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultConstructorFinder" /> class.
    /// </summary>
    /// <param name="finder">The finder function.</param>
    public DefaultConstructorFinder(Func<Type, ReflectionCache, ConstructorInfo[]> finder)
    {
        _finder = finder ?? throw new ArgumentNullException(nameof(finder));
    }

    /// <summary>
    /// Finds suitable constructors on the target type.
    /// </summary>
    /// <param name="targetType">Type to search for constructors.</param>
    /// <returns>Suitable constructors.</returns>
    public ConstructorInfo[] FindConstructors(Type targetType, ReflectionCache reflectionCache)
    {
        return _finder(targetType, reflectionCache);
    }

    private static ConstructorInfo[] GetDefaultPublicConstructors(Type type, ReflectionCache reflectionCache)
    {
        var retval = reflectionCache.Internal.DefaultPublicConstructors
            .GetOrAdd(type, t => t.GetDeclaredPublicConstructors());

        if (retval.Length == 0)
        {
            throw new NoConstructorsFoundException(type);
        }

        return retval;
    }
}
