﻿// Copyright (c) Autofac Project. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
#if NET5_0_OR_GREATER
using System.Runtime.Loader;
#endif
using Autofac.Builder;
using Autofac.Core.Registration;
using Autofac.Core.Resolving;
using Autofac.Util;

namespace Autofac.Core.Lifetime;

/// <summary>
/// Lifetime scope implementation.
/// </summary>
[DebuggerDisplay("Tag = {Tag}, IsDisposed = {IsDisposed}")]
[SuppressMessage("Microsoft.ApiDesignGuidelines", "CA2213", Justification = "The creator of the parent lifetime scope is responsible for disposal.")]
public class LifetimeScope : Disposable, ISharingLifetimeScope, IServiceProvider
{
    /// <summary>
    /// Protects shared instances from concurrent access. Other members and the base class are thread-safe.
    /// </summary>
    private readonly object _synchRoot = new();
    private readonly ConcurrentDictionary<Guid, object> _sharedInstances = new();
    private readonly ConcurrentDictionary<(Guid, Guid), object> _sharedQualifiedInstances = new();
    private object? _anonymousTag;
    private LifetimeScope? _parentScope;

    /// <summary>
    /// Gets the id of the lifetime scope self-registration.
    /// </summary>
    internal static Guid SelfRegistrationId { get; } = Guid.NewGuid();

    /// <summary>
    /// The tag applied to root scopes when no other tag is specified.
    /// </summary>
    public static readonly object RootTag = "root";

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private object MakeAnonymousTag() => _anonymousTag = new object();

