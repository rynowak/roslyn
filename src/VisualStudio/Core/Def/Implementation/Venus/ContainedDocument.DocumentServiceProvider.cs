// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor.Shared.Extensions;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Text.Shared.Extensions;
using Microsoft.VisualStudio.Text.Projection;
using Roslyn.Utilities;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.Venus
{
    internal sealed partial class ContainedDocument
    {
        public class DocumentServiceProvider : IDocumentServiceProvider
        {
            public static readonly IDocumentServiceProvider Instace = new DocumentServiceProvider();

            private readonly SpanMapper _service;

            private DocumentServiceProvider()
            {
                _service = new SpanMapper();
            }

            public TService GetService<TService>() where TService : class, IDocumentService
            {
                if (_service is TService service)
                {
                    return service;
                }

                return default;
            }

            private class SpanMapper : ISpanMappingService
            {
                public async Task<ImmutableArray<MappedSpanResult>> MapSpansAsync(Document document, IEnumerable<TextSpan> spans, CancellationToken cancellationToken)
                {
                    // REVIEW: for now, we keep document here due to open file case, otherwise, we need to create new SpanMappingService for every char user types.

                    // only internal people should use this. so throw when mis-used
                    var containedDocument = TryGetContainedDocument(document.Id);
                    Contract.ThrowIfNull(containedDocument);

                    var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                    var secondaryBufferSnapshot = sourceText.FindCorrespondingEditorTextSnapshot() as IProjectionSnapshot;
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
        }
    }
}
