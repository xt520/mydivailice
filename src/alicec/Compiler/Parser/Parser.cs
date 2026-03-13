using System;
using System.Collections.Generic;
using System.Linq;

namespace Alice.Compiler;

internal sealed class Parser
{
    private readonly IReadOnlyList<Token> _tokens;
    private readonly string _path;
    private readonly bool _isInterfaceFile;
    private int _index;
    private bool _topLevelPanic;

    internal Parser(IReadOnlyList<Token> tokens, string sourcePath)
    {
        _tokens = tokens;
        _path = string.IsNullOrWhiteSpace(sourcePath) ? "<source>" : sourcePath;
        _isInterfaceFile = _path.EndsWith(".alicei", StringComparison.OrdinalIgnoreCase);
        _index = 0;
        _topLevelPanic = false;
    }

    internal CompilationUnit ParseCompilationUnit(out List<Diagnostic> diagnostics)
    {
        diagnostics = new List<Diagnostic>();
        NamespaceDecl? ns = null;
        var imports = new List<ImportDecl>();
        var typeAliases = new List<TypeAliasDecl>();
        var globals = new List<GlobalVarDecl>();
        var funs = new List<FunDecl>();
        var classes = new List<ClassDecl>();
        var structs = new List<StructDecl>();
        var enums = new List<EnumDecl>();
        var interfaces = new List<InterfaceDecl>();

        SkipSeparators();

        if (Match(TokenKind.KwNamespace))
        {
            var kw = Previous();
            var qn = ParseQualifiedName(diagnostics);
            if (qn is not null)
            {
                ns = new NamespaceDecl(qn, Combine(kw.Span, Previous().Span));
            }
            SkipSeparators();
        }

        while (!Check(TokenKind.Eof))
        {
            if (_topLevelPanic)
            {
                SynchronizeTopLevel();
                SkipSeparators();
                _topLevelPanic = false;
                continue;
            }

            var topAttrs = new List<string>();
            while (Match(TokenKind.At))
            {
                if (!Consume(TokenKind.Identifier, diagnostics, "期望属性名"))
                {
                    _topLevelPanic = true;
                    break;
                }
                topAttrs.Add(Previous().Text);
                SkipSeparators();
            }

            if (Match(TokenKind.KwImport))
            {
                var import = ParseImportDecl(diagnostics);
                if (import is not null)
                {
                    imports.Add(import);
                }
                else
                {
                    _topLevelPanic = true;
                }
                SkipSeparators();
                continue;
            }

            if (Match(TokenKind.KwType))
            {
                var alias = ParseTypeAliasDecl(diagnostics);
                if (alias is not null)
                {
                    typeAliases.Add(alias);
                }
                else
                {
                    _topLevelPanic = true;
                }
                SkipSeparators();
                continue;
            }

            var isPublic = false;
            if (Match(TokenKind.KwPublic))
            {
                isPublic = true;
                SkipSeparators();
            }

            var isExtern = false;
            if (Match(TokenKind.KwExtern))
            {
                if (!_isInterfaceFile)
                {
                    diagnostics.Add(Error(Previous().Span, "extern 仅允许出现在 .alicei 文件"));
                    _topLevelPanic = true;
                    SkipSeparators();
                    continue;
                }
                isExtern = true;
                SkipSeparators();
            }

            if (Match(TokenKind.KwClass))
            {
                var cls = ParseClassDecl(diagnostics, isPublic, isExtern);
                if (cls is not null)
                {
                    classes.Add(cls);
                }
                else
                {
                    _topLevelPanic = true;
                }
                SkipSeparators();
                continue;
            }

            if (Match(TokenKind.KwStruct))
            {
                var st = ParseStructDecl(diagnostics, isPublic, isExtern);
                if (st is not null)
                {
                    structs.Add(st);
                }
                else
                {
                    _topLevelPanic = true;
                }
                SkipSeparators();
                continue;
            }

            if (Match(TokenKind.KwEnum))
            {
                var en = ParseEnumDecl(diagnostics, isPublic);
                if (en is not null)
                {
                    enums.Add(en);
                }
                else
                {
                    _topLevelPanic = true;
                }
                SkipSeparators();
                continue;
            }

            if (Match(TokenKind.KwInterface))
            {
                var itf = ParseInterfaceDecl(diagnostics, isPublic, isExtern);
                if (itf is not null)
                {
                    interfaces.Add(itf);
                }
                else
                {
                    _topLevelPanic = true;
                }
                SkipSeparators();
                continue;
            }

            if (isPublic)
            {
                diagnostics.Add(Error(Previous().Span, "public 只能用于 class/struct/enum/interface"));
                SynchronizeTopLevel();
                SkipSeparators();
                continue;
            }

            if (isExtern)
            {
                diagnostics.Add(Error(Previous().Span, "extern 仅允许用于 class/interface/fun/const"));
                SynchronizeTopLevel();
                SkipSeparators();
                continue;
            }

            if (Check(TokenKind.KwConst) || (Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.Colon))
            {
                var g = ParseGlobalVarDecl(diagnostics);
                if (g is not null)
                {
                    globals.Add(g);
                }
                else
                {
                    _topLevelPanic = true;
                }
                SkipSeparators();
                continue;
            }

            if (Match(TokenKind.KwFun))
            {
                var fun = ParseFunDecl(diagnostics, allowSignatureOnly: _isInterfaceFile, isAsync: false, attrs: topAttrs);
                if (fun is not null)
                {
                    funs.Add(fun);
                }
                else
                {
                    _topLevelPanic = true;
                }
                SkipSeparators();
                continue;
            }

            if (Match(TokenKind.KwAsync))
            {
                if (!Consume(TokenKind.KwFun, diagnostics, "期望 'fun'"))
                {
                    _topLevelPanic = true;
                    SkipSeparators();
                    continue;
                }

                var fun = ParseFunDecl(diagnostics, allowSignatureOnly: _isInterfaceFile, isAsync: true, attrs: topAttrs);
                if (fun is not null)
                {
                    funs.Add(fun);
                }
                else
                {
                    _topLevelPanic = true;
                }

                SkipSeparators();
                continue;
            }

            if (Check(TokenKind.Identifier) && Peek(1).Kind == TokenKind.ColonEquals)
            {
                diagnostics.Add(Error(Current().Span, "顶层禁止类型推导声明，请使用 name: Type = expr（可选 const）"));
                SynchronizeTopLevel();
                SkipSeparators();
                continue;
            }
            if (Check(TokenKind.KwVar))
            {
                diagnostics.Add(Error(Current().Span, "顶层禁止 var 推导声明，请使用 name: Type = expr（可选 const）"));
                SynchronizeTopLevel();
                SkipSeparators();
                continue;
            }

            diagnostics.Add(Error(Current().Span, "顶层只允许 namespace/import/const/显式类型全局声明/fun/class/struct/enum/interface"));
            SynchronizeTopLevel();
            SkipSeparators();
        }

        var isInterfaceFile = _isInterfaceFile;
        var baseName = Path.GetFileNameWithoutExtension(_path);
        var nsSegs = ns?.Name.Segments ?? Array.Empty<string>();
        IReadOnlyList<string> modSegs;
        if (string.Equals(baseName, "index", StringComparison.OrdinalIgnoreCase))
        {
            modSegs = nsSegs.ToArray();
        }
        else if (nsSegs.Count > 0 && string.Equals(nsSegs.Last(), baseName, StringComparison.OrdinalIgnoreCase))
        {
            modSegs = nsSegs.ToArray();
        }
        else
        {
            modSegs = nsSegs.Concat(new[] { baseName }).ToArray();
        }
        var moduleName = string.Join(".", modSegs);
        return new CompilationUnit(moduleName, baseName, _path, isInterfaceFile, IsStdLib: false, ns, imports, typeAliases, globals, funs, classes, structs, enums, interfaces);
    }

