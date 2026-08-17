// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias ghactions;

using ghactions::Microsoft.Testing.Extensions.GitHubActionsReport;

using Microsoft.Testing.Platform.Extensions.ArtifactPostProcessing;
using Microsoft.Testing.Platform.Helpers;

using Moq;

using GitHubActionsTerminalKind = ghactions::Microsoft.Testing.Extensions.TerminalKind;
using GitHubActionsTestRecord = ghactions::Microsoft.Testing.Extensions.TestRecord;
using GitHubCiRunSummaryAggregate = ghactions::Microsoft.Testing.Extensions.CiRunSummaryAggregate;
using GitHubCiRunSummaryModule = ghactions::Microsoft.Testing.Extensions.CiRunSummaryModule;
using GitHubCiRunSummaryTest = ghactions::Microsoft.Testing.Extensions.CiRunSummaryTest;

namespace Microsoft.Testing.Extensions.UnitTests;

[TestClass]
public sealed class GitHubActionsSummaryReporterTests
{
    // ExitCode.Success (0): a normal passing run — no exit-code callout expected.
    private const int SuccessExitCode = 0;

    // ExitCode.AtLeastOneTestFailed (2): failures are conveyed by the table/list, not a callout.
    private const int AtLeastOneTestFailedExitCode = 2;

    // ExitCode.ZeroTests (8) and MinimumExpectedTestsPolicyViolation (9): non-test-result failures.
    private const int ZeroTestsExitCode = 8;
    private const int MinimumExpectedTestsExitCode = 9;

    [TestMethod]
    public void BuildMarkdown_AllPassing_UsesSuccessIconAndTotals()
    {
        GitHubActionsTestRecord[] records =
        [
            new("Add", "CalculatorTests.Add", GitHubActionsTerminalKind.Passed, TimeSpan.FromMilliseconds(10)),
            new("Sub", "CalculatorTests.Sub", GitHubActionsTerminalKind.Passed, TimeSpan.FromMilliseconds(20)),
            new("Skip", "CalculatorTests.Skip", GitHubActionsTerminalKind.Skipped, TimeSpan.Zero),
        ];

        string markdown = GitHubActionsSummaryReporter.BuildMarkdown(records, "CalculatorTests", "net9.0", SuccessExitCode);

        Assert.Contains("## ✅ Test Run Summary — CalculatorTests (net9.0)", markdown);
        Assert.Contains("| 3 | 2 | 0 | 1 | 30ms |", markdown);
        Assert.DoesNotContain("### ❌ Failures", markdown);
        Assert.DoesNotContain("[!WARNING]", markdown);
    }

    [TestMethod]
    public void BuildMarkdown_WithFailures_UsesFailureIconAndListsFailures()
    {
        GitHubActionsTestRecord[] records =
        [
            new("Pass", "StringUtilsTests.Pass", GitHubActionsTerminalKind.Passed, TimeSpan.FromMilliseconds(5)),
            new("Boom", "StringUtilsTests.Boom", GitHubActionsTerminalKind.Failed, TimeSpan.FromMilliseconds(7)),
        ];

        string markdown = GitHubActionsSummaryReporter.BuildMarkdown(records, "StringUtilsTests", "net9.0", AtLeastOneTestFailedExitCode);

        Assert.Contains("## ❌ Test Run Summary — StringUtilsTests (net9.0)", markdown);
        Assert.Contains("### ❌ Failures (1)", markdown);
        Assert.Contains("<summary><code>StringUtilsTests.Boom</code> — 7ms</summary>", markdown);

        // A plain "at least one test failed" outcome is conveyed by the failures list, not an exit-code callout.
        Assert.DoesNotContain("[!WARNING]", markdown);
    }

    [TestMethod]
    public void BuildMarkdown_FailureDetailsAreEncodedAndStackTraceIsSafelyFenced()
    {
        GitHubActionsTestRecord[] records =
        [
            new(
                "Boom",
                "Tests.<script>&Boom",
                GitHubActionsTerminalKind.Failed,
                TimeSpan.FromSeconds(2.4),
                explanation: "Expected <42>\nActual 41",
                exceptionMessage: "exception fallback",
                exceptionType: "Xunit.Sdk.EqualException",
                stackTrace: "line one\n```embedded fence```\nline three",
                sourceFilePath: "Tests/Test<Class>.cs",
                sourceLineNumber: 42),
        ];

        string markdown = GitHubActionsSummaryReporter.BuildMarkdown(records, "Tests", "net9.0", AtLeastOneTestFailedExitCode);

        Assert.Contains("<summary><code>Tests.&lt;script&gt;&amp;Boom</code> — 2.40s</summary>", markdown);
        Assert.DoesNotContain("<summary><code>Tests.<script>", markdown);
        Assert.Contains("**Exception:** `Xunit.Sdk.EqualException`", markdown);
        Assert.Contains("**Location:** `Tests/Test<Class>.cs:42`", markdown);
        Assert.Contains("Expected <42>\nActual 41", markdown);
        Assert.Contains("````text\nline one\n```embedded fence```\nline three\n````", markdown);
    }