    /// <summary>
    /// Initializes a new instance of the <see cref="LifetimeScope"/> class.
    /// </summary>
    /// <param name="tag">The tag applied to the <see cref="ILifetimeScope"/>.</param>
    /// <param name="componentRegistry">Components used in the scope.</param>
    /// <param name="parent">Parent scope.</param>
    protected LifetimeScope(IComponentRegistry componentRegistry, LifetimeScope parent, object tag)
    {
        ComponentRegistry = componentRegistry ?? throw new ArgumentNullException(nameof(componentRegistry));
        Tag = tag ?? throw new ArgumentNullException(nameof(tag));
        _parentScope = parent ?? throw new ArgumentNullException(nameof(parent));

        _sharedInstances[SelfRegistrationId] = this;
        RootLifetimeScope = _parentScope.RootLifetimeScope;
        DiagnosticSource = _parentScope.DiagnosticSource;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LifetimeScope"/> class.
    /// </summary>
    /// <param name="tag">The tag applied to the <see cref="ILifetimeScope"/>.</param>
    /// <param name="componentRegistry">Components used in the scope.</param>
    public LifetimeScope(IComponentRegistry componentRegistry, object tag)
    {
        ComponentRegistry = componentRegistry ?? throw new ArgumentNullException(nameof(componentRegistry));
        Tag = tag ?? throw new ArgumentNullException(nameof(tag));

        _sharedInstances[SelfRegistrationId] = this;
        RootLifetimeScope = this;
        DiagnosticSource = new DiagnosticListener("Autofac");
        Disposer.AddInstanceForDisposal(DiagnosticSource);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LifetimeScope"/> class.
    /// </summary>
    /// <param name="componentRegistry">Components used in the scope.</param>
    public LifetimeScope(IComponentRegistry componentRegistry)
        : this(componentRegistry, RootTag)
    {
    }

    /// <summary>
    /// Begin a new anonymous sub-scope. Instances created via the sub-scope
    /// will be disposed along with it.
    /// </summary>
    /// <returns>A new lifetime scope.</returns>
    public ILifetimeScope BeginLifetimeScope()
    {
        return BeginLifetimeScope(MakeAnonymousTag());
    }

    /// <summary>
    /// Begin a new tagged sub-scope. Instances created via the sub-scope
    /// will be disposed along with it.
    /// </summary>
    /// <param name="tag">The tag applied to the <see cref="ILifetimeScope"/>.</param>
    /// <returns>A new lifetime scope.</returns>
    public ILifetimeScope BeginLifetimeScope(object tag)
    {
        CheckNotDisposed();
        CheckTagIsUnique(tag);

        var scope = new LifetimeScope(ComponentRegistry, this, tag);
        RaiseBeginning(scope);
        return scope;
    }

    private void CheckTagIsUnique(object tag)
    {
        if (ReferenceEquals(tag, _anonymousTag))
        {
            return;
        }

        ISharingLifetimeScope parentScope = this;
        while (parentScope != RootLifetimeScope)
        {
            // In the scope where we are searching for parents, then the parent scope will not be null.
            if (parentScope.Tag.Equals(tag))
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.CurrentCulture, LifetimeScopeResources.DuplicateTagDetected, tag));
            }

            // In the scope of searching for tags, the ParentLifetimeScope will always be set.
            parentScope = parentScope.ParentLifetimeScope!;
        }
    }

    [SuppressMessage("CA1030", "CA1030", Justification = "This method raises the event; it's not the event proper.")]
    private void RaiseBeginning(ILifetimeScope scope)
    {
        var handler = ChildLifetimeScopeBeginning;
        handler?.Invoke(this, new LifetimeScopeBeginningEventArgs(scope));
    }

    /// <summary>
    /// Gets the <see cref="System.Diagnostics.DiagnosticListener"/> to which
    /// trace events should be written.
    /// </summary>
    internal DiagnosticListener DiagnosticSource { get; }

    /// <summary>
    /// Begin a new anonymous sub-scope, with additional components available to it.
    /// Component instances created via the new scope
    /// will be disposed along with it.
    /// </summary>
    /// <param name="configurationAction">Action on a <see cref="ContainerBuilder"/>
    /// that adds component registrations visible only in the new scope.</param>
    /// <returns>A new lifetime scope.</returns>
    /// <example>
    /// <code>
    /// IContainer cr = // ...
    /// using (var lifetime = cr.BeginLifetimeScope(builder =&gt; {
    ///         builder.RegisterType&lt;Foo&gt;();
    ///         builder.RegisterType&lt;Bar&gt;().As&lt;IBar&gt;(); })
    /// {
    ///     var foo = lifetime.Resolve&lt;Foo&gt;();
    /// }
    /// </code>
    /// </example>
    public ILifetimeScope BeginLifetimeScope(Action<ContainerBuilder> configurationAction)
    {
        return BeginLifetimeScope(MakeAnonymousTag(), configurationAction);
    }

    /// <summary>
    /// Begin a new tagged sub-scope, with additional components available to it.
    /// Component instances created via the new scope
    /// will be disposed along with it.
    /// </summary>
    /// <param name="tag">The tag applied to the <see cref="ILifetimeScope"/>.</param>
    /// <param name="configurationAction">Action on a <see cref="ContainerBuilder"/>
    /// that adds component registrations visible only in the new scope.</param>
    /// <returns>A new lifetime scope.</returns>
    /// <example>
    /// <code>
    /// IContainer cr = // ...
    /// using (var lifetime = cr.BeginLifetimeScope("unitOfWork", builder =&gt; {
    ///         builder.RegisterType&lt;Foo&gt;();
    ///         builder.RegisterType&lt;Bar&gt;().As&lt;IBar&gt;(); })
    /// {
    ///     var foo = lifetime.Resolve&lt;Foo&gt;();
    /// }
    /// </code>
    /// </example>
    public ILifetimeScope BeginLifetimeScope(object tag, Action<ContainerBuilder> configurationAction)
    {
        return InternalBeginLifetimeScope(tag, configurationAction, isolatedScope: false);
    }

