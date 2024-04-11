// Copyright (c) Autofac Project. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Text;
using Autofac.Core.Resolving.Pipeline;

namespace Autofac.Core.Resolving.Middleware;

/// <summary>
/// Selects the correct activation scope based on the registration's lifetime.
/// </summary>
internal class ScopeSelectionMiddleware : IResolveMiddleware
{
    /// <summary>
    /// Gets the singleton instance of the <see cref="ScopeSelectionMiddleware"/>.
    /// </summary>
    public static ScopeSelectionMiddleware Instance => new();

    private ScopeSelectionMiddleware()
    {
        // Only want to use the static instance.
    }

    /// <inheritdoc/>
    public PipelinePhase Phase => PipelinePhase.ScopeSelection;

    /// <inheritdoc/>
    public void Execute(ResolveRequestContext context, Action<ResolveRequestContext> next)
    {
        try
        {
#pragma warning disable CA2000 // Dispose objects before losing scope
            if (context.Required)
            {
                context.ChangeScope(context.Registration.Lifetime.FindScope(context.ActivationScope));
            }
            else if (context.Registration.Lifetime.TryFindScope(context.ActivationScope, out ISharingLifetimeScope? scope))
            {
                context.ChangeScope(scope);
            }
            else
            {
                return;
            }
#pragma warning restore CA2000 // Dispose objects before losing scope
        }
        catch (DependencyResolutionException ex)
        {
            var services = new StringBuilder();
            foreach (var s in context.Registration.Services)
            {
                services.Append("- ");
                services.AppendLine(s.Description);
            }

            var message = string.Format(CultureInfo.CurrentCulture, MiddlewareMessages.UnableToLocateLifetimeScope, context.Registration.Activator.LimitType, services);
            throw new DependencyResolutionException(message, ex);
        }

        next(context);
    }

    /// <inheritdoc/>
    public override string ToString() => nameof(ScopeSelectionMiddleware);
}