    [TestMethod]
    public void BuildMarkdown_FailureWithMissingDiagnosticsStillRendersCollapsibleSummary()
    {
        GitHubActionsTestRecord[] records =
        [
            new("Boom", "Tests.Boom", GitHubActionsTerminalKind.Failed, TimeSpan.Zero),
        ];

        string markdown = GitHubActionsSummaryReporter.BuildMarkdown(records, "Tests", "net9.0", AtLeastOneTestFailedExitCode);

        Assert.Contains("<summary><code>Tests.Boom</code> — 0ms</summary>", markdown);
        Assert.DoesNotContain("**Exception:**", markdown);
        Assert.DoesNotContain("**Location:**", markdown);
        Assert.DoesNotContain("**Message:**", markdown);
        Assert.DoesNotContain("**Stack trace:**", markdown);
    }

    [TestMethod]
    public void BuildMarkdown_ReportsFailuresOmittedByCap()
    {
        GitHubActionsTestRecord[] records =
        [
            .. Enumerable.Range(1, 21).Select(index => new GitHubActionsTestRecord(
                $"Failure {index}",
                $"Tests.Failure{index}",
                GitHubActionsTerminalKind.Failed,
                TimeSpan.FromMilliseconds(index))),
        ];

        string markdown = GitHubActionsSummaryReporter.BuildMarkdown(records, "Tests", "net9.0", AtLeastOneTestFailedExitCode);

        Assert.Contains("### ❌ Failures (21)", markdown);
        Assert.Contains("_1 additional failure omitted (limit 20)._", markdown);
        Assert.AreEqual(20, CountOccurrences(markdown, "<summary><code>Tests.Failure"));
    }

    [TestMethod]
    public void BuildMarkdown_TruncatesFailureDiagnosticsWithinVisibleBudget()
    {
        GitHubActionsTestRecord[] records =
        [
            new(
                "Boom",
                "Tests.Boom",
                GitHubActionsTerminalKind.Failed,
                TimeSpan.Zero,
                explanation: new string('x', 70 * 1024)),
        ];

        string markdown = GitHubActionsSummaryReporter.BuildMarkdown(records, "Tests", "net9.0", AtLeastOneTestFailedExitCode);

        Assert.Contains("_Failure diagnostics were truncated to fit the 64 KiB summary budget._", markdown);
        Assert.IsLessThan(67 * 1024, markdown.Length);
        Assert.Contains("</details>", markdown);
    }

    [TestMethod]
    public void BuildAggregateMarkdown_RendersEquivalentFailureDiagnosticsAndOmittedCount()
    {
        var failure = new GitHubCiRunSummaryTest
        {
            DisplayName = "Boom",
            FullyQualifiedName = "Tests.<Boom>",
            DurationTicks = TimeSpan.FromMilliseconds(15).Ticks,
            Explanation = "Expected\nActual",
            ExceptionMessage = "fallback",
            ExceptionType = "System.InvalidOperationException",
            StackTrace = "at Tests.Boom()\nat Runner.Main()",
            SourceFilePath = "Tests/Boom.cs",
            SourceLineNumber = 12,
        };
        var module = new GitHubCiRunSummaryModule
        {
            AssemblyName = "Tests",
            ModulePath = "Tests.dll",
            TargetFramework = "net9.0",
            Architecture = "x64",
            ExecutionId = "execution",
            SessionUid = "session",
            AttemptNumber = 1,
            ExitCode = AtLeastOneTestFailedExitCode,
            TotalTests = 21,
            FailedTests = 21,
            Failures = [failure],
        };
        var aggregate = new GitHubCiRunSummaryAggregate(
            [module],
            new ArtifactPostProcessingContext(ArtifactPostProcessingTruncationReason.None),
            totalTests: 21,
            passedTests: 0,
            failedTests: 21,
            skippedTests: 0,
            duration: TimeSpan.FromMilliseconds(15),
            exitCode: AtLeastOneTestFailedExitCode,
            hasAuthoritativeRunSummary: true,
            isPartial: false);
        GitHubActionsTestRecord[] directRecords =
        [
            new(
                failure.DisplayName,
                failure.FullyQualifiedName,
                GitHubActionsTerminalKind.Failed,
                TimeSpan.FromTicks(failure.DurationTicks),
                failure.Explanation,
                failure.ExceptionMessage,
                failure.ExceptionType,
                failure.StackTrace,
                failure.SourceFilePath,
                failure.SourceLineNumber),
        ];

        string direct = GitHubActionsSummaryReporter.BuildMarkdown(directRecords, "Tests", "net9.0", AtLeastOneTestFailedExitCode);
        string aggregateMarkdown = GitHubActionsSummaryReporter.BuildAggregateMarkdown(aggregate);

        string expectedFailureBlock = GetFirstFailureBlock(direct);
        Assert.Contains(expectedFailureBlock, aggregateMarkdown);
        Assert.Contains("<summary><code>Tests.&lt;Boom&gt;</code> — 15ms</summary>", aggregateMarkdown);
        Assert.Contains("_20 additional failures omitted (limit 20)._", aggregateMarkdown);
    }

