using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Alice.Compiler;
using MediatR;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharpDiagnosticSeverity = OmniSharp.Extensions.LanguageServer.Protocol.Models.DiagnosticSeverity;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace alicec.Lsp;

#pragma warning disable CS0618

internal sealed class OlsTextDocumentHandler : TextDocumentSyncHandlerBase
{
    private readonly OlsWorkspace _workspace;
    private readonly ILanguageServerFacade _server;
    private readonly ILogger<OlsTextDocumentHandler> _logger;

    private static readonly TextDocumentSelector Selector = new(
        new TextDocumentFilter { Pattern = "**/*.alice", Language = "alice" }
    );

    public OlsTextDocumentHandler(OlsWorkspace workspace, ILanguageServerFacade server, ILogger<OlsTextDocumentHandler> logger)
    {
        _workspace = workspace;
        _server = server;
        _logger = logger;
    }

    public TextDocumentSyncKind Change { get; } = TextDocumentSyncKind.Full;

    public override Task<Unit> Handle(DidOpenTextDocumentParams notification, CancellationToken token)
    {
        _workspace.Upsert(notification.TextDocument.Uri, notification.TextDocument.Text, notification.TextDocument.Version ?? 0);
        PublishDiagnostics(notification.TextDocument.Uri);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams notification, CancellationToken token)
    {
        var text = string.Empty;
        foreach (var change in notification.ContentChanges)
        {
            text = change.Text;
            break;
        }

        _workspace.Upsert(notification.TextDocument.Uri, text, notification.TextDocument.Version ?? 0);
        PublishDiagnostics(notification.TextDocument.Uri);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams notification, CancellationToken token)
    {
        _workspace.Remove(notification.TextDocument.Uri);
        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = notification.TextDocument.Uri,
            Diagnostics = new Container<LspDiagnostic>()
        });

        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams notification, CancellationToken token)
    {
        PublishDiagnostics(notification.TextDocument.Uri);
        return Unit.Task;
    }

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = Selector,
            Change = Change,
            Save = new SaveOptions { IncludeText = true }
        };

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) => new(uri, "alice");

    private void PublishDiagnostics(DocumentUri uri)
    {
        if (!_workspace.TryGet(uri, out var doc) || doc is null)
        {
            return;
        }

        var diags = new List<Alice.Compiler.Diagnostic>();
        try
        {
            var path = uri.GetFileSystemPath();

            var lexer = new Lexer(doc.Text, path);
            var tokens = lexer.LexAll(out var lexDiagnostics);
            diags.AddRange(lexDiagnostics);

            if (lexDiagnostics.Count == 0)
            {
                var parser = new Parser(tokens, path);
                _ = parser.ParseCompilationUnit(out var parseDiagnostics);
                diags.AddRange(parseDiagnostics);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成诊断失败");
        }

        var lspDiagnostics = new List<LspDiagnostic>();
        foreach (var d in diags)
        {
            lspDiagnostics.Add(ToLspDiagnostic(d));
        }

        _server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<LspDiagnostic>(lspDiagnostics)
        });
    }

    private static LspDiagnostic ToLspDiagnostic(Alice.Compiler.Diagnostic d)
    {
        var span = d.Span;
        var startLine = Math.Max(0, span.Line - 1);
        var startCol = Math.Max(0, span.Column - 1);
        var endLine = startLine;
        var endCol = startCol + Math.Max(1, span.Length);
        endCol = Math.Min(endCol, startCol + 256);

        return new LspDiagnostic
        {
            Severity = d.Severity == Alice.Compiler.DiagnosticSeverity.Error ? OmniSharpDiagnosticSeverity.Error : OmniSharpDiagnosticSeverity.Warning,
            Message = d.Message,
            Range = new Range(new Position(startLine, startCol), new Position(endLine, endCol))
        };
    }
}
