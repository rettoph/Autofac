// Copyright (c) Autofac Project. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Autofac.Core;
using Autofac.Core.Lifetime;

namespace Autofac.Test.Core.Lifetime;

public class MatchingScopeLifetimeTests
{
    [Fact]
    public void WhenNoMatchingScopeIsPresent_TheExceptionMessageIncludesTheTag()
    {
        var container = Factory.CreateEmptyContainer();
        const string tag = "abcdefg";
        var msl = new MatchingScopeLifetime(tag);
        var rootScope = (ISharingLifetimeScope)container.Resolve<ILifetimeScope>();

        var ex = Assert.Throws<DependencyResolutionException>(() => msl.FindScope(rootScope));
        Assert.Contains(tag, ex.Message);
    }

    [Fact]
    public void WhenNoMatchingScopeIsPresent_TheExceptionMessageIncludesTheTags()
    {
        var container = Factory.CreateEmptyContainer();
        const string tag1 = "abc";
        const string tag2 = "def";
        var msl = new MatchingScopeLifetime(tag1, tag2);
        var rootScope = (ISharingLifetimeScope)container.Resolve<ILifetimeScope>();

        var ex = Assert.Throws<DependencyResolutionException>(() => msl.FindScope(rootScope));
        Assert.Contains(string.Format("{0}, {1}", tag1, tag2), ex.Message);
    }

    [Fact]
    public void WhenTagsToMatchIsNull_ExceptionThrown()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new MatchingScopeLifetime(null));

        Assert.Equal("lifetimeScopeTagsToMatch", exception.ParamName);
    }

    [Fact]
    public void MatchesAgainstSingleTaggedScope()
    {
        const string tag = "Tag";
        var msl = new MatchingScopeLifetime(tag);
        var container = Factory.CreateEmptyContainer();
        var lifetimeScope = (ISharingLifetimeScope)container.BeginLifetimeScope(tag);

        Assert.Equal(lifetimeScope, msl.FindScope(lifetimeScope));
    }

    [Fact]
    public void MatchesAgainstMultipleTaggedScopes()
    {
        const string tag1 = "Tag1";
        const string tag2 = "Tag2";

        var msl = new MatchingScopeLifetime(tag1, tag2);
        var container = Factory.CreateEmptyContainer();

        var tag1Scope = (ISharingLifetimeScope)container.BeginLifetimeScope(tag1);
        Assert.Equal(tag1Scope, msl.FindScope(tag1Scope));

        var tag2Scope = (ISharingLifetimeScope)container.BeginLifetimeScope(tag2);
        Assert.Equal(tag2Scope, msl.FindScope(tag2Scope));
    }

    [Fact]
    public void WhenTryNoMatchingScopeIsPresent_ReturnsNull()
    {
        var container = Factory.CreateEmptyContainer();
        const string tag = "abcdefg";
        var msl = new MatchingScopeLifetime(tag);
        var rootScope = (ISharingLifetimeScope)container.Resolve<ILifetimeScope>();

        var result = msl.TryFindScope(rootScope, out var scope);

        Assert.False(result);
        Assert.Null(scope);
    }

    [Fact]
    public void TryMatchesAgainstSingleTaggedScope()
    {
        const string tag = "Tag";
        var msl = new MatchingScopeLifetime(tag);
        var container = Factory.CreateEmptyContainer();
        var lifetimeScope = (ISharingLifetimeScope)container.BeginLifetimeScope(tag);

        var result = msl.TryFindScope(lifetimeScope, out var scope);

        Assert.True(result);
        Assert.Equal(lifetimeScope, scope);
    }

    [Fact]
    public void TryMatchesAgainstMultipleTaggedScopes()
    {
        const string tag1 = "Tag1";
        const string tag2 = "Tag2";

        var msl = new MatchingScopeLifetime(tag1, tag2);
        var container = Factory.CreateEmptyContainer();


        var tag1Scope = (ISharingLifetimeScope)container.BeginLifetimeScope(tag1);
        var result1 = msl.TryFindScope(tag1Scope, out var scope1);
        Assert.True(result1);
        Assert.Equal(tag1Scope, scope1);

        var tag2Scope = (ISharingLifetimeScope)container.BeginLifetimeScope(tag2);
        var result2 = msl.TryFindScope(tag2Scope, out var scope2);
        Assert.True(result2);
        Assert.Equal(tag2Scope, scope2);
    }
}
