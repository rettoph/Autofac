﻿// Copyright (c) Autofac Project. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Autofac.Core.Resolving.Pipeline;
using Autofac.Diagnostics;

namespace Autofac.Core.Resolving;

/// <summary>
/// A <see cref="ResolveOperation"/> is a component context that sequences and monitors the multiple
/// activations that go into producing a single requested object graph.
/// </summary>
internal sealed class ResolveOperation : IDependencyTrackingResolveOperation
{
    private const int SuccessListInitialCapacity = 32;
    private bool _ended;

    private readonly List<DefaultResolveRequestContext> _successfulRequests = new(SuccessListInitialCapacity);

    private int _nextCompleteSuccessfulRequestStartPos;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResolveOperation" /> class.
    /// </summary>
    /// <param name="mostNestedLifetimeScope"> The most nested scope in which to begin the operation. The operation
    /// can move upward to less nested scopes as components with wider sharing scopes are activated.
    /// </param>
    /// <param name="diagnosticSource">
    /// The <see cref="System.Diagnostics.DiagnosticListener" /> to which trace events should be written.
    /// </param>
    public ResolveOperation(
        ISharingLifetimeScope mostNestedLifetimeScope,
        DiagnosticListener diagnosticSource)
    {
        CurrentScope = mostNestedLifetimeScope ?? throw new ArgumentNullException(nameof(mostNestedLifetimeScope));
        DiagnosticSource = diagnosticSource ?? throw new ArgumentNullException(nameof(diagnosticSource));
    }

    /// <summary>
    /// Execute the complete resolve operation.
    /// </summary>
    /// <param name="request">The resolution context.</param>
    public object Execute(in ResolveRequest request)
    {
        return ExecuteOperation(request);
    }

    /// <summary>
    /// Gets the active resolve request.
    /// </summary>
    public ResolveRequestContext? ActiveRequestContext { get; private set; }

    /// <summary>
    /// Gets the current lifetime scope of the operation; based on the most recently executed request.
    /// </summary>
    public ISharingLifetimeScope CurrentScope { get; private set; }

    /// <inheritdoc/>
    public IEnumerable<ResolveRequestContext> InProgressRequests => RequestStack;

    /// <summary>
    /// Gets the <see cref="System.Diagnostics.DiagnosticListener" /> for the operation.
    /// </summary>
    public DiagnosticListener DiagnosticSource { get; }

    /// <summary>
    /// Gets the current request depth.
    /// </summary>
    public int RequestDepth { get; private set; }

    /// <summary>
    /// Gets the <see cref="ResolveRequest" /> that initiated the operation. Other nested requests may have been
    /// issued as a result of this one.
    /// </summary>
    public ResolveRequest? InitiatingRequest { get; private set; }

    /// <inheritdoc />
    public event EventHandler<ResolveRequestBeginningEventArgs>? ResolveRequestBeginning;

    /// <inheritdoc />
    public event EventHandler<ResolveOperationEndingEventArgs>? CurrentOperationEnding;

    /// <summary>
    /// Enter a new dependency chain block where subsequent requests inside the operation are allowed to repeat
    /// registrations from before the block.
    /// </summary>
    /// <returns>A disposable that should be disposed to exit the block.</returns>
    public IDisposable EnterNewDependencyDetectionBlock() => RequestStack.EnterSegment();

    /// <inheritdoc/>
    public SegmentedStack<ResolveRequestContext> RequestStack { get; } = new SegmentedStack<ResolveRequestContext>();

    /// <inheritdoc />
    public object GetOrCreateInstance(ISharingLifetimeScope currentOperationScope, in ResolveRequest request)
    {
        return this.GetOrCreateInstance(currentOperationScope, in request, true)!;
    }

    /// <inheritdoc />
    public bool TryGetOrCreateInstance(ISharingLifetimeScope currentOperationScope, in ResolveRequest request, [MaybeNullWhen(false)] out object? instance)
    {
        instance = this.GetOrCreateInstance(currentOperationScope, in request, false);
        return instance is not null;
    }

    private object? GetOrCreateInstance(ISharingLifetimeScope currentOperationScope, in ResolveRequest request, bool required)
    {
        if (_ended)
        {
            throw new ObjectDisposedException(ResolveOperationResources.TemporaryContextDisposed, innerException: null);
        }

