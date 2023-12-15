﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;

namespace Microsoft.Testing.TestInfrastructure;

public sealed class TestHostResult(string command, int exitCode, string standardOutput, ReadOnlyCollection<string> standardOutputLines, string standardError, ReadOnlyCollection<string> standardErrorLines)
{
    public string Command { get; } = command;

    public int ExitCode { get; } = exitCode;

    public string StandardOutput { get; } = standardOutput;

    public ReadOnlyCollection<string> StandardOutputLines { get; } = standardOutputLines;

    public string StandardError { get; } = standardError;

    public ReadOnlyCollection<string> StandardErrorLines { get; } = standardErrorLines;

    public override string ToString()
    {
        var stringBuilder = new StringBuilder();
        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"Command: {Command}");
        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"====================");
        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"ExitCode: {ExitCode}");
        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"====================");
        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"StandardOutput:\n{StandardOutput}");
        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"====================");
        stringBuilder.AppendLine(CultureInfo.InvariantCulture, $"StandardError:\n{StandardError}");

        return stringBuilder.ToString();
    }
}