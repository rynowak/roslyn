﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor.Shared.Utilities;
using Microsoft.CodeAnalysis.Experiment;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServices.Implementation.ProjectSystem;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Venus
{
    using System.Diagnostics;
    using System.Threading;
    using System.Threading.Tasks;
    using Workspace = Microsoft.CodeAnalysis.Workspace;

    /// <summary>
    /// An IVisualStudioDocument which represents the secondary buffer to the workspace API.
    /// </summary>
    internal sealed class SimpleContainedDocument : ForegroundThreadAffinitizedObject, IVisualStudioHostDocument
    {
        /// <summary>
        /// The IDocumentProvider that created us.
        /// </summary>
        private readonly DocumentProvider _documentProvider;
        private readonly SourceTextContainer _sourceTextContainer;
        private readonly ReiteratedVersionSnapshotTracker _snapshotTracker;
        private readonly EventHandler _updatedHandler;

        private ITextBuffer _openTextBuffer;

        public DocumentId Id { get; }
        public IReadOnlyList<string> Folders { get; }
        public AbstractProject Project { get; }
        public SourceCodeKind SourceCodeKind { get; }
        public DocumentKey Key { get; }
        public TextLoader Loader { get; }
        public IDocumentServiceFactory DocumentServiceFactory { get; }

        public SimpleContainedDocument(
            DocumentProvider documentProvider,
            AbstractProject project,
            DocumentKey documentKey,
            Func<uint, IReadOnlyList<string>> getFolderNames,
            SourceTextContainer sourceTextContainer,
            SourceCodeKind sourceCodeKind,
            DocumentId id,
            EventHandler updatedHandler,
            EventHandler<bool> openedHandler,
            EventHandler<bool> closingHandler,
            IDocumentServiceFactory documentServiceFactory)
        {
            Contract.ThrowIfNull(documentProvider);

            this.Project = project;

            this.Key = documentKey;
            this.SourceCodeKind = sourceCodeKind;

            this.Id = id ?? DocumentId.CreateNewId(project.Id, documentKey.Moniker);

            var itemid = this.GetItemId();
            this.Folders = itemid == (uint)VSConstants.VSITEMID.Nil
                ? SpecializedCollections.EmptyReadOnlyList<string>()
                : getFolderNames(itemid);

            _documentProvider = documentProvider;

            this.Loader = new SourceTextContainerTextLoader(sourceTextContainer, this.FilePath);

            _updatedHandler = updatedHandler;
            _sourceTextContainer = sourceTextContainer;
            _sourceTextContainer.TextChanged += OnTextChanged;

            _snapshotTracker = new ReiteratedVersionSnapshotTracker(null);

            this.DocumentServiceFactory = documentServiceFactory;

            if (openedHandler != null)
            {
                Opened += openedHandler;
            }

            if (closingHandler != null)
            {
                Closing += closingHandler;
            }
        }

        public uint GetItemId()
        {
            AssertIsForeground();

            if (this.Key.Moniker == null || Project.Hierarchy == null)
            {
                return (uint)VSConstants.VSITEMID.Nil;
            }

            return Project.Hierarchy.ParseCanonicalName(this.Key.Moniker, out var itemId) == VSConstants.S_OK
                ? itemId
                : (uint)VSConstants.VSITEMID.Nil;
        }

        public DocumentInfo GetInitialState()
        {
            return DocumentInfo.Create(
                this.Id,
                this.Name,
                folders: this.Folders,
                sourceCodeKind: SourceCodeKind,
                loader: this.Loader,
                filePath: this.Key.Moniker,
                isGenerated: true,
                documentServiceFactory: DocumentServiceFactory);
        }

        internal void ProcessOpen(ITextBuffer openedBuffer, bool isCurrentContext)
        {
            Debug.Assert(openedBuffer != null);

            _sourceTextContainer.TextChanged -= OnTextChanged;
            _snapshotTracker.StartTracking(openedBuffer);

            _openTextBuffer = openedBuffer;
            Opened?.Invoke(this, isCurrentContext);
        }

        internal void ProcessClose(bool updateActiveContext)
        {
            // Todo: it might already be closed...
            // For now, continue asserting as it can be clicked through.
            Debug.Assert(_openTextBuffer != null);
            Closing?.Invoke(this, updateActiveContext);

            var buffer = _openTextBuffer;
            _openTextBuffer = null;

            _snapshotTracker.StopTracking(buffer);
            _sourceTextContainer.TextChanged += OnTextChanged;
        }

        public string FilePath => Key.Moniker;
        public bool IsOpen => _openTextBuffer != null;

#pragma warning disable 67

        public event EventHandler UpdatedOnDisk;
        public event EventHandler<bool> Opened;
        public event EventHandler<bool> Closing;

#pragma warning restore 67

        public ITextBuffer GetOpenTextBuffer() => _openTextBuffer;
        public SourceTextContainer GetOpenTextContainer() => _openTextBuffer.AsTextContainer();

        public string Name
        {
            get
            {
                try
                {
                    return Path.GetFileName(this.FilePath);
                }
                catch (ArgumentException)
                {
                    return this.FilePath;
                }
            }
        }

        public void Dispose()
        {
            _sourceTextContainer.TextChanged -= OnTextChanged;

            _documentProvider.StopTrackingDocument(this);
        }

        public void UpdateText(SourceText newText) => throw new NotSupportedException();
        public ITextBuffer GetTextUndoHistoryBuffer() => null;

        private void OnTextChanged(object sender, TextChangeEventArgs e)
        {
            _updatedHandler(this, e);
        }

        private class SourceTextContainerTextLoader : TextLoader
        {
            private readonly SourceTextContainer _sourceTextContainer;
            private readonly string _filePath;

            public SourceTextContainerTextLoader(SourceTextContainer sourceTextContainer, string filePath)
            {
                _sourceTextContainer = sourceTextContainer;
                _filePath = filePath;
            }

            public override Task<TextAndVersion> LoadTextAndVersionAsync(Workspace workspace, DocumentId documentId, CancellationToken cancellationToken)
            {
                return Task.FromResult(LoadTextAndVersionSynchronously(workspace, documentId, cancellationToken));
            }

            internal override TextAndVersion LoadTextAndVersionSynchronously(Workspace workspace, DocumentId documentId, CancellationToken cancellationToken)
            {
                return TextAndVersion.Create(_sourceTextContainer.CurrentText, VersionStamp.Create(), _filePath);
            }
        }
    }
}