#if NETCOREAPP1_0_OR_GREATER
    /// <inheritdoc />
    public ILifetimeScope BeginLoadContextLifetimeScope(AssemblyLoadContext loadContext, Action<ContainerBuilder> configurationAction)
    {
        return BeginLoadContextLifetimeScope(MakeAnonymousTag(), loadContext, configurationAction);
    }

    /// <inheritdoc />
    public ILifetimeScope BeginLoadContextLifetimeScope(object tag, AssemblyLoadContext loadContext, Action<ContainerBuilder> configurationAction)
    {
        if (loadContext == AssemblyLoadContext.Default)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, LifetimeScopeResources.DefaultLoadContextError, nameof(BeginLoadContextLifetimeScope), nameof(BeginLifetimeScope)));
        }

        var newScope = InternalBeginLifetimeScope(tag, configurationAction, isolatedScope: true);

        newScope.CurrentScopeEnding += (sender, args) =>
        {
            // Clear the reflection cache for those assemblies when the inner scope goes away.
            ReflectionCacheSet.Shared.Clear((cacheKey, referencedAssemblySet) =>
            {
                foreach (var refAssembly in referencedAssemblySet)
                {
                    if (AssemblyLoadContext.GetLoadContext(refAssembly) is AssemblyLoadContext assemblyContext && assemblyContext.Equals(loadContext))
                    {
                        return true;
                    }
                }

                return false;
            });
        };

        return newScope;
    }
