// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Testing.Platform.Extensions.Messages;
using Microsoft.Testing.Platform.Helpers;
using Microsoft.Testing.Platform.Logging;

namespace Microsoft.Testing.Extensions.GitHubActionsReport;

#pragma warning disable RS0051 // Internal implementation details are exercised through the unit-test assembly alias.

/// <summary>
/// A workspace-relative source location used to pin a GitHub Actions annotation to a file (and, when known,
/// a line) so it renders on the pull request's "Files changed" diff gutter.
/// </summary>
/// <remarks>
/// <see cref="LineNumber"/> is <c>0</c> when the producing test framework reported a file but no usable line
/// (it uses a <c>-1</c> sentinel, or none at all). GitHub accepts a <c>file</c>-only annotation, so callers
/// emit the annotation without a <c>line</c> property in that case rather than fabricating one.
/// </remarks>
internal readonly struct GitHubActionsSourceLocation
{
    public GitHubActionsSourceLocation(string relativeNormalizedPath, int lineNumber)
    {
        RelativeNormalizedPath = relativeNormalizedPath;
        LineNumber = lineNumber;
    }

    /// <summary>
    /// Gets the workspace-relative, forward-slash separated path of the source file.
    /// </summary>
    public string RelativeNormalizedPath { get; }

    /// <summary>
    /// Gets the 1-based line number, or <c>0</c> when the line is unknown.
    /// </summary>
    public int LineNumber { get; }
}

/// <summary>
/// Resolves a failure location consistently for GitHub annotations and step summaries.
/// </summary>
internal static class GitHubActionsSourceLocationResolver
{
    public static GitHubActionsSourceLocation? Resolve(
        TestNode testNode,
        Exception? exception,
        string? repoRoot,
        IFileSystem fileSystem,
        ILogger logger,
        bool skipAssertionFrames)
        => Resolve(
            exception,
            repoRoot,
            fileSystem,
            logger,
            skipAssertionFrames,
            TryResolveDeclaredLocation(testNode, repoRoot, fileSystem));

    public static GitHubActionsSourceLocation? Resolve(
        Exception? exception,
        string? repoRoot,
        IFileSystem fileSystem,
        ILogger logger,
        bool skipAssertionFrames,
        GitHubActionsSourceLocation? declaredLocation)
    {
        (string RelativeNormalizedPath, int LineNumber)? stackLocation = StackTraceSourceLocationResolver.TryResolve(
            exception?.StackTrace,
            repoRoot,
            fileSystem,
            logger,
            skipAssertionFrames);
        return stackLocation is { } resolved
            ? new GitHubActionsSourceLocation(resolved.RelativeNormalizedPath, resolved.LineNumber)
            : declaredLocation;
    }

    public static GitHubActionsSourceLocation? TryResolveDeclaredLocation(TestNode testNode, string? repoRoot, IFileSystem fileSystem)
    {
        if (testNode.Properties.FirstOrDefault<TestFileLocationProperty>() is not { } fileLocation)
        {
            return null;
        }

        string? relativeNormalizedPath = StackTraceSourceLocationResolver.TryMakeWorkspaceRelative(fileLocation.FilePath, repoRoot, fileSystem);
        if (relativeNormalizedPath is null)
        {
            return null;
        }

        int line = fileLocation.LineSpan.Start.Line;
        return new GitHubActionsSourceLocation(relativeNormalizedPath, line > 0 ? line : 0);
    }
}

#pragma warning restore RS0051