    private StructDecl? ParseStructDecl(List<Diagnostic> diagnostics, bool isPublic, bool isExtern)
    {
        var structKw = Previous();
        if (!Consume(TokenKind.Identifier, diagnostics, "期望结构体名"))
        {
            return null;
        }
        var nameTok = Previous();
        var typeParams = ParseTypeParamDecls(diagnostics);
        var baseTypes = new List<NamedTypeRef>();
        if (Match(TokenKind.Colon))
        {
            while (true)
            {
                var tr = ParseNamedTypeRef(diagnostics);
                if (tr is null) return null;
                baseTypes.Add(tr);
                if (Match(TokenKind.Comma)) continue;
                break;
            }
        }
        SkipSeparators();
        if (!Consume(TokenKind.LBrace, diagnostics, "期望 '{'"))
        {
            return null;
        }
        var members = new List<ClassMember>();
        SkipSeparators();
        while (!Check(TokenKind.RBrace) && !Check(TokenKind.Eof))
        {
            var mem = ParseClassMember(diagnostics, nameTok.Text, defaultAccess: Accessibility.Default, allowOverride: false);
            if (mem is null) return null;
            members.Add(mem);
            SkipSeparators();
        }
        if (!Consume(TokenKind.RBrace, diagnostics, "期望 '}'"))
        {
            return null;
        }
        var rb = Previous();
        return new StructDecl(isPublic, isExtern, nameTok.Text, typeParams, baseTypes, members, Combine(structKw.Span, rb.Span));
    }

    private EnumDecl? ParseEnumDecl(List<Diagnostic> diagnostics, bool isPublic)
    {
        var kw = Previous();
        if (!Consume(TokenKind.Identifier, diagnostics, "期望枚举名"))
        {
            return null;
        }
        var nameTok = Previous();
        SkipSeparators();
        if (!Consume(TokenKind.LBrace, diagnostics, "期望 '{'"))
        {
            return null;
        }
        var members = new List<EnumMemberDecl>();
        long next = 0;
        SkipSeparators();
        while (!Check(TokenKind.RBrace) && !Check(TokenKind.Eof))
        {
            if (!Consume(TokenKind.Identifier, diagnostics, "期望枚举成员名"))
            {
                return null;
            }
            var mname = Previous();
            long? value = null;
            if (Match(TokenKind.Equals))
            {
                var sign = 1L;
                if (Match(TokenKind.Minus))
                {
                    sign = -1;
                }

                if (!Consume(TokenKind.IntLiteral, diagnostics, "期望整数常量"))
                {
                    return null;
                }
                if (!long.TryParse(Previous().Text, out var v0))
                {
                    diagnostics.Add(Error(Previous().Span, "枚举值超出范围"));
                    return null;
                }

                try
                {
                    value = checked(sign * v0);
                }
                catch (OverflowException)
                {
                    diagnostics.Add(Error(Previous().Span, "枚举值超出范围"));
                    return null;
                }
            }

            if (value is null)
            {
                value = next;
            }
            next = value.Value + 1;
            members.Add(new EnumMemberDecl(mname.Text, value, Combine(mname.Span, Previous().Span)));
            if (Match(TokenKind.Comma))
            {
                SkipSeparators();
                continue;
            }
            if (Match(TokenKind.NewLine) || Match(TokenKind.Semicolon))
            {
                SkipSeparators();
                continue;
            }
        }
        if (!Consume(TokenKind.RBrace, diagnostics, "期望 '}'"))
        {
            return null;
        }
        var rb = Previous();
        return new EnumDecl(isPublic, nameTok.Text, members, Combine(kw.Span, rb.Span));
    }

    private ClassDecl? ParseClassDecl(List<Diagnostic> diagnostics, bool isPublic, bool isExtern)
    {
        var classKw = Previous();
        if (!Consume(TokenKind.Identifier, diagnostics, "期望类名"))
        {
            return null;
        }

        var nameTok = Previous();

        var typeParams = ParseTypeParamDecls(diagnostics);
        var baseTypes = new List<NamedTypeRef>();
        if (Match(TokenKind.Colon))
        {
            while (true)
            {
                var tr = ParseNamedTypeRef(diagnostics);
                if (tr is null)
                {
                    return null;
                }
                baseTypes.Add(tr);
                if (Match(TokenKind.Comma))
                {
                    continue;
                }
                break;
            }
        }

        SkipSeparators();
        if (!Consume(TokenKind.LBrace, diagnostics, "期望 '{'"))
        {
            return null;
        }

        var lbrace = Previous();
        var members = new List<ClassMember>();
        SkipSeparators();
        while (!Check(TokenKind.RBrace) && !Check(TokenKind.Eof))
        {
            if (Match(TokenKind.KwPublic) || Match(TokenKind.KwProtected) || Match(TokenKind.KwPrivate))
            {
                var access = AccessibilityFromToken(Previous().Kind);
                SkipSeparators();
                if (Check(TokenKind.LBrace))
                {
                    var block = ParseClassVisibilityBlock(diagnostics, nameTok.Text, access);
                    if (block is null)
                    {
                        return null;
                    }
                    members.AddRange(block);
                }
                else
                {
                    var mem = ParseClassMember(diagnostics, nameTok.Text, defaultAccess: access, allowOverride: true);
                    if (mem is null)
                    {
                        return null;
                    }
                    members.Add(mem);
                }

                SkipSeparators();
                continue;
            }

            var m0 = ParseClassMember(diagnostics, nameTok.Text, defaultAccess: Accessibility.Default, allowOverride: true);
            if (m0 is null)
            {
                return null;
            }
            members.Add(m0);
            SkipSeparators();
        }

        if (!Consume(TokenKind.RBrace, diagnostics, "期望 '}'"))
        {
            return null;
        }

        var rbrace = Previous();
        return new ClassDecl(isPublic, IsExtern: isExtern, nameTok.Text, typeParams, baseTypes, members, Combine(classKw.Span, rbrace.Span));
    }

    private IReadOnlyList<ClassMember>? ParseClassVisibilityBlock(List<Diagnostic> diagnostics, string className, Accessibility access)
    {
        if (!Consume(TokenKind.LBrace, diagnostics, "期望 '{'"))
        {
            return null;
        }

        var members = new List<ClassMember>();
        SkipSeparators();
        while (!Check(TokenKind.RBrace) && !Check(TokenKind.Eof))
        {
            var mem = ParseClassMember(diagnostics, className, defaultAccess: access, allowOverride: true);
            if (mem is null)
            {
                return null;
            }
            members.Add(mem);
            SkipSeparators();
        }

        if (!Consume(TokenKind.RBrace, diagnostics, "期望 '}'"))
        {
            return null;
        }

        return members;
    }

