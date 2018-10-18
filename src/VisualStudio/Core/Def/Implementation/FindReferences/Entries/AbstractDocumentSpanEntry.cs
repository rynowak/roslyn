// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Documents;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell.TableControl;
using Microsoft.VisualStudio.Shell.TableManager;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.FindUsages
{
    internal partial class StreamingFindUsagesPresenter
    {
        /// <summary>
        /// Base type of all <see cref="Entry"/>s that represent some source location in 
        /// a <see cref="CodeAnalysis.Document"/>.  Navigation to that location is provided by this type.
        /// Subclasses can be used to provide customized line text to display in the entry.
        /// </summary>
        private abstract class AbstractDocumentSpanEntry : Entry
        {
            private readonly AbstractTableDataSourceFindUsagesContext _context;

            private readonly string _projectName;
            private readonly object _boxedProjectGuid;

            private readonly DocumentSpan _originalDocumentSpan;
            private readonly SourceText _originalSourceText;

            private readonly MappedSpanResult _mappedSpanResult;

            protected AbstractDocumentSpanEntry(
                AbstractTableDataSourceFindUsagesContext context,
                RoslynDefinitionBucket definitionBucket,
                string projectName,
                Guid projectGuid,
                DocumentSpan originalDocumentSpan,
                SourceText originalSourceText,
                MappedSpanResult mappedSpanResult)
                : base(definitionBucket)
            {
                _context = context;

                _projectName = projectName;
                _boxedProjectGuid = projectGuid;

                _originalDocumentSpan = originalDocumentSpan;
                _originalSourceText = originalSourceText;

                _mappedSpanResult = mappedSpanResult;
            }

            protected StreamingFindUsagesPresenter Presenter => _context.Presenter;

            protected SourceText SourceText => _originalSourceText;
            protected Document Document => _originalDocumentSpan.Document;
            protected TextSpan SourceSpan => _originalDocumentSpan.SourceSpan;

            protected override object GetValueWorker(string keyName)
            {
                switch (keyName)
                {
                    case StandardTableKeyNames.DocumentName:
                        return _mappedSpanResult.FilePath;
                    case StandardTableKeyNames.Line:
                        return _mappedSpanResult.LinePositionSpan.Start.Line;
                    case StandardTableKeyNames.Column:
                        return _mappedSpanResult.LinePositionSpan.Start.Character;
                    case StandardTableKeyNames.ProjectName:
                        return _projectName;
                    case StandardTableKeyNames.ProjectGuid:
                        return _boxedProjectGuid;
                    case StandardTableKeyNames.Text:
                        // return original text for now due to classification. otherwise, we can't classify something like razor
                        // file correctly
                        return _originalSourceText.Lines.GetLineFromPosition(_originalDocumentSpan.SourceSpan.Start).ToString().Trim();
                }

                return null;
            }

            public override bool TryCreateColumnContent(string columnName, out FrameworkElement content)
            {
                if (columnName == StandardTableColumnDefinitions2.LineText)
                {
                    var inlines = CreateLineTextInlines();
                    var textBlock = inlines.ToTextBlock(Presenter.ClassificationFormatMap, Presenter.TypeMap, wrap: false);

                    content = textBlock;
                    return true;
                }

                content = null;
                return false;
            }

            protected abstract IList<Inline> CreateLineTextInlines();

            public static async Task<MappedSpanResult> MapAndGetFirstAsync(DocumentSpan documentSpan, SourceText sourceText, CancellationToken cancellationToken)
            {
                var service = documentSpan.Document.State.Services.GetService<ISpanMappingService>();
                if (service == null)
                {
                    return new MappedSpanResult(documentSpan.Document.FilePath, sourceText.Lines.GetLinePositionSpan(documentSpan.SourceSpan), documentSpan.SourceSpan);
                }

                var result = await service.MapSpansAsync(
                    documentSpan.Document, SpecializedCollections.SingletonEnumerable(documentSpan.SourceSpan), cancellationToken).ConfigureAwait(false);

                if (result.IsDefaultOrEmpty)
                {
                    return new MappedSpanResult(documentSpan.Document.FilePath, sourceText.Lines.GetLinePositionSpan(documentSpan.SourceSpan), documentSpan.SourceSpan);
                }

                return result[0];
            }
        }
    }
}