        // Create a new request context.
        var requestContext = new DefaultResolveRequestContext(this, request, currentOperationScope, DiagnosticSource, required);

        // Raise our request-beginning event.
        var handler = ResolveRequestBeginning;
        handler?.Invoke(this, new ResolveRequestBeginningEventArgs(requestContext));

        RequestDepth++;

        // Track the last active request and scope in the call stack.
        ResolveRequestContext? lastActiveRequest = ActiveRequestContext;
        var lastScope = CurrentScope;

        ActiveRequestContext = requestContext;
        CurrentScope = currentOperationScope;

        try
        {
            // Same basic flow in if/else, but doing a one-time check for diagnostics
            // and choosing the "diagnostics enabled" version vs. the more common
            // "no diagnostics enabled" path: hot-path optimization.
            if (DiagnosticSource.IsEnabled())
            {
                DiagnosticSource.RequestStart(this, requestContext);
                InvokePipeline(request, requestContext, required);
                DiagnosticSource.RequestSuccess(this, requestContext);
            }
            else
            {
                InvokePipeline(request, requestContext, required);
            }
        }
        catch (Exception ex)
        {
            if (DiagnosticSource.IsEnabled())
            {
                DiagnosticSource.RequestFailure(this, requestContext, ex);
            }

            throw;
        }
        finally
        {
            ActiveRequestContext = lastActiveRequest;
            CurrentScope = lastScope;

            // Raise the appropriate completion events.
            if (RequestStack.Count == 0)
            {
                CompleteRequests();
            }

            RequestDepth--;
        }

        // InvokePipeline throws if the instance is null but
        // analyzers don't pick that up and get mad.
        return requestContext.Instance!;
    }

    /// <summary>
    /// Invoke this method to execute the operation for a given request.
    /// </summary>
    /// <param name="request">The resolve request.</param>
    /// <returns>The resolved instance.</returns>
    private object ExecuteOperation(in ResolveRequest request)
    {
        object result;

        try
        {
            InitiatingRequest = request;
            if (DiagnosticSource.IsEnabled())
            {
                DiagnosticSource.OperationStart(this, request);
            }

            result = GetOrCreateInstance(CurrentScope, request);
        }
        catch (ObjectDisposedException disposeException)
        {
            if (DiagnosticSource.IsEnabled())
            {
                DiagnosticSource.OperationFailure(this, disposeException);
            }

            throw;
        }
        catch (DependencyResolutionException dependencyResolutionException)
        {
            if (DiagnosticSource.IsEnabled())
            {
                DiagnosticSource.OperationFailure(this, dependencyResolutionException);
            }

            End(dependencyResolutionException);
            throw;
        }
        catch (Exception exception)
        {
            End(exception);
            if (DiagnosticSource.IsEnabled())
            {
                DiagnosticSource.OperationFailure(this, exception);
            }

            throw new DependencyResolutionException(ResolveOperationResources.ExceptionDuringResolve, exception);
        }
        finally
        {
            ResetSuccessfulRequests();
        }

        End();

        if (DiagnosticSource.IsEnabled())
        {
            DiagnosticSource.OperationSuccess(this, result);
        }

        return result;
    }

    /// <summary>
    /// Basic pipeline invocation steps used when retrieving an instance. Isolated
    /// to enable it to be optionally surrounded with diagnostics.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InvokePipeline(in ResolveRequest request, DefaultResolveRequestContext requestContext, bool required)
    {
        request.ResolvePipeline.Invoke(requestContext);
        if (requestContext.Instance == null && required == true)
        {
            throw new DependencyResolutionException(ResolveOperationResources.PipelineCompletedWithNoInstance);
        }

        _successfulRequests.Add(requestContext);
    }

    private void CompleteRequests()
    {
        var completed = _successfulRequests;
        var count = completed.Count;
        var startPosition = _nextCompleteSuccessfulRequestStartPos;
        ResetSuccessfulRequests();

        for (var i = startPosition; i < count; i++)
        {
            completed[i].CompleteRequest();
        }
    }

    private void ResetSuccessfulRequests()
    {
        _nextCompleteSuccessfulRequestStartPos = _successfulRequests.Count;
    }

    private void End(Exception? exception = null)
    {
        if (_ended)
        {
            return;
        }

        _ended = true;
        var handler = CurrentOperationEnding;
        handler?.Invoke(this, new ResolveOperationEndingEventArgs(this, exception));
    }
}