    private ClassMember? ParseClassMember(List<Diagnostic> diagnostics, string className, Accessibility defaultAccess, bool allowOverride)
    {
        var attrs = new List<string>();
        while (Match(TokenKind.At))
        {
            if (!Consume(TokenKind.Identifier, diagnostics, "期望属性名"))
            {
                return null;
            }
            attrs.Add(Previous().Text);
            SkipSeparators();
        }

        var access = defaultAccess;
        var isStatic = false;
        var isConst = false;
        var isExtern = false;

        if (allowOverride)
        {
            if (Match(TokenKind.KwPublic) || Match(TokenKind.KwProtected) || Match(TokenKind.KwPrivate))
            {
                access = AccessibilityFromToken(Previous().Kind);
                SkipSeparators();
            }
        }

        if (Match(TokenKind.KwStatic))
        {
            isStatic = true;
            SkipSeparators();
        }

        if (Match(TokenKind.KwConst))
        {
            isConst = true;
            SkipSeparators();
        }

        if (Match(TokenKind.KwExtern))
        {
            if (!_isInterfaceFile)
            {
                diagnostics.Add(Error(Previous().Span, "extern 仅允许出现在 .alicei 文件"));
                return null;
            }
            isExtern = true;
            SkipSeparators();
        }
        
        if (Match(TokenKind.KwType))
        {
            var typeKw = Previous();
            if (!Consume(TokenKind.Identifier, diagnostics, "期望关联类型名"))
            {
                return null;
            }
            var nameTok2 = Previous();
            if (!Consume(TokenKind.Equals, diagnostics, "期望 '='"))
            {
                return null;
            }
            var valueType = ParseType(diagnostics);
            if (valueType is null)
            {
                return null;
            }
            if (Match(TokenKind.Semicolon) || Match(TokenKind.NewLine))
            {
                SkipSeparators();
            }
            return new AssociatedTypeBindDecl(nameTok2.Text, valueType, Combine(typeKw.Span, valueType.Span));
        }

        if (Match(TokenKind.KwFun))
        {
            var funTok = Previous();
            if (!Consume(TokenKind.Identifier, diagnostics, "期望方法名"))
            {
                return null;
            }
        var nameTok = Previous();

        var typeParams = ParseTypeParamDecls(diagnostics);

        if (!Consume(TokenKind.LParen, diagnostics, "期望 '('") )
        {
            return null;
        }

            var parameters = new List<ParamDecl>();
            if (!Check(TokenKind.RParen))
            {
                while (true)
                {
                    if (!Consume(TokenKind.Identifier, diagnostics, "期望参数名"))
                    {
                        break;
                    }
                    var paramName = Previous();
                    if (!Consume(TokenKind.Colon, diagnostics, "期望 ':'"))
                    {
                        break;
                    }
                    var type = ParseType(diagnostics);
                    if (type is null)
                    {
                        break;
                    }
                    parameters.Add(new ParamDecl(paramName.Text, type, paramName.Span));
                    if (Match(TokenKind.Comma))
                    {
                        continue;
                    }
                    break;
                }
            }
            if (!Consume(TokenKind.RParen, diagnostics, "期望 ')'") )
            {
                return null;
            }

            TypeNode? returnType = null;
            if (Match(TokenKind.Colon))
            {
                returnType = ParseType(diagnostics);
            }

            if (_isInterfaceFile || isExtern)
            {
                if (Match(TokenKind.Semicolon) || Match(TokenKind.NewLine))
                {
                    SkipSeparators();
                }
                var sigSpan = returnType is null ? Combine(funTok.Span, Previous().Span) : Combine(funTok.Span, returnType.Span);
                var body2 = new BlockStmt(Array.Empty<Stmt>(), sigSpan);
                if (nameTok.Text == className)
                {
                    if (returnType is not null)
                    {
                        diagnostics.Add(Error(returnType.Span, "构造函数不允许返回类型"));
                    }
                    return new CtorDecl(access, IsExtern: isExtern, attrs, nameTok.Text, parameters, body2, sigSpan);
                }
                return new MethodDecl(access, isStatic, IsExtern: isExtern, attrs, nameTok.Text, typeParams, parameters, returnType, body2, sigSpan);
            }

            SkipSeparators();
            var body = ParseBlock(diagnostics);
            if (body is null)
            {
                return null;
            }

            var span = Combine(funTok.Span, body.Span);
            if (nameTok.Text == className)
            {
                if (returnType is not null)
                {
                    diagnostics.Add(Error(returnType.Span, "构造函数不允许返回类型"));
                }
                return new CtorDecl(access, IsExtern: isExtern, attrs, nameTok.Text, parameters, body, span);
            }

            return new MethodDecl(access, isStatic, IsExtern: isExtern, attrs, nameTok.Text, typeParams, parameters, returnType, body, span);
        }

        if (!Consume(TokenKind.Identifier, diagnostics, "期望字段名或 fun"))
        {
            return null;
        }

        var fieldName = Previous();
        if (!Consume(TokenKind.Colon, diagnostics, "期望 ':'"))
        {
            return null;
        }
        var fieldType = ParseType(diagnostics);
        if (fieldType is null)
        {
            return null;
        }

        Expr? init = null;
        if (Match(TokenKind.Equals))
        {
            init = ParseExpression(diagnostics);
        }

        var span2 = init is null ? Combine(fieldName.Span, fieldType.Span) : Combine(fieldName.Span, init.Span);
        return new FieldDecl(access, isStatic, isConst, fieldName.Text, fieldType, init, span2);
    }