    [TestMethod]
    public void BuildMarkdown_EmitsSlowestTestsSortedByDuration()
    {
        GitHubActionsTestRecord[] records =
        [
            new("Fast", "T.Fast", GitHubActionsTerminalKind.Passed, TimeSpan.FromMilliseconds(10)),
            new("Slow", "T.Slow", GitHubActionsTerminalKind.Passed, TimeSpan.FromSeconds(65)),
        ];

        string markdown = GitHubActionsSummaryReporter.BuildMarkdown(records, "T", "net9.0", SuccessExitCode);

        Assert.Contains("### ⏱ Slowest tests", markdown);
        int slowIndex = markdown.IndexOf("- `T.Slow` — 1m 05s", StringComparison.Ordinal);
        int fastIndex = markdown.IndexOf("- `T.Fast` — 10ms", StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, slowIndex, markdown);
        Assert.IsGreaterThanOrEqualTo(0, fastIndex, markdown);

        // Slowest-first ordering: the slow test must be listed before the fast one, i.e. at a smaller index.
        // IsLessThan(upperBound, value) asserts value < upperBound, so this asserts slowIndex < fastIndex.
        Assert.IsLessThan(fastIndex, slowIndex, markdown);
    }

    [TestMethod]
    public void BuildMarkdown_NoTests_StillEmitsHeaderAndZeroTotals()
    {
        string markdown = GitHubActionsSummaryReporter.BuildMarkdown([], "Empty", "net9.0", SuccessExitCode);

        Assert.Contains("## ✅ Test Run Summary — Empty (net9.0)", markdown);
        Assert.Contains("| 0 | 0 | 0 | 0 | 0ms |", markdown);
    }

    [TestMethod]
    public void BuildMarkdown_ZeroTestsExitCode_UsesFailureIconAndEmitsCallout()
    {
        // No failing tests, but the process exit code says the run failed because nothing ran.
        string markdown = GitHubActionsSummaryReporter.BuildMarkdown([], "Empty", "net9.0", ZeroTestsExitCode);

        Assert.Contains("## ❌ Test Run Summary — Empty (net9.0)", markdown);
        Assert.Contains("> [!WARNING]", markdown);
        Assert.Contains("Exit code 8 — ZeroTests:", markdown);
    }

    [TestMethod]
    public void BuildMarkdown_MinimumExpectedTestsExitCode_EmitsCalloutEvenWhenTestsPassed()
    {
        GitHubActionsTestRecord[] records =
        [
            new("Add", "CalculatorTests.Add", GitHubActionsTerminalKind.Passed, TimeSpan.FromMilliseconds(10)),
        ];

        string markdown = GitHubActionsSummaryReporter.BuildMarkdown(records, "CalculatorTests", "net9.0", MinimumExpectedTestsExitCode);

        // The single test passed, yet the run failed the minimum-expected-tests policy: icon and callout reflect it.
        Assert.Contains("## ❌ Test Run Summary — CalculatorTests (net9.0)", markdown);
        Assert.Contains("Exit code 9 — MinimumExpectedTestsPolicyViolation:", markdown);
        Assert.Contains("--minimum-expected-tests", markdown);
    }

