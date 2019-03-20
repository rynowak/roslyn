﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.CodeAnalysis.Host
{
    /// <summary>
    /// Provider for the <see cref="DynamicFileInfo"/>
    /// 
    /// implementer of this service should be pure free-thread meaning it can't switch to UI thread underneath.
    /// otherwise, we can get into dead lock if we wait for the dynamic file info from UI thread
    /// </summary>
    internal interface IDynamicFileInfoProvider
    {
        /// <summary>
        /// return <see cref="DynamicFileInfo"/> for the context given
        /// </summary>
        /// <param name="projectId"><see cref="ProjectId"/> this file belongs to</param>
        /// <param name="projectFilePath">full path to project file (ex, csproj)</param>
        /// <param name="filePath">full path to non source file (ex, cshtml)</param>
        /// <returns>null if this provider can't handle the given file</returns>
        Task<DynamicFileInfo> GetDynamicFileInfoAsync(ProjectId projectId, string projectFilePath, string filePath, CancellationToken cancellationToken);

        /// <summary>
        /// let provider know certain file has been removed
        /// </summary>
        /// <param name="projectId"><see cref="ProjectId"/> this file belongs to</param>
        /// <param name="projectFilePath">full path to project file (ex, csproj)</param>
        /// <param name="filePath">full path to non source file (ex, cshtml)</param>
        Task RemoveDynamicFileInfoAsync(ProjectId projectId, string projectFilePath, string filePath, CancellationToken cancellationToken);

        /// <summary>
        /// indicate content of a file has updated. the event argument "string" should be same as "filepath" given to <see cref="GetDynamicFileInfoAsync(ProjectId, string, string, CancellationToken)"/>
        /// </summary>
        event EventHandler<DynamicFileChangeEventArgs> Updated;
    }

    internal class DynamicFileChangeEventArgs : EventArgs
    {
        public DynamicFileChangeEventArgs(string filePath)
        {
            FilePath = filePath;
        }

        public DynamicFileChangeEventArgs(string filePath, SourceTextContainer textContainer)
        {
            FilePath = filePath;
            TextContainer = textContainer;
        }

        public string FilePath { get; }

        public SourceTextContainer TextContainer { get; }
    }
}