    private InterfaceDecl? ParseInterfaceDecl(List<Diagnostic> diagnostics, bool isPublic, bool isExtern)
    {
        var kw = Previous();
        if (!Consume(TokenKind.Identifier, diagnostics, "期望接口名"))
        {
            return null;
        }
        var nameTok = Previous();

        var typeParams = ParseTypeParamDecls(diagnostics);

        var baseTypes = new List<NamedTypeRef>();
        if (Match(TokenKind.Colon))
        {
            while (true)
            {
                var tr = ParseNamedTypeRef(diagnostics);
                if (tr is null)
                {
                    return null;
                }
                baseTypes.Add(tr);
                if (Match(TokenKind.Comma))
                {
                    continue;
                }
                break;
            }
        }

        SkipSeparators();
        if (!Consume(TokenKind.LBrace, diagnostics, "期望 '{'"))
        {
            return null;
        }

        var entries = new List<InterfaceEntry>();
        SkipSeparators();
        while (!Check(TokenKind.RBrace) && !Check(TokenKind.Eof))
        {
            if (Match(TokenKind.KwType))
            {
                var typeKw = Previous();
                if (!Consume(TokenKind.Identifier, diagnostics, "期望关联类型名"))
                {
                    return null;
                }
                var nameTok2 = Previous();
                entries.Add(new AssociatedTypeDecl(nameTok2.Text, Combine(typeKw.Span, nameTok2.Span)));
                if (Match(TokenKind.Semicolon) || Match(TokenKind.NewLine))
                {
                    SkipSeparators();
                }
                continue;
            }

            if (Match(TokenKind.KwFun))
            {
                var funKw = Previous();
                if (!Consume(TokenKind.Identifier, diagnostics, "期望方法名"))
                {
                    return null;
                }
                var mname = Previous();
        if (!Consume(TokenKind.LParen, diagnostics, "期望 '('") )
        {
            return null;
        }
                var parameters = new List<ParamDecl>();
                if (!Check(TokenKind.RParen))
                {
                    while (true)
                    {
                        if (!Consume(TokenKind.Identifier, diagnostics, "期望参数名"))
                        {
                            break;
                        }
                        var paramName = Previous();
                        if (!Consume(TokenKind.Colon, diagnostics, "期望 ':'"))
                        {
                            break;
                        }
                        var type = ParseType(diagnostics);
                        if (type is null)
                        {
                            break;
                        }
                        parameters.Add(new ParamDecl(paramName.Text, type, paramName.Span));
                        if (Match(TokenKind.Comma))
                        {
                            continue;
                        }
                        break;
                    }
                }
                if (!Consume(TokenKind.RParen, diagnostics, "期望 ')'") )
                {
                    return null;
                }

                TypeNode? ret = null;
                if (Match(TokenKind.Colon))
                {
                    ret = ParseType(diagnostics);
                }

                var span = ret is null ? Combine(funKw.Span, Previous().Span) : Combine(funKw.Span, ret.Span);
                entries.Add(new InterfaceMethodSig(mname.Text, parameters, ret, span));

                if (Match(TokenKind.Semicolon) || Match(TokenKind.NewLine))
                {
                    SkipSeparators();
                }
                continue;
            }

            if (Check(TokenKind.Identifier))
            {
                var tr = ParseNamedTypeRef(diagnostics);
                if (tr is null)
                {
                    return null;
                }
                if (_isInterfaceFile)
                {
                    diagnostics.Add(Error(tr.Span, "仅 .alice 文件允许接口嵌入"));
                    return null;
                }
                entries.Add(new InterfaceEmbedEntry(tr, tr.Span));
                if (Match(TokenKind.Semicolon) || Match(TokenKind.NewLine))
                {
                    SkipSeparators();
                }
                continue;
            }

            diagnostics.Add(Error(Current().Span, "接口体内只允许 type 声明、fun 签名或嵌入接口名"));
            Advance();
            SkipSeparators();
        }

        if (!Consume(TokenKind.RBrace, diagnostics, "期望 '}'"))
        {
            return null;
        }

        var rb = Previous();
        return new InterfaceDecl(isPublic, IsExtern: isExtern, nameTok.Text, typeParams, baseTypes, entries, Combine(kw.Span, rb.Span));
    }

    private NamedTypeRef? ParseNamedTypeRef(List<Diagnostic> diagnostics)
    {
        if (!Consume(TokenKind.Identifier, diagnostics, "期望类型名"))
        {
            return null;
        }
        var id = Previous();

        if (Check(TokenKind.Less))
        {
            var targs = ParseTypeArgs(diagnostics);
            if (targs is null)
            {
                return null;
            }
            return new NamedTypeRef(id.Text, targs, Combine(id.Span, targs.Last().Span));
        }

        return new NamedTypeRef(id.Text, id.Span);
    }

    private IReadOnlyList<TypeNode>? ParseTypeArgs(List<Diagnostic> diagnostics)
    {
        if (!Consume(TokenKind.Less, diagnostics, "期望 '<'"))
        {
            return null;
        }
        var args = new List<TypeNode>();
        SkipSeparators();
        if (!Check(TokenKind.Greater))
        {
            while (true)
            {
                var ta = ParseType(diagnostics);
                if (ta is null) return null;
                args.Add(ta);
                if (Match(TokenKind.Comma))
                {
                    SkipSeparators();
                    continue;
                }
                break;
            }
        }
        if (!Consume(TokenKind.Greater, diagnostics, "期望 '>'"))
        {
            return null;
        }
        return args;
    }

    private static Accessibility AccessibilityFromToken(TokenKind kind)
    {
        return kind switch
        {
            TokenKind.KwPublic => Accessibility.Public,
            TokenKind.KwProtected => Accessibility.Protected,
            TokenKind.KwPrivate => Accessibility.Private,
            _ => Accessibility.Default,
        };
    }

    private ImportDecl? ParseImportDecl(List<Diagnostic> diagnostics)
    {
        var kw = Previous();
        var qn = ParseQualifiedName(diagnostics);
        if (qn is null)
        {
            return null;
        }

        var alias = qn.Segments.Count > 0 ? qn.Segments[^1] : "_";
        if (Match(TokenKind.KwAs))
        {
            if (!Consume(TokenKind.Identifier, diagnostics, "期望别名标识符"))
            {
                return null;
            }
            alias = Previous().Text;
        }
        return new ImportDecl(qn, alias, Combine(kw.Span, Previous().Span));
    }

    private GlobalVarDecl? ParseGlobalVarDecl(List<Diagnostic> diagnostics)
    {
        var isConst = Match(TokenKind.KwConst);
        var start = Previous();
        if (!Consume(TokenKind.Identifier, diagnostics, "期望全局变量名"))
        {
            return null;
        }
        var nameTok = Previous();
        if (!Consume(TokenKind.Colon, diagnostics, "期望 ':'"))
        {
            return null;
        }
        var type = ParseType(diagnostics);
        if (type is null)
        {
            return null;
        }
        if (!Consume(TokenKind.Equals, diagnostics, "期望 '='"))
        {
            return null;
        }
        var init = ParseExpression(diagnostics);
        return new GlobalVarDecl(isConst, nameTok.Text, type, init, Combine(isConst ? start.Span : nameTok.Span, init.Span));
    }

    private QualifiedName? ParseQualifiedName(List<Diagnostic> diagnostics)
    {
        if (!ConsumeQualifiedNameSegment(diagnostics, "期望限定名"))
        {
            return null;
        }
        var segs = new List<string> { Previous().Text };
        while (Match(TokenKind.Dot))
        {
            if (!ConsumeQualifiedNameSegment(diagnostics, "期望限定名段"))
            {
                return null;
            }
            segs.Add(Previous().Text);
        }
        return new QualifiedName(segs);
    }

    private bool ConsumeQualifiedNameSegment(List<Diagnostic> diagnostics, string message)
    {
        var k = Current().Kind;
        if (k is TokenKind.Identifier or TokenKind.KwAsync or TokenKind.KwAwait or TokenKind.KwGo)
        {
            Advance();
            return true;
        }
        diagnostics.Add(Error(Current().Span, message));
        return false;
    }

