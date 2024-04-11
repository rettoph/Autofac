﻿// Copyright (c) Autofac Project. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Autofac.Features.Decorators;

namespace Autofac.Core.Resolving.Pipeline;

/// <summary>
///     Context area for a resolve request.
/// </summary>
internal sealed class DefaultResolveRequestContext : ResolveRequestContext
{
    private readonly ResolveRequest _resolveRequest;
    private object? _instance;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultResolveRequestContext" /> class.
    /// </summary>
    /// <param name="owningOperation">The owning resolve operation.</param>
    /// <param name="request">The initiating resolve request.</param>
    /// <param name="scope">The lifetime scope.</param>
    /// <param name="diagnosticSource">
    /// The <see cref="System.Diagnostics.DiagnosticListener" /> to which trace events should be written.
    /// </param>
    /// <param name="required">whether or not resolving instance is required.<summary>
    /// </summary></param>
    internal DefaultResolveRequestContext(
        IResolveOperation owningOperation,
        in ResolveRequest request,
        ISharingLifetimeScope scope,
        DiagnosticListener diagnosticSource,
        bool required)
    {
        Operation = owningOperation;
        ActivationScope = scope;
        Parameters = request.Parameters;
        _resolveRequest = request;
        PhaseReached = PipelinePhase.ResolveRequestStart;
        DiagnosticSource = diagnosticSource;
        Required = required;
    }

    /// <inheritdoc />
    public override IResolveOperation Operation { get; }

    /// <inheritdoc />
    public override ISharingLifetimeScope ActivationScope { get; protected set; }

    /// <inheritdoc />
    public override IComponentRegistration Registration => _resolveRequest.Registration;

    /// <inheritdoc />
    public override Service Service => _resolveRequest.Service;

    /// <inheritdoc />
    public override IComponentRegistration? DecoratorTarget => _resolveRequest.DecoratorTarget;

    /// <inheritdoc />
    [DisallowNull]
    public override object? Instance
    {
        get => _instance;
        set => _instance = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc />
    public override bool NewInstanceActivated => Instance is not null && PhaseReached == PipelinePhase.Activation;

    /// <inheritdoc />
    public override DiagnosticListener DiagnosticSource { get; }

    /// <inheritdoc />
    public override IEnumerable<Parameter> Parameters { get; protected set; }

    /// <inheritdoc />
    public override PipelinePhase PhaseReached { get; set; }

    /// <inheritdoc />
    public override IComponentRegistry ComponentRegistry => ActivationScope.ComponentRegistry;

    /// <inheritdoc />
    public override event EventHandler<ResolveRequestCompletingEventArgs>? RequestCompleting;

    /// <inheritdoc />
    public override DecoratorContext? DecoratorContext { get; set; }

    /// <inheritdoc />
    public override bool Required { get; protected set; }

    /// <inheritdoc />
    public override void ChangeScope(ISharingLifetimeScope newScope) =>
        ActivationScope = newScope ?? throw new ArgumentNullException(nameof(newScope));

    /// <inheritdoc />
    public override void ChangeParameters(IEnumerable<Parameter> newParameters) =>
        Parameters = newParameters ?? throw new ArgumentNullException(nameof(newParameters));

    /// <inheritdoc />
    public override object ResolveComponent(in ResolveRequest request) =>
        Operation.GetOrCreateInstance(ActivationScope, request);

    /// <inheritdoc />
    public override bool TryResolveComponent(in ResolveRequest request, [MaybeNullWhen(false)] out object? component)
        => Operation.TryGetOrCreateInstance(ActivationScope, in request, out component);

    /// <summary>
    /// Complete the request, raising any appropriate events.
    /// </summary>
    public void CompleteRequest()
    {
        EventHandler<ResolveRequestCompletingEventArgs>? handler = RequestCompleting;
        handler?.Invoke(this, new ResolveRequestCompletingEventArgs(this));
    }
}
