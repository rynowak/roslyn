// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Text.Shared.Extensions;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Projection;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Venus
{
    internal sealed partial class ContainedDocument
    {
        public class DocumentServiceProvider : IDocumentServiceProvider
        {
            public static readonly IDocumentServiceProvider Instace = new DocumentServiceProvider();

            private readonly SpanMapper _spanMapper;
            private readonly DocumentExcerpter _excerpter;

            private DocumentServiceProvider()
            {
                _spanMapper = new SpanMapper();
                _excerpter = new DocumentExcerpter();
            }

            public TService GetService<TService>() where TService : class, IDocumentService
            {
                if (_spanMapper is TService spanMapper)
                {
                    return spanMapper;
                }

                if (_excerpter is TService excerpter)
                {
                    return excerpter;
                }

                // ask the default document service provider
                return TextDocumentState.DefaultDocumentServiceProvider.Instance.GetService<TService>();
            }

            private static async Task<IProjectionSnapshot> GetProjectSnapshotAsync(Document document, CancellationToken cancellationToken)
            {
                // only internal people should use this. so throw when mis-used
                var containedDocument = TryGetContainedDocument(document.Id);
                Contract.ThrowIfNull(containedDocument);

                var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                return sourceText.FindCorrespondingEditorTextSnapshot() as IProjectionSnapshot;
            }

            private class SpanMapper : ISpanMappingService
            {
                public async Task<ImmutableArray<MappedSpanResult>> MapSpansAsync(Document document, IEnumerable<TextSpan> spans, CancellationToken cancellationToken)
                {
                    // REVIEW: for now, we keep document here due to open file case, otherwise, we need to create new SpanMappingService for every char user types.

                    var secondaryBufferSnapshot = await GetProjectSnapshotAsync(document, cancellationToken).ConfigureAwait(false);
                    if (secondaryBufferSnapshot == null)
                    {
                        return default;
                    }

                    var builder = ArrayBuilder<MappedSpanResult>.GetInstance();
                    foreach (var span in spans)
                    {
                        var result = default(MappedSpanResult?);
                        foreach (var primarySpan in secondaryBufferSnapshot.MapToSourceSnapshots(span.ToSpan()))
                        {
                            // this is from http://index/?query=MapSecondaryToPrimarySpan&rightProject=Microsoft.VisualStudio.Editor.Implementation&file=VsTextBufferCoordinatorAdapter.cs&line=177
                            // make sure we only consider one that's not split
                            if (primarySpan.Length != span.Length)
                            {
                                continue;
                            }

                            // take the first one.
                            // contained document file path points cshtml this secondary buffer belong to
                            result = new MappedSpanResult(document.FilePath, primarySpan.ToLinePositionSpan(), primarySpan.Span.ToTextSpan());
                            break;
                        }

                        // this is only used internally. we don't expect it to ever fail to map to primary buffer. 
                        // otherwise. caller is using it wrong
                        Contract.ThrowIfFalse(result.HasValue);

                        builder.Add(result.Value);
                    }

                    return builder.ToImmutableAndFree();
                }
            }

            private class DocumentExcerpter : IDocumentExcerptService
            {
                public async Task<ExcerptResult?> TryExcerptAsync(Document document, TextSpan span, ExcerptMode mode, CancellationToken cancellationToken)
                {
                    // REVIEW: for now, we keep document here due to open file case, otherwise, we need to create new SpanMappingService for every char user types.

                    var secondaryBufferSnapshot = await GetProjectSnapshotAsync(document, cancellationToken).ConfigureAwait(false);
                    if (secondaryBufferSnapshot == null)
                    {
                        return null;
                    }

                    var primarySpans = secondaryBufferSnapshot.MapToSourceSnapshots(span.ToSpan());
                    if (primarySpans.Count != 1)
                    {
                        // default version doesn't support where span mapped multiple primary buffer spans
                        return null;
                    }

                    var contentSpan = GetContentSpanFromPrimarySpan(mode, primarySpans[0]);
                    if (contentSpan == null)
                    {
                        // can't figure out span to extract content from
                        return null;
                    }

                    var (content, mappedSpan) = GetContentAndMappedSpan(mode, primarySpans[0], contentSpan.Value);
                    if (content == null)
                    {
                        return null;
                    }

                    // TODO: map content span on the primary buffer to second buffer and for ones that can be mapped,
                    //       get classification for those portion on secondary buffer and convert span on those to
                    //       span on the content and create ClassifiedSpan

                    // the default implementation has no idea how to classify the primary snapshot
                    return new ExcerptResult(content, mappedSpan, ImmutableArray<ClassifiedSpan>.Empty, document, span);
                }

                private static (SourceText, TextSpan) GetContentAndMappedSpan(ExcerptMode mode, SnapshotSpan primarySpan, TextSpan contentSpan)
                {
                    var line = primarySpan.Start.GetContainingLine();

                    if (mode == ExcerptMode.SingleLine)
                    {
                        return (line.Snapshot.AsText().GetSubText(contentSpan), GetSpanOnContent(primarySpan.Span.ToTextSpan(), contentSpan));
                    }

                    if (mode == ExcerptMode.Tooltip)
                    {
                        return (line.Snapshot.AsText().GetSubText(contentSpan), GetSpanOnContent(primarySpan.Span.ToTextSpan(), contentSpan));
                    }

                    return (default, default);
                }

                private static TextSpan? GetContentSpanFromPrimarySpan(ExcerptMode mode, SnapshotSpan primarySpan)
                {
                    var line = primarySpan.Start.GetContainingLine();

                    if (mode == ExcerptMode.SingleLine)
                    {
                        // the line where primary span is on
                        return line.Extent.Span.ToTextSpan();
                    }

                    if (mode == ExcerptMode.Tooltip)
                    {
                        // +-1 line of the line where primary span is on
                        var startLine = line.Snapshot.GetLineFromLineNumber(Math.Min(0, line.LineNumber - 1));
                        var endLine = line.Snapshot.GetLineFromLineNumber(Math.Max(line.Snapshot.LineCount - 1, line.LineNumber + 1));

                        return TextSpan.FromBounds(startLine.Extent.Start.Position, endLine.Extent.End.Position);
                    }

                    return default;
                }

                private static TextSpan GetSpanOnContent(TextSpan targetSpan, TextSpan excerptSpan)
                {
                    return TextSpan.FromBounds(targetSpan.Start - excerptSpan.Start, targetSpan.Length);
                }
            }
        }
    }
}