    private FunDecl? ParseFunDecl(List<Diagnostic> diagnostics, bool allowSignatureOnly, bool isAsync, IReadOnlyList<string> attrs)
    {
        var funToken = Previous();
        if (!Consume(TokenKind.Identifier, diagnostics, "期望函数名"))
        {
            return null;
        }
        var nameTok = Previous();

        var typeParams = ParseTypeParamDecls(diagnostics);

        if (!Consume(TokenKind.LParen, diagnostics, "期望 '('"))
        {
            return null;
        }

        var parameters = new List<ParamDecl>();
        if (!Check(TokenKind.RParen))
        {
            while (true)
            {
                if (!Consume(TokenKind.Identifier, diagnostics, "期望参数名"))
                {
                    break;
                }

                var paramName = Previous();
                if (!Consume(TokenKind.Colon, diagnostics, "期望 ':'"))
                {
                    break;
                }

                var type = ParseType(diagnostics);
                if (type is null)
                {
                    break;
                }

                parameters.Add(new ParamDecl(paramName.Text, type, paramName.Span));

                if (Match(TokenKind.Comma))
                {
                    continue;
                }

                break;
            }
        }

        if (!Consume(TokenKind.RParen, diagnostics, "期望 ')'"))
        {
            return null;
        }

        TypeNode? returnType = null;
        if (Match(TokenKind.Colon))
        {
            returnType = ParseType(diagnostics);
        }

        if (allowSignatureOnly)
        {
            if (Match(TokenKind.Semicolon) || Match(TokenKind.NewLine))
            {
                SkipSeparators();
            }
            var span2 = returnType is null ? Combine(funToken.Span, Previous().Span) : Combine(funToken.Span, returnType.Span);
            var body2 = new BlockStmt(Array.Empty<Stmt>(), span2);
            return new FunDecl(isAsync, attrs.ToList(), nameTok.Text, typeParams, parameters, returnType, body2, span2);
        }

        SkipSeparators();
        var body = ParseBlock(diagnostics);
        if (body is null)
        {
            return null;
        }

        var span = Combine(funToken.Span, body.Span);
        return new FunDecl(isAsync, attrs.ToList(), nameTok.Text, typeParams, parameters, returnType, body, span);
    }

    private BlockStmt? ParseBlock(List<Diagnostic> diagnostics)
    {
        if (!Consume(TokenKind.LBrace, diagnostics, "期望 '{'"))
        {
            return null;
        }

        var lbrace = Previous();
        var stmts = new List<Stmt>();
        SkipSeparators();
        while (!Check(TokenKind.RBrace) && !Check(TokenKind.Eof))
        {
            var st = ParseStatement(diagnostics);
            if (st is not null)
            {
                stmts.Add(st);
            }

            SkipSeparators();
        }

        if (!Consume(TokenKind.RBrace, diagnostics, "期望 '}'"))
        {
            return null;
        }

        var rbrace = Previous();
        return new BlockStmt(stmts, Combine(lbrace.Span, rbrace.Span));
    }

    private Stmt? ParseStatement(List<Diagnostic> diagnostics)
    {
        if (Match(TokenKind.KwDefer))
        {
            var kw = Previous();
            var expr = ParseExpression(diagnostics);
            return new DeferStmt(expr, Combine(kw.Span, expr.Span));
        }

        if (Match(TokenKind.KwTry))
        {
            return ParseTry(diagnostics);
        }

        if (Match(TokenKind.KwRaise))
        {
            var kw = Previous();
            if (IsStatementTerminator(Current().Kind))
            {
                return new RaiseStmt(null, kw.Span);
            }
            var expr = ParseExpression(diagnostics);
            return new RaiseStmt(expr, Combine(kw.Span, expr.Span));
        }

        if (Match(TokenKind.KwIf))
        {
            return ParseIf(diagnostics);
        }

        if (Match(TokenKind.KwWhile))
        {
            return ParseWhile(diagnostics);
        }

        if (Match(TokenKind.KwReturn))
        {
            var kw = Previous();
            if (IsStatementTerminator(Current().Kind))
            {
                return new ReturnStmt(null, kw.Span);
            }

            var expr = ParseExpression(diagnostics);
            return new ReturnStmt(expr, Combine(kw.Span, expr.Span));
        }

        if (Match(TokenKind.KwBreak))
        {
            return new BreakStmt(Previous().Span);
        }

        if (Match(TokenKind.KwContinue))
        {
            return new ContinueStmt(Previous().Span);
        }

        if (Match(TokenKind.KwVar))
        {
            return ParseVarDeclVarKeyword(diagnostics);
        }

        if (Check(TokenKind.Identifier))
        {
            var id = Current();
            var next = Peek(1);
            if (next.Kind == TokenKind.ColonEquals)
            {
                Advance();
                Advance();
                var init = ParseExpression(diagnostics);
                return new VarDeclStmt(id.Text, null, init, VarDeclKind.ColonEquals, Combine(id.Span, init.Span));
            }
            if (next.Kind == TokenKind.Colon)
            {
                Advance();
                Consume(TokenKind.Colon, diagnostics, "期望 ':'");
                var type = ParseType(diagnostics);
                if (type is null)
                {
                    return null;
                }
                if (!Consume(TokenKind.Equals, diagnostics, "期望 '='"))
                {
                    return null;
                }
                var init = ParseExpression(diagnostics);
                return new VarDeclStmt(id.Text, type, init, VarDeclKind.ExplicitType, Combine(id.Span, init.Span));
            }
        }

        var e = ParseExpression(diagnostics);
        return new ExprStmt(e, e.Span);
    }

    private TryStmt? ParseTry(List<Diagnostic> diagnostics)
    {
        var tryKw = Previous();
        SkipSeparators();
        var tryBlock = ParseBlock(diagnostics);
        if (tryBlock is null)
        {
            return null;
        }

        SkipSeparators();
        ExceptClause? except = null;
        if (Match(TokenKind.KwExcept))
        {
            var exKw = Previous();
            string? name = null;
            TypeNode? type = null;
            SkipSeparators();
            if (Match(TokenKind.LParen))
            {
                if (!Consume(TokenKind.Identifier, diagnostics, "期望异常变量名"))
                {
                    return null;
                }
                name = Previous().Text;
                if (!Consume(TokenKind.Colon, diagnostics, "期望 ':'"))
                {
                    return null;
                }
                type = ParseType(diagnostics);
                if (type is null)
                {
                    return null;
                }
                if (!Consume(TokenKind.RParen, diagnostics, "期望 ')'") )
                {
                    return null;
                }
                SkipSeparators();
            }

            var block = ParseBlock(diagnostics);
            if (block is null)
            {
                return null;
            }
            except = new ExceptClause(name, type, block, Combine(exKw.Span, block.Span));
            SkipSeparators();
        }

        BlockStmt? fin = null;
        if (Match(TokenKind.KwFinally))
        {
            var finKw = Previous();
            SkipSeparators();
            fin = ParseBlock(diagnostics);
            if (fin is null)
            {
                return null;
            }
            SkipSeparators();
        }

        if (except is null && fin is null)
        {
            diagnostics.Add(Error(tryKw.Span, "try 必须带 except 或 finally"));
        }

        var endSpan = fin is not null ? fin.Span : (except is not null ? except.Span : tryBlock.Span);
        return new TryStmt(tryBlock, except, fin, Combine(tryKw.Span, endSpan));
    }

    private Stmt? ParseVarDeclVarKeyword(List<Diagnostic> diagnostics)
    {
        var kw = Previous();
        if (!Consume(TokenKind.Identifier, diagnostics, "期望变量名"))
        {
            return null;
        }

        var nameTok = Previous();
        if (!Consume(TokenKind.Equals, diagnostics, "期望 '='"))
        {
            return null;
        }

        var init = ParseExpression(diagnostics);
        return new VarDeclStmt(nameTok.Text, null, init, VarDeclKind.VarKeyword, Combine(kw.Span, init.Span));
    }

    private IfStmt? ParseIf(List<Diagnostic> diagnostics)
    {
        var ifTok = Previous();
        var cond = ParseExpression(diagnostics);
        SkipSeparators();
        var thenBlock = ParseBlock(diagnostics);
        if (thenBlock is null)
        {
            return null;
        }

        SkipSeparators();
        BlockStmt? elseBlock = null;
        if (Match(TokenKind.KwElse))
        {
            SkipSeparators();
            elseBlock = ParseBlock(diagnostics);
        }

        var span = elseBlock is null ? Combine(ifTok.Span, thenBlock.Span) : Combine(ifTok.Span, elseBlock.Span);
        return new IfStmt(cond, thenBlock, elseBlock, span);
    }