#endif

    private LifetimeScope InternalBeginLifetimeScope(object tag, Action<ContainerBuilder> configurationAction, bool isolatedScope)
    {
        if (configurationAction == null)
        {
            throw new ArgumentNullException(nameof(configurationAction));
        }

        CheckNotDisposed();
        CheckTagIsUnique(tag);

        var localsBuilder = CreateScopeRestrictedRegistry(tag, configurationAction, isolatedScope);
        var scope = new LifetimeScope(localsBuilder.Build(), this, tag);
        scope.Disposer.AddInstanceForDisposal(localsBuilder);

        if (localsBuilder.Properties.TryGetValue(MetadataKeys.ContainerBuildOptions, out var options)
            && options is not null
            && !((ContainerBuildOptions)options).HasFlag(ContainerBuildOptions.IgnoreStartableComponents))
        {
            StartableManager.StartStartableComponents(localsBuilder.Properties, scope);
        }

        // Run any build callbacks.
        BuildCallbackManager.RunBuildCallbacks(scope);

        RaiseBeginning(scope);

        return scope;
    }

    /// <summary>
    /// Creates and setup the registry for a child scope.
    /// </summary>
    /// <param name="tag">The tag applied to the <see cref="ILifetimeScope"/>.</param>
    /// <param name="configurationAction">Action on a <see cref="ContainerBuilder"/>
    /// that adds component registrations visible only in the child scope.</param>
    /// <param name="isolatedScope">
    /// Indicates whether the generated registry should be 'isolated'; an isolated registry does not hold on to
    /// any type information for retrieved services that do not result in registrations.
    /// </param>
    /// <returns>Registry to use for a child scope.</returns>
    /// <remarks>It is the responsibility of the caller to make sure that the registry is properly
    /// disposed of. This is generally done by adding the registry to the <see cref="Disposer"/>
    /// property of the child scope.</remarks>
    private ComponentRegistryBuilder CreateScopeRestrictedRegistry(object tag, Action<ContainerBuilder> configurationAction, bool isolatedScope)
    {
        var restrictedRootScopeLifetime = new MatchingScopeLifetime(tag);
        var tracker = new ScopeRestrictedRegisteredServicesTracker(restrictedRootScopeLifetime);

        var fallbackProperties = new FallbackDictionary<string, object?>(ComponentRegistry.Properties);

        foreach (var source in ComponentRegistry.Sources)
        {
            if (source.IsAdapterForIndividualComponents || (source is IPerScopeRegistrationSource && isolatedScope))
            {
                tracker.AddRegistrationSource(source);
            }
        }

        // Issue #272: Only the most nested parent registry with HasLocalComponents is registered as an external source
        // It provides all non-adapting registrations from itself and from it's parent registries
        ISharingLifetimeScope? parent = this;
        while (parent is not null)
        {
            if (parent.ComponentRegistry.HasLocalComponents)
            {
                var externalSource = new ExternalRegistrySource(parent.ComponentRegistry, isolatedScope);
                tracker.AddRegistrationSource(externalSource);

                // Add a source for the service pipeline stages.
                var externalServicePipelineSource = new ExternalRegistryServiceMiddlewareSource(parent.ComponentRegistry, isolatedScope);
                tracker.AddServiceMiddlewareSource(externalServicePipelineSource);

                break;
            }

            parent = parent.ParentLifetimeScope;
        }

        var registryBuilder = new ComponentRegistryBuilder(tracker, fallbackProperties);
        var builder = new ContainerBuilder(fallbackProperties, registryBuilder);

        configurationAction(builder);

        builder.UpdateRegistry(registryBuilder);
        return registryBuilder;
    }

    /// <inheritdoc />
    public object ResolveComponent(in ResolveRequest request)
    {
        CheckNotDisposed();

        var operation = new ResolveOperation(this, DiagnosticSource);
        var handler = ResolveOperationBeginning;
        handler?.Invoke(this, new ResolveOperationBeginningEventArgs(operation));
        return operation.Execute(request);
    }

    /// <inheritdoc />
    public bool TryResolveComponent(in ResolveRequest request, [MaybeNullWhen(false)] out object? component)
    {
        CheckNotDisposed();

        var operation = new ResolveOperation(this, DiagnosticSource);
        var handler = ResolveOperationBeginning;
        handler?.Invoke(this, new ResolveOperationBeginningEventArgs(operation));
        component = operation.Execute(request);

        return component is not null;
    }

    /// <summary>
    /// Gets the parent of this node of the hierarchy, or null.
    /// </summary>
    public ISharingLifetimeScope? ParentLifetimeScope => _parentScope;

    /// <summary>
    /// Gets the root of the sharing hierarchy.
    /// </summary>
    public ISharingLifetimeScope RootLifetimeScope { get; }

    /// <inheritdoc />
    public object CreateSharedInstance(Guid id, Func<object> creator)
    {
        if (creator == null)
        {
            throw new ArgumentNullException(nameof(creator));
        }

        if (_sharedInstances.TryGetValue(id, out var tempResult))
        {
            return tempResult;
        }

        lock (_synchRoot)
        {
            if (_sharedInstances.TryGetValue(id, out var result))
            {
                return result;
            }

            result = creator();
            if (!_sharedInstances.TryAdd(id, result))
            {
                throw new DependencyResolutionException(string.Format(CultureInfo.CurrentCulture, LifetimeScopeResources.SelfConstructingDependencyDetected, result.GetType().FullName));
            }

            return result;
        }
    }

    /// <inheritdoc/>
    public object CreateSharedInstance(Guid primaryId, Guid? qualifyingId, Func<object> creator)
    {
        if (creator == null)
        {
            throw new ArgumentNullException(nameof(creator));
        }

        if (qualifyingId == null)
        {
            return CreateSharedInstance(primaryId, creator);
        }

        var instanceKey = (primaryId, qualifyingId.Value);

        if (_sharedQualifiedInstances.TryGetValue(instanceKey, out var tempResult))
        {
            return tempResult;
        }

        lock (_synchRoot)
        {
            if (_sharedQualifiedInstances.TryGetValue(instanceKey, out var result))
            {
                return result;
            }

            result = creator();
            if (!_sharedQualifiedInstances.TryAdd(instanceKey, result))
            {
                throw new DependencyResolutionException(string.Format(CultureInfo.CurrentCulture, LifetimeScopeResources.SelfConstructingDependencyDetected, result.GetType().FullName));
            }

            return result;
        }
    }

    /// <inheritdoc />
    public bool TryGetSharedInstance(Guid id, [NotNullWhen(true)] out object? value) => _sharedInstances.TryGetValue(id, out value);

    /// <inheritdoc/>
    public bool TryGetSharedInstance(Guid primaryId, Guid? qualifyingId, [NotNullWhen(true)] out object? value)
    {
        return qualifyingId == null
            ? TryGetSharedInstance(primaryId, out value)
            : _sharedQualifiedInstances.TryGetValue((primaryId, qualifyingId.Value), out value);
    }

    /// <summary>
    /// Gets the disposer associated with this container. Instances can be associated
    /// with it manually if required.
    /// </summary>
    public IDisposer Disposer { get; } = new Disposer();

    /// <summary>
    /// Gets the tag applied to the lifetime scope.
    /// </summary>
    /// <remarks>The tag applied to this scope and the contexts generated when
    /// it resolves component dependencies.</remarks>
    public object Tag { get; }

    /// <summary>
    /// Gets the services associated with the components that provide them.
    /// </summary>
    public IComponentRegistry ComponentRegistry { get; }

    /// <summary>
    /// Releases unmanaged and - optionally - managed resources.
    /// </summary>
    /// <param name="disposing"><c>true</c> to release both managed and unmanaged resources; <c>false</c> to release only unmanaged resources.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            var handler = CurrentScopeEnding;

            try
            {
                handler?.Invoke(this, new LifetimeScopeEndingEventArgs(this));
            }
            finally
            {
                Disposer.Dispose();
            }

            // ReSharper disable once InconsistentlySynchronizedField
            _sharedInstances.Clear();
            _parentScope = null;
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc/>
    protected override async ValueTask DisposeAsync(bool disposing)
    {
        if (disposing)
        {
            var handler = CurrentScopeEnding;

            try
            {
                handler?.Invoke(this, new LifetimeScopeEndingEventArgs(this));
            }
            finally
            {
                await Disposer.DisposeAsync().ConfigureAwait(false);
            }

            // ReSharper disable once InconsistentlySynchronizedField
            _sharedInstances.Clear();
            _parentScope = null;
        }

        // Don't call the base (which would just call the normal Dispose).
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckNotDisposed()
    {
        if (IsTreeDisposed())
        {
            // Throw in a separate method to allow this check to be inlined.
            ThrowDisposedException();
        }
    }

    /// <summary>
    /// Gets a value indicating whether this or any of the parent disposables have been disposed.
    /// </summary>
    /// <returns>true if this instance of any of the parent instances have been disposed.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsTreeDisposed()
    {
        return IsDisposed || (_parentScope?.IsTreeDisposed() ?? false);
    }

    /// <summary>
    /// Gets the service object of the specified type.
    /// </summary>
    /// <param name="serviceType">An object that specifies the type of service object
    /// to get.</param>
    /// <returns>
    /// A service object of type <paramref name="serviceType"/>.-or- null if there is
    /// no service object of type <paramref name="serviceType"/>.
    /// </returns>
    public object? GetService(Type serviceType)
    {
        if (serviceType == null)
        {
            throw new ArgumentNullException(nameof(serviceType));
        }

        return this.ResolveOptional(serviceType);
    }

    /// <summary>
    /// Fired when a new scope based on the current scope is beginning.
    /// </summary>
    public event EventHandler<LifetimeScopeBeginningEventArgs>? ChildLifetimeScopeBeginning;

    /// <summary>
    /// Fired when this scope is ending.
    /// </summary>
    public event EventHandler<LifetimeScopeEndingEventArgs>? CurrentScopeEnding;

    /// <summary>
    /// Fired when a resolve operation is beginning in this scope.
    /// </summary>
    public event EventHandler<ResolveOperationBeginningEventArgs>? ResolveOperationBeginning;

    [DoesNotReturn]
    private static void ThrowDisposedException()
    {
        throw new ObjectDisposedException(LifetimeScopeResources.ScopeIsDisposed, innerException: null);
    }
}
