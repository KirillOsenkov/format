﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the MIT license.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.CodeAnalysis.Tools.Workspaces
{
    internal sealed partial class FolderWorkspace : Workspace
    {
        private abstract class ProjectLoader
        {
            public abstract string Language { get; }
            public abstract string FileExtension { get; }
            public virtual string ProjectName => $"{Language}{FileExtension}proj";

            public virtual async Task<ProjectInfo> LoadProjectInfoAsync(string folderPath, ImmutableHashSet<string> filesToInclude, CancellationToken cancellationToken)
            {
                var projectId = ProjectId.CreateNewId(debugName: folderPath);

                var documents = await LoadDocumentInfosAsync(projectId, folderPath, FileExtension, filesToInclude);
                if (documents.IsDefaultOrEmpty)
                {
                    return null;
                }

                return ProjectInfo.Create(
                    projectId,
                    version: default,
                    name: ProjectName,
                    assemblyName: folderPath,
                    Language,
                    filePath: folderPath,
                    documents: documents);
            }

            private static Task<ImmutableArray<DocumentInfo>> LoadDocumentInfosAsync(ProjectId projectId, string folderPath, string fileExtension, ImmutableHashSet<string> filesToInclude)
            {
                return Task.Run(() =>
                {
                    string[] filePaths;
                    if (filesToInclude.Count == 0)
                    {
                        filePaths = Directory.GetFiles(folderPath, $"*{fileExtension}", SearchOption.AllDirectories);
                    }
                    else
                    {
                        filePaths = filesToInclude.Where(
                            filePath => filePath.EndsWith(fileExtension) && File.Exists(filePath)).ToArray();
                    }

                    if (filePaths.Length == 0)
                    {
                        return ImmutableArray<DocumentInfo>.Empty;
                    }

                    var documentInfos = ImmutableArray.CreateBuilder<DocumentInfo>(filePaths.Length);

                    foreach (var filePath in filePaths)
                    {
                        var documentInfo = DocumentInfo.Create(
                            DocumentId.CreateNewId(projectId, debugName: filePath),
                            name: Path.GetFileName(filePath),
                            loader: new FileTextLoader(filePath, DefaultEncoding),
                            filePath: filePath);

                        documentInfos.Add(documentInfo);
                    }

                    return documentInfos.ToImmutableArray();
                });
            }
        }
    }
}