    [TestMethod]
    public void BuildAggregateMarkdown_HtmlEncodesModuleSummaryLabel()
    {
        var module = new GitHubCiRunSummaryModule
        {
            AssemblyName = "<h1>A&B</h1>",
            ModulePath = "A.dll",
            TargetFramework = "net9.0<&>",
            Architecture = "x64&arm64",
            ExecutionId = "execution",
            SessionUid = "session",
            AttemptNumber = 1,
        };
        var aggregate = new GitHubCiRunSummaryAggregate(
            [module],
            new ArtifactPostProcessingContext(ArtifactPostProcessingTruncationReason.None),
            totalTests: 0,
            passedTests: 0,
            failedTests: 0,
            skippedTests: 0,
            duration: null,
            exitCode: null,
            hasAuthoritativeRunSummary: false,
            isPartial: false);

        string markdown = GitHubActionsSummaryReporter.BuildAggregateMarkdown(aggregate);

        Assert.Contains("<summary>&lt;h1&gt;A&amp;B&lt;/h1&gt; (net9.0&lt;&amp;&gt;, x64&amp;arm64)</summary>", markdown);
        Assert.DoesNotContain("<summary><h1>", markdown);
    }

    [TestMethod]
    public void BuildAggregateMarkdown_PartialFailureUsesFailureIconAndDisambiguatesAttempts()
    {
        var first = new GitHubCiRunSummaryModule
        {
            AssemblyName = "Tests",
            ModulePath = "Tests.dll",
            TargetFramework = "net9.0",
            Architecture = "x64",
            ExecutionId = "execution",
            SessionUid = "session-1",
            AttemptNumber = 1,
            ExitCode = AtLeastOneTestFailedExitCode,
            FailedTests = 1,
            TotalTests = 1,
        };
        var second = new GitHubCiRunSummaryModule
        {
            AssemblyName = "Tests",
            ModulePath = "other/Tests.dll",
            TargetFramework = "net9.0",
            Architecture = "x64",
            ExecutionId = "execution",
            SessionUid = "session-2",
            AttemptNumber = 2,
            ExitCode = SuccessExitCode,
        };
        var aggregate = new GitHubCiRunSummaryAggregate(
            [first, second],
            new ArtifactPostProcessingContext(ArtifactPostProcessingTruncationReason.MaximumFailedTests),
            totalTests: 1,
            passedTests: 0,
            failedTests: 1,
            skippedTests: 0,
            duration: TimeSpan.FromSeconds(1),
            exitCode: AtLeastOneTestFailedExitCode,
            hasAuthoritativeRunSummary: true,
            isPartial: true);

        string markdown = GitHubActionsSummaryReporter.BuildAggregateMarkdown(aggregate);

        Assert.StartsWith("## ❌ Overall Test Run Summary", markdown);
        Assert.Contains("attempt 1, session session-1", markdown);
        Assert.Contains("attempt 2, session session-2", markdown);
        Assert.Contains("This summary is partial because the test run was truncated.", markdown);
    }

    [TestMethod]
    public async Task AppendStepSummaryWithRetryAsync_WritesContent_OnFirstAttempt()
    {
        var buffer = new MemoryStream();
        Mock<IFileSystem> fileSystem = CreateFileSystemWritingTo(buffer);

        await GitHubActionsSummaryReporter.AppendStepSummaryWithRetryAsync(
            fileSystem.Object, "summary.md", "hello world", maxAttempts: 5, retryDelay: TimeSpan.Zero, CancellationToken.None);

        // UTF8Encoding(false) is used by the reporter, so there is no BOM to strip.
        Assert.AreEqual("hello world", Encoding.UTF8.GetString(buffer.ToArray()));
        fileSystem.Verify(f => f.NewFileStream("summary.md", FileMode.Append, FileAccess.Write, FileShare.Read), Times.Once);
    }

    [TestMethod]
    public async Task AppendStepSummaryWithRetryAsync_RetriesOnSharingViolation_ThenSucceeds()
    {
        var buffer = new MemoryStream();
        var fileStream = new Mock<IFileStream>();
        fileStream.Setup(s => s.Stream).Returns(buffer);

        var fileSystem = new Mock<IFileSystem>();
        // First open loses the race against another process (sharing violation), the second one wins.
        fileSystem.SetupSequence(f => f.NewFileStream("summary.md", FileMode.Append, FileAccess.Write, FileShare.Read))
            .Throws(new IOException("The process cannot access the file because it is being used by another process."))
            .Returns(fileStream.Object);

        await GitHubActionsSummaryReporter.AppendStepSummaryWithRetryAsync(
            fileSystem.Object, "summary.md", "second-wins", maxAttempts: 5, retryDelay: TimeSpan.Zero, CancellationToken.None);

        Assert.AreEqual("second-wins", Encoding.UTF8.GetString(buffer.ToArray()));
        fileSystem.Verify(f => f.NewFileStream("summary.md", FileMode.Append, FileAccess.Write, FileShare.Read), Times.Exactly(2));
    }

