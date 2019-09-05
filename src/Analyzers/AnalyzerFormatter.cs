﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the MIT license.  See License.txt in the project root for license information.

# nullable enable

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Tools.Formatters;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.CodingConventions;

namespace Microsoft.CodeAnalysis.Tools.Analyzers
{
    class AnalyzerFormatter : ICodeFormatter
    {
        public FormatType FormatType => FormatType.CodeStyle;

        private readonly IAnalyzerFinder _finder;
        private readonly IAnalyzerRunner _runner;
        private readonly ICodeFixApplier _applier;

        public AnalyzerFormatter(IAnalyzerFinder finder,
                                 IAnalyzerRunner runner,
                                 ICodeFixApplier applier)
        {
            _finder = finder;
            _runner = runner;
            _applier = applier;
        }

        public async Task<Solution> FormatAsync(Solution solution,
                                                ImmutableArray<(DocumentId, OptionSet, ICodingConventionsSnapshot)> formattableDocuments,
                                                FormatOptions options,
                                                ILogger logger,
                                                CancellationToken cancellationToken)
        {
            var analysisStopwatch = Stopwatch.StartNew();
            logger.LogTrace($"Analyzing code style.");


            if (!options.SaveFormattedFiles)
            {
                await LogDiagnosticsAsync(solution, formattableDocuments, options, logger, cancellationToken);
            }
            else
            {
                solution = await FixDiagnosticsAsync(solution, formattableDocuments, logger, cancellationToken);
            }

            logger.LogTrace("Analysis complete in {0}ms.", analysisStopwatch.ElapsedMilliseconds);

            return solution;
        }

        private async Task LogDiagnosticsAsync(Solution solution, ImmutableArray<(DocumentId, OptionSet, ICodingConventionsSnapshot)> formattableDocuments, FormatOptions options, ILogger logger, CancellationToken cancellationToken)
        {
            var pairs = _finder.GetAnalyzersAndFixers();
            var paths = formattableDocuments.Select(x => solution.GetDocument(x.Item1)!.FilePath!).ToImmutableArray();

            // no need to run codefixes as we won't persist the changes
            var analyzers = pairs.Select(x => x.Analyzer).ToImmutableArray();
            var result = new CodeAnalysisResult();
            await solution.Projects.ForEachAsync(async (project, token) =>
            {
                var options = _finder.GetWorkspaceAnalyzerOptions(project);
                await _runner.RunCodeAnalysisAsync(result, analyzers, project, options, paths, logger, token);
            }, cancellationToken);

            LogDiagnosticLocations(result.Diagnostics.SelectMany(kvp => kvp.Value), options.WorkspaceFilePath, options.ChangesAreErrors, logger);

            return;

            static void LogDiagnosticLocations(IEnumerable<Diagnostic> diagnostics, string workspacePath, bool changesAreErrors, ILogger logger)
            {
                var workspaceFolder = Path.GetDirectoryName(workspacePath);

                foreach (var diagnostic in diagnostics)
                {
                    var message = diagnostic.GetMessage();
                    var filePath = diagnostic.Location.SourceTree.FilePath;

                    var mappedLineSpan = diagnostic.Location.GetMappedLineSpan();
                    var changePosition = mappedLineSpan.StartLinePosition;

                    var formatMessage = $"{Path.GetRelativePath(workspaceFolder, filePath)}({changePosition.Line + 1},{changePosition.Character + 1}): {message}";

                    if (changesAreErrors)
                    {
                        logger.LogError(formatMessage);
                    }
                    else
                    {
                        logger.LogWarning(formatMessage);
                    }
                }
            }
        }

        private async Task<Solution> FixDiagnosticsAsync(Solution solution, ImmutableArray<(DocumentId, OptionSet, ICodingConventionsSnapshot)> formattableDocuments, ILogger logger, CancellationToken cancellationToken)
        {
            var pairs = _finder.GetAnalyzersAndFixers();
            var paths = formattableDocuments.Select(x => solution.GetDocument(x.Item1)!.FilePath!).ToImmutableArray();

            // we need to run each codefix iteratively so ensure that all diagnostics are found and fixed
            foreach (var (analyzer, codefix) in pairs)
            {
                var result = new CodeAnalysisResult();
                await solution.Projects.ForEachAsync(async (project, token) =>
                {
                    var options = _finder.GetWorkspaceAnalyzerOptions(project);
                    await _runner.RunCodeAnalysisAsync(result, analyzer, project, options, paths, logger, token);
                }, cancellationToken);

                var hasDiagnostics = result.Diagnostics.Any(kvp => kvp.Value.Count > 0);
                if (hasDiagnostics)
                {
                    logger.LogTrace($"Applying fixes for {codefix.GetType().Name}");
                    solution = await _applier.ApplyCodeFixesAsync(solution, result, codefix, logger, cancellationToken);
                    var changedSolution = await _applier.ApplyCodeFixesAsync(solution, result, codefix, logger, cancellationToken);
                    if (changedSolution.GetChanges(solution).Any())
                    {
                        solution = changedSolution;
                    }
                }
            }

            return solution;
        }
    }
}
