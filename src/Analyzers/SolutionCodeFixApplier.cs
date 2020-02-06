﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the MIT license.  See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.Extensions.Logging;

namespace Microsoft.CodeAnalysis.Tools.Analyzers
{
    internal class SolutionCodeFixApplier : ICodeFixApplier
    {
        public async Task<Solution> ApplyCodeFixesAsync(
            Solution solution,
            CodeAnalysisResult result,
            CodeFixProvider codeFix,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var fixAllProvider = codeFix.GetFixAllProvider();
            if (!fixAllProvider.GetSupportedFixAllScopes().Contains(FixAllScope.Solution))
            {
                throw new InvalidOperationException($"Code fix {codeFix.GetType()} doesn't support Fix All in Solution");
            }

            var project = solution.Projects.FirstOrDefault();
            if (project == null)
            {
                throw new InvalidOperationException($"Solution {solution} has no projects");
            }

            var fixAllContext = new FixAllContext(
                project: project,
                codeFixProvider: codeFix,
                scope: FixAllScope.Solution,
                codeActionEquivalenceKey: null,
                diagnosticIds: codeFix.FixableDiagnosticIds,
                fixAllDiagnosticProvider: new DiagnosticProvider(result),
                cancellationToken: cancellationToken);

            var action = await fixAllProvider.GetFixAsync(fixAllContext);
            var operations = await (action?.GetOperationsAsync(cancellationToken) ?? Task.FromResult(ImmutableArray<CodeActionOperation>.Empty));
            var applyChangesOperation = operations.OfType<ApplyChangesOperation>().SingleOrDefault();
            return applyChangesOperation?.ChangedSolution ?? solution;
        }

        private class DiagnosticProvider : FixAllContext.DiagnosticProvider
        {
            private static readonly Task<IEnumerable<Diagnostic>> EmptyDignosticResult = Task.FromResult(Enumerable.Empty<Diagnostic>());
            private readonly IReadOnlyDictionary<Project, List<Diagnostic>> _diagnosticsByProject;

            internal DiagnosticProvider(CodeAnalysisResult analysisResult)
            {
                _diagnosticsByProject = analysisResult.Diagnostics;
            }

            public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            {
                return GetProjectDiagnosticsAsync(project, cancellationToken);
            }

            public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document, CancellationToken cancellationToken)
            {
                throw new NotImplementedException();
            }

            public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project, CancellationToken cancellationToken)
            {
                return _diagnosticsByProject.ContainsKey(project)
                    ? Task.FromResult<IEnumerable<Diagnostic>>(_diagnosticsByProject[project])
                    : EmptyDignosticResult;
            }
        }
    }
}