    [TestMethod]
    public async Task AppendStepSummaryWithRetryAsync_Rethrows_WhenAllAttemptsFail()
    {
        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.NewFileStream("summary.md", FileMode.Append, FileAccess.Write, FileShare.Read))
            .Throws(new IOException("locked"));

        // After exhausting the bounded attempts the final IOException propagates so the caller can surface its
        // best-effort warning rather than looping forever.
        await Assert.ThrowsExactlyAsync<IOException>(() => GitHubActionsSummaryReporter.AppendStepSummaryWithRetryAsync(
            fileSystem.Object, "summary.md", "never-written", maxAttempts: 3, retryDelay: TimeSpan.Zero, CancellationToken.None));

        fileSystem.Verify(f => f.NewFileStream("summary.md", FileMode.Append, FileAccess.Write, FileShare.Read), Times.Exactly(3));
    }

    [TestMethod]
    public async Task AppendStepSummaryWithRetryAsync_DoesNotRetry_WhenWriteFailsAfterHandleAcquired()
    {
        // The handle is acquired successfully but the write/flush fails (e.g. disk full). Retrying would re-append
        // the full section on top of a partial one, so the failure must propagate after a single attempt.
        var fileStream = new Mock<IFileStream>();
        fileStream.Setup(s => s.Stream).Returns(new ThrowOnWriteStream());

        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.NewFileStream("summary.md", FileMode.Append, FileAccess.Write, FileShare.Read))
            .Returns(fileStream.Object);

        await Assert.ThrowsExactlyAsync<IOException>(() => GitHubActionsSummaryReporter.AppendStepSummaryWithRetryAsync(
            fileSystem.Object, "summary.md", "partial", maxAttempts: 5, retryDelay: TimeSpan.Zero, CancellationToken.None));

        // Exactly one acquisition: a post-acquire write failure is not contention and must not be retried.
        fileSystem.Verify(f => f.NewFileStream("summary.md", FileMode.Append, FileAccess.Write, FileShare.Read), Times.Once);
    }

    [TestMethod]
    public async Task UpsertStepSummaryWithRetryAsync_ReplacesMatchingSection()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "existing\n");
            var fileSystem = new SystemFileSystem();

            await GitHubActionsSummaryReporter.UpsertStepSummaryWithRetryAsync(
                fileSystem, path, "run-1", "first", maxAttempts: 1, retryDelay: TimeSpan.Zero, CancellationToken.None);
            await GitHubActionsSummaryReporter.UpsertStepSummaryWithRetryAsync(
                fileSystem, path, "run-1", "second", maxAttempts: 1, retryDelay: TimeSpan.Zero, CancellationToken.None);

            string summary = File.ReadAllText(path);
            Assert.Contains("existing", summary);
            Assert.Contains("second", summary);
            Assert.DoesNotContain("first", summary);
            const string Marker = "microsoft-testing-platform:github-actions:run-1:start";
            int firstMarker = summary.IndexOf(Marker, StringComparison.Ordinal);
            Assert.IsGreaterThanOrEqualTo(0, firstMarker);
            Assert.AreEqual(-1, summary.IndexOf(Marker, firstMarker + Marker.Length, StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Mock<IFileSystem> CreateFileSystemWritingTo(Stream target)
    {
        var fileStream = new Mock<IFileStream>();
        fileStream.Setup(s => s.Stream).Returns(target);

        var fileSystem = new Mock<IFileSystem>();
        fileSystem.Setup(f => f.NewFileStream(It.IsAny<string>(), FileMode.Append, FileAccess.Write, FileShare.Read))
            .Returns(fileStream.Object);
        return fileSystem;
    }

    private static int CountOccurrences(string value, string substring)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(substring, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += substring.Length;
        }

        return count;
    }

    private static string GetFirstFailureBlock(string markdown)
    {
        int start = markdown.IndexOf("<details>\n<summary><code>", StringComparison.Ordinal);
        int end = markdown.IndexOf("</details>", start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        Assert.IsGreaterThanOrEqualTo(0, end);
        return markdown.Substring(start, end + "</details>".Length - start);
    }

    // A writable stream that fails on any attempt to write or flush, simulating a mid-write I/O error (e.g. disk full)
    // after the exclusive append handle has already been acquired.
    private sealed class ThrowOnWriteStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => 0;

            // Position is not settable on this write-only, non-seekable test stream. The discard makes the
            // otherwise-ignored assigned value explicit so static analysis doesn't flag it.
            set => _ = value;
        }

        public override void Flush() => throw new IOException("There is not enough space on the disk.");

        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("There is not enough space on the disk.");

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