    private WhileStmt? ParseWhile(List<Diagnostic> diagnostics)
    {
        var whileTok = Previous();
        var cond = ParseExpression(diagnostics);
        SkipSeparators();
        var body = ParseBlock(diagnostics);
        if (body is null)
        {
            return null;
        }
        return new WhileStmt(cond, body, Combine(whileTok.Span, body.Span));
    }

    private IReadOnlyList<TypeParamDecl> ParseTypeParamDecls(List<Diagnostic> diagnostics)
    {
        if (!Match(TokenKind.Less))
        {
            return Array.Empty<TypeParamDecl>();
        }

        var lt = Previous();
        var list = new List<TypeParamDecl>();
        while (!Check(TokenKind.Greater) && !Check(TokenKind.Eof))
        {
            if (!Consume(TokenKind.Identifier, diagnostics, "期望类型参数名"))
            {
                break;
            }
            var nameTok = Previous();

            var constraints = new List<TypeNode>();
            TypeNode? def = null;

            if (Match(TokenKind.Identifier) && string.Equals(Previous().Text, "extends", StringComparison.Ordinal))
            {
                while (true)
                {
                    var c = ParseType(diagnostics);
                    if (c is null) break;
                    constraints.Add(c);
                    if (Match(TokenKind.Comma)) continue;
                    break;
                }
            }

            if (Match(TokenKind.Equals))
            {
                def = ParseType(diagnostics);
            }

            list.Add(new TypeParamDecl(nameTok.Text, constraints, def, Combine(lt.Span, nameTok.Span)));
            if (Match(TokenKind.Comma)) continue;
            break;
        }

        Consume(TokenKind.Greater, diagnostics, "期望 '>'");
        return list;
    }

    private TypeAliasDecl? ParseTypeAliasDecl(List<Diagnostic> diagnostics)
    {
        var kw = Previous();
        if (!Consume(TokenKind.Identifier, diagnostics, "期望别名名"))
        {
            return null;
        }
        var nameTok = Previous();
        var tps = ParseTypeParamDecls(diagnostics);
        if (!Consume(TokenKind.Equals, diagnostics, "期望 '='"))
        {
            return null;
        }
        var target = ParseType(diagnostics);
        if (target is null)
        {
            return null;
        }
        if (Match(TokenKind.Semicolon) || Match(TokenKind.NewLine))
        {
            SkipSeparators();
        }
        return new TypeAliasDecl(nameTok.Text, tps, target, Combine(kw.Span, target.Span));
    }

    private TypeNode? ParseType(List<Diagnostic> diagnostics)
    {
        if (Check(TokenKind.Identifier) && string.Equals(Current().Text, "ptr", StringComparison.Ordinal) && Peek(1).Kind == TokenKind.Less)
        {
            Advance();
            var nameTok = Previous();
            Consume(TokenKind.Less, diagnostics, "期望 '<'");
            var elem = ParseType(diagnostics);
            if (elem is null) return null;
            Consume(TokenKind.Greater, diagnostics, "期望 '>'");
            var gt = Previous();
            return new PtrTypeNode(elem, Combine(nameTok.Span, gt.Span));
        }

        TypeNode? node = null;
        if (Match(TokenKind.LParen))
        {
            var lp = Previous();
            var paramTypes = new List<TypeNode>();
            if (!Check(TokenKind.RParen))
            {
                while (true)
                {
                    var pt = ParseType(diagnostics);
                    if (pt is null)
                    {
                        return null;
                    }
                    paramTypes.Add(pt);
                    if (Match(TokenKind.Comma))
                    {
                        continue;
                    }
                    break;
                }
            }
            if (!Consume(TokenKind.RParen, diagnostics, "期望 ')'") )
            {
                return null;
            }
            if (!Consume(TokenKind.Arrow, diagnostics, "期望 '->'"))
            {
                return null;
            }
            var ret = ParseType(diagnostics);
            if (ret is null)
            {
                return null;
            }
            node = new FunctionTypeNode(paramTypes, ret, Combine(lp.Span, ret.Span));
        }
        else
        {
            if (!Consume(TokenKind.Identifier, diagnostics, "期望类型名"))
            {
                return null;
            }

            var nameTok = Previous();

            var segs = new List<string> { nameTok.Text };
            while (Match(TokenKind.Dot))
            {
                if (!Consume(TokenKind.Identifier, diagnostics, "期望类型名"))
                {
                    return null;
                }
                segs.Add(Previous().Text);
            }

            node = segs.Count == 1
                ? new SimpleTypeNode(segs[0], nameTok.Span)
                : new QualifiedTypeNode(segs, Combine(nameTok.Span, Previous().Span));

            if (Match(TokenKind.Less))
            {
                var args = new List<TypeNode>();
                if (!Check(TokenKind.Greater))
                {
                    while (true)
                    {
                        var ta = ParseType(diagnostics);
                        if (ta is null) return null;
                        args.Add(ta);
                        if (Match(TokenKind.Comma)) continue;
                        break;
                    }
                }
                Consume(TokenKind.Greater, diagnostics, "期望 '>'");
                node = new GenericTypeNode(node, args, Combine(node.Span, Previous().Span));
            }

            while (Match(TokenKind.Dot))
            {
                if (!Consume(TokenKind.Identifier, diagnostics, "期望成员名"))
                {
                    return null;
                }
                var mem = Previous();
                node = new AssocTypeNode(node, mem.Text, Combine(node.Span, mem.Span));
            }
        }

        if (node is null)
        {
            return null;
        }

        while (Check(TokenKind.LBracket) && Peek(1).Kind == TokenKind.RBracket)
        {
            Advance();
            var lb = Previous();
            Advance();
            var rb = Previous();
            node = new ArrayTypeNode(node, Combine(lb.Span, rb.Span));
        }
        return node;
    }

    private Expr ParseExpression(List<Diagnostic> diagnostics)
    {
        return ParseExpressionBp(diagnostics, 0);
    }

    private Expr ParseExpressionBp(List<Diagnostic> diagnostics, int minBp)
    {
        var t = Current();
        Expr left;
        if (Match(TokenKind.KwAwait))
        {
            var kw = Previous();
            var rhs = ParseExpressionBp(diagnostics, 80);
            left = new AwaitExpr(rhs, Combine(kw.Span, rhs.Span));
        }
        else if (Match(TokenKind.KwGo))
        {
            var kw = Previous();
            var rhs = ParseExpressionBp(diagnostics, 80);
            if (rhs is CallExpr c)
            {
                left = new GoExpr(c, Combine(kw.Span, rhs.Span));
            }
            else
            {
                diagnostics.Add(Error(rhs.Span, "go 后必须是调用表达式"));
                left = rhs;
            }
        }
        else if (Match(TokenKind.Amp))
        {
            var opTok = Previous();
            var rhs = ParseExpressionBp(diagnostics, 80);
            left = new AddrOfExpr(rhs, Combine(opTok.Span, rhs.Span));
        }
        else if (Match(TokenKind.Star))
        {
            var opTok = Previous();
            var rhs = ParseExpressionBp(diagnostics, 80);
            left = new DerefExpr(rhs, Combine(opTok.Span, rhs.Span));
        }
        else if (Match(TokenKind.Bang) || Match(TokenKind.Plus) || Match(TokenKind.Minus))
        {
            var opTok = Previous();
            var rhs = ParseExpressionBp(diagnostics, 80);
            left = new UnaryExpr(opTok.Text, rhs, Combine(opTok.Span, rhs.Span));
        }
        else
        {
            left = ParsePrimary(diagnostics);
        }

        while (true)
        {
            if (TryParsePostfix(diagnostics, ref left))
            {
                continue;
            }

            if (Match(TokenKind.KwAs))
            {
                var kw = Previous();
                var castType = ParseType(diagnostics);
                if (castType is null)
                {
                    diagnostics.Add(Error(kw.Span, "as 后期望类型"));
                }
                else
                {
                    left = new CastExpr(left, castType, Combine(left.Span, castType.Span));
                }
                continue;
            }

            var op = Current();
            if (!TryGetInfixBindingPower(op.Kind, out var lbp, out var rbp, out var opText))
            {
                break;
            }

            if (lbp < minBp)
            {
                break;
            }

            Advance();
            var right = ParseExpressionBp(diagnostics, rbp);
            if (op.Kind == TokenKind.Equals)
            {
                if (!IsLValue(left))
                {
                    diagnostics.Add(Error(left.Span, "赋值左侧必须是可赋值表达式"));
                }
                left = new AssignExpr(left, right, Combine(left.Span, right.Span));
            }
            else
            {
                left = new BinaryExpr(opText, left, right, Combine(left.Span, right.Span));
            }
        }

        return left;
    }

    private bool TryParsePostfix(List<Diagnostic> diagnostics, ref Expr left)
    {
        if (TryParseGenericCallPostfix(diagnostics, left, out var genericCall))
        {
            left = genericCall;
            return true;
        }

        if (Match(TokenKind.LParen))
        {
            var lp = Previous();
            var args = new List<Expr>();
            if (!Check(TokenKind.RParen))
            {
                while (true)
                {
                    var e = ParseExpression(diagnostics);
                    args.Add(e);
                    if (Match(TokenKind.Comma))
                    {
                        continue;
                    }
                    break;
                }
            }

            if (!Consume(TokenKind.RParen, diagnostics, "期望 ')'"))
            {
                return true;
            }

            var rp = Previous();
            left = new CallExpr(left, args, Combine(left.Span, rp.Span));
            return true;
        }

        if (Match(TokenKind.LBracket))
        {
            var lb = Previous();
            Expr? lo = null;
            Expr? hi = null;
            if (!Check(TokenKind.Colon) && !Check(TokenKind.RBracket))
            {
                lo = ParseExpression(diagnostics);
            }

            if (Match(TokenKind.Colon))
            {
                if (!Check(TokenKind.RBracket))
                {
                    hi = ParseExpression(diagnostics);
                }
                if (!Consume(TokenKind.RBracket, diagnostics, "期望 ']'"))
                {
                    return true;
                }
                var rb2 = Previous();
                left = new SliceExpr(left, lo, hi, Combine(left.Span, rb2.Span));
                return true;
            }

            if (lo is null)
            {
                diagnostics.Add(Error(lb.Span, "期望索引或切片"));
                if (!Consume(TokenKind.RBracket, diagnostics, "期望 ']'"))
                {
                    return true;
                }
                return true;
            }

            if (!Consume(TokenKind.RBracket, diagnostics, "期望 ']'"))
            {
                return true;
            }
            var rb = Previous();
            left = new IndexExpr(left, lo, Combine(left.Span, rb.Span));
            return true;
        }

        if (Match(TokenKind.Dot))
        {
            if (!Consume(TokenKind.Identifier, diagnostics, "期望成员名"))
            {
                return true;
            }
            var mem = Previous();
            left = new MemberExpr(left, mem.Text, Combine(left.Span, mem.Span));
            return true;
        }

        return false;
    }

    private bool TryParseGenericCallPostfix(List<Diagnostic> diagnostics, Expr callee, out Expr genericCall)
    {
        genericCall = callee;
        if (!Check(TokenKind.Less)) return false;

        if (!LooksLikeGenericArgsStart())
        {
            return false;
        }

        var mark = _index;
        var tempDiagnostics = new List<Diagnostic>();

        Advance();
        var typeArgs = new List<TypeNode>();
        while (!Check(TokenKind.Greater) && !Check(TokenKind.Eof))
        {
            var t = ParseType(tempDiagnostics);
            if (t is null)
            {
                _index = mark;
                return false;
            }
            typeArgs.Add(t);
            if (Match(TokenKind.Comma))
            {
                continue;
            }
            break;
        }

        if (!Check(TokenKind.Greater))
        {
            _index = mark;
            return false;
        }
        Advance();

        if (!Check(TokenKind.LParen))
        {
            _index = mark;
            return false;
        }

        Advance();
        var args = new List<Expr>();
        if (!Check(TokenKind.RParen))
        {
            while (true)
            {
                var e = ParseExpression(tempDiagnostics);
                args.Add(e);
                if (Match(TokenKind.Comma))
                {
                    continue;
                }
                break;
            }
        }
        if (!Check(TokenKind.RParen))
        {
            _index = mark;
            return false;
        }
        Advance();
        var rp = Previous();

        foreach (var d in tempDiagnostics)
        {
            diagnostics.Add(d);
        }
        genericCall = new GenericCallExpr(callee, typeArgs, args, Combine(callee.Span, rp.Span));
        return true;
    }

    private bool LooksLikeGenericArgsStart()
    {
        if (Peek(1).Kind is TokenKind.Identifier)
        {
            return true;
        }
        return false;
    }

    private Expr ParsePrimary(List<Diagnostic> diagnostics)
    {
        if (Match(TokenKind.KwThis))
        {
            return new ThisExpr(Previous().Span);
        }

        if (Match(TokenKind.KwNew))
        {
            var kw = Previous();
            var type = ParseType(diagnostics);
            if (type is null)
            {
                return new IdentifierExpr("__error", kw.Span);
            }

            if (Match(TokenKind.LBracket))
            {
                var size = ParseExpression(diagnostics);
                if (!Consume(TokenKind.RBracket, diagnostics, "期望 ']'"))
                {
                    return new IdentifierExpr("__error", kw.Span);
                }
                var rb = Previous();
                return new NewArrayExpr(type, size, Combine(kw.Span, rb.Span));
            }

            if (!Consume(TokenKind.LParen, diagnostics, "期望 '('") )
            {
                return new IdentifierExpr("__error", kw.Span);
            }
            var args = new List<Expr>();
            if (!Check(TokenKind.RParen))
            {
                while (true)
                {
                    var e = ParseExpression(diagnostics);
                    args.Add(e);
                    if (Match(TokenKind.Comma))
                    {
                        continue;
                    }
                    break;
                }
            }
            Consume(TokenKind.RParen, diagnostics, "期望 ')'");
            var rp = Previous();
            return new NewExpr(type, args, Combine(kw.Span, rp.Span));
        }

        if (Match(TokenKind.KwFun) && Check(TokenKind.LParen))
        {
            var funKw = Previous();
            Consume(TokenKind.LParen, diagnostics, "期望 '('");
            var parameters = new List<ParamDecl>();
            if (!Check(TokenKind.RParen))
            {
                while (true)
                {
                    if (!Consume(TokenKind.Identifier, diagnostics, "期望参数名"))
                    {
                        break;
                    }
                    var paramName = Previous();
                    Consume(TokenKind.Colon, diagnostics, "期望 ':'");
                    var type = ParseType(diagnostics);
                    if (type is null)
                    {
                        break;
                    }
                    parameters.Add(new ParamDecl(paramName.Text, type, paramName.Span));
                    if (Match(TokenKind.Comma))
                    {
                        continue;
                    }
                    break;
                }
            }
            Consume(TokenKind.RParen, diagnostics, "期望 ')'");
            TypeNode? returnType = null;
            if (Match(TokenKind.Colon))
            {
                returnType = ParseType(diagnostics);
            }
            SkipSeparators();
            var body = ParseBlock(diagnostics);
            if (body is null)
            {
                diagnostics.Add(Error(funKw.Span, "lambda 期望 block"));
                return new IdentifierExpr("__error", funKw.Span);
            }
            return new LambdaExpr(parameters, returnType, body, Combine(funKw.Span, body.Span));
        }

        if (Match(TokenKind.Identifier))
        {
            var id = Previous();
            return new IdentifierExpr(id.Text, id.Span);
        }

        if (Match(TokenKind.IntLiteral))
        {
            var tok = Previous();
            return new LiteralExpr(new IntLiteralValue(tok.Text, tok.Suffix), tok.Span);
        }

        if (Match(TokenKind.FloatLiteral))
        {
            var tok = Previous();
            return new LiteralExpr(new FloatLiteralValue(tok.Text, tok.Suffix), tok.Span);
        }

        if (Match(TokenKind.StringLiteral))
        {
            var tok = Previous();
            return new LiteralExpr(new StringLiteralValue(tok.Text), tok.Span);
        }

        if (Match(TokenKind.CharLiteral))
        {
            var tok = Previous();
            return new LiteralExpr(new CharLiteralValue(tok.Text), tok.Span);
        }

        if (Match(TokenKind.KwTrue))
        {
            return new LiteralExpr(new BoolLiteralValue(true), Previous().Span);
        }

        if (Match(TokenKind.KwFalse))
        {
            return new LiteralExpr(new BoolLiteralValue(false), Previous().Span);
        }

        if (Match(TokenKind.KwNull))
        {
            return new LiteralExpr(new NullLiteralValue(), Previous().Span);
        }

        if (Match(TokenKind.LParen))
        {
            var lp = Previous();
            var e = ParseExpression(diagnostics);
            Consume(TokenKind.RParen, diagnostics, "期望 ')'" );
            var rp = Previous();
            return new ParenWrapExpr(e, Combine(lp.Span, rp.Span));
        }

        if (Match(TokenKind.LBracket))
        {
            var lb = Previous();
            var elements = new List<Expr>();
            if (!Check(TokenKind.RBracket))
            {
                while (true)
                {
                    var e = ParseExpression(diagnostics);
                    elements.Add(e);
                    if (Match(TokenKind.Comma))
                    {
                        continue;
                    }
                    break;
                }
            }
            Consume(TokenKind.RBracket, diagnostics, "期望 ']'" );
            var rb = Previous();
            return new ArrayLiteralExpr(elements, Combine(lb.Span, rb.Span));
        }

        diagnostics.Add(Error(Current().Span, "期望表达式"));
        var bad = Current();
        Advance();
        return new IdentifierExpr("__error", bad.Span);
    }

    private static bool IsStatementTerminator(TokenKind kind) => kind is TokenKind.NewLine or TokenKind.Semicolon or TokenKind.RBrace or TokenKind.Eof;

    private static bool IsLValue(Expr expr) => expr is IdentifierExpr or IndexExpr or MemberExpr or DerefExpr;

    private static bool TryGetInfixBindingPower(TokenKind kind, out int lbp, out int rbp, out string opText)
    {
        switch (kind)
        {
            case TokenKind.Equals:
                lbp = 10;
                rbp = 9;
                opText = "=";
                return true;
            case TokenKind.PipePipe:
                lbp = 20;
                rbp = 21;
                opText = "||";
                return true;
            case TokenKind.AmpAmp:
                lbp = 30;
                rbp = 31;
                opText = "&&";
                return true;
            case TokenKind.EqualsEquals:
                lbp = 40;
                rbp = 41;
                opText = "==";
                return true;
            case TokenKind.BangEquals:
                lbp = 40;
                rbp = 41;
                opText = "!=";
                return true;
            case TokenKind.Less:
                lbp = 50;
                rbp = 51;
                opText = "<";
                return true;
            case TokenKind.LessEquals:
                lbp = 50;
                rbp = 51;
                opText = "<=";
                return true;
            case TokenKind.Greater:
                lbp = 50;
                rbp = 51;
                opText = ">";
                return true;
            case TokenKind.GreaterEquals:
                lbp = 50;
                rbp = 51;
                opText = ">=";
                return true;
            case TokenKind.Plus:
                lbp = 60;
                rbp = 61;
                opText = "+";
                return true;
            case TokenKind.Minus:
                lbp = 60;
                rbp = 61;
                opText = "-";
                return true;
            case TokenKind.Star:
                lbp = 70;
                rbp = 71;
                opText = "*";
                return true;
            case TokenKind.Slash:
                lbp = 70;
                rbp = 71;
                opText = "/";
                return true;
            case TokenKind.Percent:
                lbp = 70;
                rbp = 71;
                opText = "%";
                return true;
            default:
                lbp = 0;
                rbp = 0;
                opText = string.Empty;
                return false;
        }
    }

    private void SkipSeparators()
    {
        while (Match(TokenKind.NewLine) || Match(TokenKind.Semicolon))
        {
        }
    }

    private void SkipNewLines()
    {
        while (Match(TokenKind.NewLine))
        {
        }
    }

    private void SynchronizeTopLevel()
    {
        while (!Check(TokenKind.Eof) && !Check(TokenKind.KwFun))
        {
            Advance();
        }
    }

    private bool Consume(TokenKind kind, List<Diagnostic> diagnostics, string message)
    {
        if (Check(kind))
        {
            Advance();
            return true;
        }

        diagnostics.Add(Error(Current().Span, message));
        return false;
    }

    private Diagnostic Error(SourceSpan span, string message) => new(DiagnosticSeverity.Error, span, message);

    private bool Check(TokenKind kind) => Current().Kind == kind;
    private Token Current() => _tokens[Math.Min(_index, _tokens.Count - 1)];
    private Token Previous() => _tokens[Math.Max(_index - 1, 0)];
    private Token Peek(int offset) => _tokens[Math.Min(_index + offset, _tokens.Count - 1)];
    private bool Match(TokenKind kind)
    {
        if (Check(kind))
        {
            Advance();
            return true;
        }
        return false;
    }

    private void Advance() => _index++;

    private static SourceSpan Combine(SourceSpan a, SourceSpan b)
    {
        if (a.SourcePath != b.SourcePath)
        {
            return a;
        }
        var start = Math.Min(a.Start, b.Start);
        var end = Math.Max(a.Start + a.Length, b.Start + b.Length);
        return new SourceSpan(a.SourcePath, start, end - start, a.Line, a.Column);
    }
}
