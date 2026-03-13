using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Alice.Compiler;

internal readonly record struct NodeKey(string SourcePath, int Start, int Length)
{
    public static NodeKey FromSpan(SourceSpan span) => new(span.SourcePath, span.Start, span.Length);
}

internal sealed record BoundClassBaseList(string? BaseClassTypeName, IReadOnlyList<string> InterfaceTypeNames);

internal sealed record BoundInterfaceBases(IReadOnlyList<string> InterfaceTypeNames);

internal sealed record BindingResult(
    ProgramNode Program,
    IReadOnlyDictionary<NodeKey, FunctionTypeNode> InferredVarFunctionTypes,
    IReadOnlyDictionary<NodeKey, BoundClassBaseList> ClassBaseLists,
    IReadOnlyDictionary<NodeKey, BoundInterfaceBases> InterfaceBaseLists,
    IReadOnlyDictionary<NodeKey, TypeNode> ResolvedAssocTypes,
    IReadOnlyDictionary<NodeKey, IReadOnlyList<string>> InterfaceAssocTypeParams,
    IReadOnlyDictionary<NodeKey, IReadOnlyDictionary<string, TypeNode>> ClassAssocTypeBindings,
    IReadOnlyDictionary<NodeKey, IReadOnlyList<string>> ScopeExtraTypeParams,
    IReadOnlyDictionary<NodeKey, IReadOnlyDictionary<string, IReadOnlyList<TypeNode>>> ScopeTypeParamWhereConstraints,
    IReadOnlyList<QualifiedName> MissingModules,
    bool HasErrors);

internal sealed class Binder
{
    private readonly IReadOnlyList<CompilationUnit> _units;
    private readonly TextWriter _diagnostics;
    private readonly bool _strictUnknownTypes;

    private readonly Dictionary<string, TypeSymbol> _typesByFullName;
    private readonly Dictionary<(string Ns, string Name), TypeSymbol> _typesByNamespaceAndName;
    private readonly Dictionary<string, TypeAliasDecl> _typeAliasesByUniqueName;

    private readonly Dictionary<(string UnitPath, string Name), FunDecl> _topLevelFunctions;
    private readonly Dictionary<(string UnitPath, string Name), GlobalVarDecl> _topLevelGlobals;

    private readonly HashSet<string> _referencedInterfaces;
    private readonly Dictionary<NodeKey, FunctionTypeNode> _inferredVarFuncTypes;
    private readonly Dictionary<NodeKey, BoundClassBaseList> _classBaseLists;
    private readonly Dictionary<NodeKey, BoundInterfaceBases> _interfaceBaseLists;
    private readonly Dictionary<NodeKey, TypeNode> _resolvedAssocTypes;
    private readonly Dictionary<NodeKey, IReadOnlyList<string>> _interfaceAssocTypeParams;
    private readonly Dictionary<NodeKey, IReadOnlyDictionary<string, TypeNode>> _classAssocTypeBindings;
    private readonly Dictionary<NodeKey, IReadOnlyList<string>> _scopeExtraTypeParams;
    private readonly Dictionary<NodeKey, IReadOnlyDictionary<string, IReadOnlyList<TypeNode>>> _scopeTypeParamWhereConstraints;
    private readonly List<QualifiedName> _missingModules;
    private readonly HashSet<string> _missingModuleNames;

    private bool _hasErrors;

    internal Binder(IReadOnlyList<CompilationUnit> units, TextWriter diagnostics, bool strictUnknownTypes)
    {
        _units = units;
        _diagnostics = diagnostics;
        _strictUnknownTypes = strictUnknownTypes;
        _typesByFullName = new Dictionary<string, TypeSymbol>(StringComparer.Ordinal);
        _typesByNamespaceAndName = new Dictionary<(string Ns, string Name), TypeSymbol>();
        _typeAliasesByUniqueName = new Dictionary<string, TypeAliasDecl>(StringComparer.Ordinal);
        _topLevelFunctions = new Dictionary<(string UnitPath, string Name), FunDecl>();
        _topLevelGlobals = new Dictionary<(string UnitPath, string Name), GlobalVarDecl>();
        _referencedInterfaces = new HashSet<string>(StringComparer.Ordinal);
        _inferredVarFuncTypes = new Dictionary<NodeKey, FunctionTypeNode>();
        _classBaseLists = new Dictionary<NodeKey, BoundClassBaseList>();
        _interfaceBaseLists = new Dictionary<NodeKey, BoundInterfaceBases>();
        _resolvedAssocTypes = new Dictionary<NodeKey, TypeNode>();
        _interfaceAssocTypeParams = new Dictionary<NodeKey, IReadOnlyList<string>>();
        _classAssocTypeBindings = new Dictionary<NodeKey, IReadOnlyDictionary<string, TypeNode>>();
        _scopeExtraTypeParams = new Dictionary<NodeKey, IReadOnlyList<string>>();
        _scopeTypeParamWhereConstraints = new Dictionary<NodeKey, IReadOnlyDictionary<string, IReadOnlyList<TypeNode>>>();
        _missingModules = new List<QualifiedName>();
        _missingModuleNames = new HashSet<string>(StringComparer.Ordinal);
        _hasErrors = false;
    }

    internal BindingResult Bind()
    {
        CollectTopLevelSymbols();
        CollectReferencedInterfaces();
        BindAssociatedTypes();
        BindInterfaces();
        BindClasses();
        ValidateAllTypeAliases(ns: string.Empty);
        BindBodiesForThisRaiseAndFuncRefs();

        return new BindingResult(
            new ProgramNode(_units),
            _inferredVarFuncTypes,
            _classBaseLists,
            _interfaceBaseLists,
            _resolvedAssocTypes,
            _interfaceAssocTypeParams,
            _classAssocTypeBindings,
            _scopeExtraTypeParams,
            _scopeTypeParamWhereConstraints,
            _missingModules,
            _hasErrors);
    }

    private void BindAssociatedTypes()
    {
        foreach (var unit in _units)
        {
            foreach (var itf in unit.Interfaces)
            {
                var key = NodeKey.FromSpan(itf.Span);
                var assocNames = itf.Entries.OfType<AssociatedTypeDecl>().Select(e => e.Name).Distinct(StringComparer.Ordinal).ToList();
                _interfaceAssocTypeParams[key] = assocNames;
                if (assocNames.Count > 0)
                {
                    _scopeExtraTypeParams[key] = assocNames.Select(n => "__Assoc_" + n).ToList();
                    _scopeTypeParamWhereConstraints[key] = new Dictionary<string, IReadOnlyList<TypeNode>>(StringComparer.Ordinal);
                }
            }
        }

        foreach (var unit in _units)
        {
            foreach (var f in unit.Functions)
            {
                var ns = UnitNamespace(unit);
                var key = NodeKey.FromSpan(f.Span);
                var map = BuildAssocConstraintsFromTypeParams(ns, f.TypeParams);
                if (map.Count > 0)
                {
                    _scopeTypeParamWhereConstraints[key] = map;
                    _scopeExtraTypeParams[key] = map.Keys.ToList();
                }
            }

            foreach (var cls in unit.Classes)
            {
                var ns = UnitNamespace(unit);
                foreach (var m in cls.Members)
                {
                    if (m is not MethodDecl md) continue;
                    var key = NodeKey.FromSpan(md.Span);
                    var map = BuildAssocConstraintsFromTypeParams(ns, md.TypeParams);
                    if (map.Count > 0)
                    {
                        _scopeTypeParamWhereConstraints[key] = map;
                        _scopeExtraTypeParams[key] = map.Keys.ToList();
                    }
                }
            }
        }



        foreach (var unit in _units)
        {
            foreach (var cls in unit.Classes)
            {
                var key = NodeKey.FromSpan(cls.Span);
                var map = new Dictionary<string, TypeNode>(StringComparer.Ordinal);
                foreach (var mem in cls.Members)
                {
                    if (mem is not AssociatedTypeBindDecl atb) continue;
                    if (!map.TryAdd(atb.Name, atb.ValueType))
                    {
                        Error(atb.Span, $"重复绑定关联类型: {cls.Name}.{atb.Name}");
                    }
                }
                _classAssocTypeBindings[key] = map;
            }
        }

        foreach (var unit in _units)
        {
            var ns = UnitNamespace(unit);
            foreach (var cls in unit.Classes)
            {
                var key = NodeKey.FromSpan(cls.Span);
                if (!_classAssocTypeBindings.TryGetValue(key, out var binds))
                {
                    binds = new Dictionary<string, TypeNode>(StringComparer.Ordinal);
                }

                var whereMap = new Dictionary<string, IReadOnlyList<TypeNode>>(StringComparer.Ordinal);

                foreach (var bt in cls.BaseTypes)
                {
                    var sym = ResolveAnyTypeSymbol(ns, bt.Name, bt.Span, requireKnown: false);
                    if (sym?.Interface is null) continue;
                    var itfKey = NodeKey.FromSpan(sym.Interface.Span);
                    if (!_interfaceAssocTypeParams.TryGetValue(itfKey, out var assocNames) || assocNames.Count == 0) continue;

                    foreach (var an in assocNames)
                    {
                        TypeNode? t = null;
                        if (bt.TypeArguments.Count > 0)
                        {
                            var idx = 0;
                            for (var i = 0; i < assocNames.Count; i++)
                            {
                                if (string.Equals(assocNames[i], an, StringComparison.Ordinal))
                                {
                                    idx = i;
                                    break;
                                }
                            }
                            if (idx >= 0 && idx < bt.TypeArguments.Count)
                            {
                                t = bt.TypeArguments[idx];
                            }
                        }

                        if (t is null && !binds.TryGetValue(an, out t))
                        {
                            Error(cls.Span, $"类 {cls.Name} 实现接口 {sym.FullName} 但缺少关联类型绑定: {an}");
                            continue;
                        }

                        whereMap["__Assoc_" + an] = new[] { t };
                    }
                }

                if (whereMap.Count > 0)
                {
                    _scopeTypeParamWhereConstraints[key] = whereMap;
                }
            }
        }

        foreach (var unit in _units)
        {
            var ns = UnitNamespace(unit);

            foreach (var g in unit.Globals)
            {
                ResolveAssocTypesInType(ns, TypeEnv.Empty, NodeKey.FromSpan(g.Span), g.Type);
            }

            foreach (var ta in unit.TypeAliases)
            {
                var envTa = TypeEnv.Empty.AddMany(ta.TypeParams.Select(tp => tp.Name));
                ResolveAssocTypesInType(ns, envTa, NodeKey.FromSpan(ta.Span), ta.Target);
            }

            foreach (var f in unit.Functions)
            {
                var fk = NodeKey.FromSpan(f.Span);
                var env = TypeEnv.Empty.AddMany(f.TypeParams.Select(tp => tp.Name));
                foreach (var p in f.Parameters) ResolveAssocTypesInType(ns, env, fk, p.Type);
                if (f.ReturnType is not null) ResolveAssocTypesInType(ns, env, fk, f.ReturnType);

                foreach (var tp in f.TypeParams)
                {
                    foreach (var c in tp.Constraints)
                    {
                        ResolveAssocTypesInType(ns, env, fk, c);
                    }
                }
            }

            foreach (var cls in unit.Classes)
            {
                var ck = NodeKey.FromSpan(cls.Span);
                var env = TypeEnv.Empty.AddMany(cls.TypeParams.Select(tp => tp.Name));
                foreach (var mem in cls.Members)
                {
                    switch (mem)
                    {
                        case FieldDecl fd:
                            ResolveAssocTypesInType(ns, env, NodeKey.FromSpan(fd.Span), fd.Type);
                            break;
                        case MethodDecl md:
                            var mk = NodeKey.FromSpan(md.Span);
                            var env2 = env.AddMany(md.TypeParams.Select(tp => tp.Name));
                            foreach (var p in md.Parameters) ResolveAssocTypesInType(ns, env2, mk, p.Type);
                            if (md.ReturnType is not null) ResolveAssocTypesInType(ns, env2, mk, md.ReturnType);

                            foreach (var tp in md.TypeParams)
                            {
                                foreach (var c in tp.Constraints)
                                {
                                    ResolveAssocTypesInType(ns, env2, mk, c);
                                }
                            }
                            break;
                        case CtorDecl cd:
                            foreach (var p in cd.Parameters) ResolveAssocTypesInType(ns, env, NodeKey.FromSpan(cd.Span), p.Type);
                            break;
                    }
                }

                foreach (var tp in cls.TypeParams)
                {
                    foreach (var c in tp.Constraints)
                    {
                        ResolveAssocTypesInType(ns, env, ck, c);
                    }
                }
            }

            foreach (var itf in unit.Interfaces)
            {
                var ik = NodeKey.FromSpan(itf.Span);
                var assocNames = _interfaceAssocTypeParams.TryGetValue(ik, out var an0) ? an0 : Array.Empty<string>();
                var env = TypeEnv.Empty.AddMany(itf.TypeParams.Select(tp => tp.Name)).AddMany(assocNames);
                foreach (var bt in itf.BaseTypes)
                {
                    Ignore(bt);
                }
                foreach (var e in itf.Entries)
                {
                    if (e is InterfaceMethodSig ms)
                    {
                        foreach (var p in ms.Parameters) ResolveAssocTypesInType(ns, env, ik, p.Type);
                        if (ms.ReturnType is not null) ResolveAssocTypesInType(ns, env, ik, ms.ReturnType);
                    }
                }

            }
        }
    }

    private Dictionary<string, IReadOnlyList<TypeNode>> BuildAssocConstraintsFromTypeParams(string ns, IReadOnlyList<TypeParamDecl> tps)
    {
        var result = new Dictionary<string, IReadOnlyList<TypeNode>>(StringComparer.Ordinal);
        foreach (var tp in tps)
        {
            foreach (var c in tp.Constraints)
            {
                if (c is not SimpleTypeNode s) continue;
                var sym = ResolveAnyTypeSymbol(ns, s.Name, s.Span, requireKnown: false);
                if (sym?.Interface is null) continue;
                var ik = NodeKey.FromSpan(sym.Interface.Span);
                if (!_interfaceAssocTypeParams.TryGetValue(ik, out var assocNames)) continue;
                foreach (var an in assocNames)
                {
                    result["__Assoc_" + an] = Array.Empty<TypeNode>();
                }
            }
        }
        return result;
    }

    private void ResolveAssocTypesInType(string ns, TypeEnv env, NodeKey scopeKey, TypeNode node)
    {
        switch (node)
        {
            case SimpleTypeNode s0:
                if (_interfaceAssocTypeParams.TryGetValue(scopeKey, out var assocNames) && assocNames.Contains(s0.Name))
                {
                    _resolvedAssocTypes[NodeKey.FromSpan(s0.Span)] = new SimpleTypeNode("__Assoc_" + s0.Name, AssocInternalSpan(s0.Span));
                }
                return;
            case QualifiedTypeNode q:
                if (TryGetAssocProjectionFromQualified(ns, env, q, out var baseType, out var member))
                {
                    var assoc = new AssocTypeNode(baseType, member, q.Span);
                    ResolveAssocTypesInType(ns, env, scopeKey, assoc);
                }
                return;
            case ArrayTypeNode a:
                ResolveAssocTypesInType(ns, env, scopeKey, a.ElementType);
                return;
            case FunctionTypeNode f:
                foreach (var p in f.ParameterTypes) ResolveAssocTypesInType(ns, env, scopeKey, p);
                ResolveAssocTypesInType(ns, env, scopeKey, f.ReturnType);
                return;
            case GenericTypeNode g:
                ResolveAssocTypesInType(ns, env, scopeKey, g.BaseType);
                foreach (var ta in g.TypeArguments) ResolveAssocTypesInType(ns, env, scopeKey, ta);
                return;
            case AssocTypeNode a2:
                if (a2.BaseType is SimpleTypeNode s2 && env.IsTypeParam(s2.Name))
                {
                    if (_scopeTypeParamWhereConstraints.TryGetValue(scopeKey, out var wc) && wc.ContainsKey("__Assoc_" + a2.Member))
                    {
                        _resolvedAssocTypes[NodeKey.FromSpan(a2.Span)] = new SimpleTypeNode("__Assoc_" + a2.Member, AssocInternalSpan(a2.Span));
                    }
                    return;
                }

                ResolveAssocTypesInType(ns, env, scopeKey, a2.BaseType);
                var resolved = TryResolveAssocType(ns, env, a2);
                if (resolved is not null)
                {
                    _resolvedAssocTypes[NodeKey.FromSpan(a2.Span)] = resolved;
                    ResolveAssocTypesInType(ns, env, scopeKey, resolved);
                }
                return;
        }
    }

    private TypeNode? TryResolveAssocType(string ns, TypeEnv env, AssocTypeNode node)
    {
        if (node.BaseType is SimpleTypeNode s)
        {
            if (env.IsTypeParam(s.Name)) return null;

            var sym = ResolveAnyTypeSymbol(ns, s.Name, s.Span, requireKnown: true);
            if (sym is null) return null;

            if (!sym.IsInterface && sym.Class is not null)
            {
                return TryResolveAssocTypeFromClass(ns, sym.Class, node.Member, node.Span);
            }

            if (sym.IsInterface && sym.Interface is not null)
            {
                Error(node.Span, $"无法解析关联类型 {sym.FullName}.{node.Member}: 需要对具体 class 做投影");
                return null;
            }
        }

        return null;
    }

    private TypeNode? TryResolveAssocTypeFromClass(string ns, ClassDecl cls, string member, SourceSpan span)
    {
        var key = NodeKey.FromSpan(cls.Span);
        if (!_classAssocTypeBindings.TryGetValue(key, out var map))
        {
            return null;
        }
        if (!map.TryGetValue(member, out var t))
        {
            Error(span, $"类 {cls.Name} 未绑定关联类型: {member}");
            return null;
        }
        ValidateTypeExists(ns, TypeEnv.Empty.AddMany(cls.TypeParams.Select(tp => tp.Name)), t);
        return t;
    }

    private static void Ignore<T>(T value)
    {
        _ = value;
    }

    private static SourceSpan AssocInternalSpan(SourceSpan span) => new("<assoc>", span.Start, span.Length, span.Line, span.Column);

    private sealed record TypeSymbol(string Namespace, string Name, bool IsInterface, InterfaceDecl? Interface, ClassDecl? Class, StructDecl? Struct)
    {
        public string FullName => string.IsNullOrWhiteSpace(Namespace) ? Name : Namespace + "." + Name;
    }

    private readonly record struct TypeEnv(IReadOnlyCollection<string> TypeParams)
    {
        public static readonly TypeEnv Empty = new(Array.Empty<string>());
        public bool IsTypeParam(string name) => TypeParams.Contains(name);
        public TypeEnv AddMany(IEnumerable<string> names) => new(TypeParams.Concat(names).ToArray());
    }

    private TypeSymbol? ResolveAnyTypeSymbol(string currentNamespace, string name, SourceSpan span, bool requireKnown)
    {
        if (IsBuiltinType(name))
        {
            if (requireKnown)
            {
                Error(span, $"未知类型: {name}");
            }
            return null;
        }

        if (_typesByNamespaceAndName.TryGetValue((currentNamespace, name), out var sym0))
        {
            return sym0;
        }

        var matches = _typesByFullName.Values.Where(t => t.Name == name).ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }

        if (requireKnown)
        {
            AddMissingModule(!string.IsNullOrWhiteSpace(currentNamespace)
                ? new QualifiedName(currentNamespace.Split('.').Concat(new[] { name }).ToArray())
                : new QualifiedName(new[] { name }));
            if (_strictUnknownTypes)
            {
                Error(span, $"未知类型: {name}");
            }
        }
        return null;
    }

    private static string UnitNamespace(CompilationUnit unit)
    {
        var segs = unit.Namespace?.Name.Segments ?? Array.Empty<string>();
        return string.Join(".", segs);
    }

    private void CollectTopLevelSymbols()
    {
        foreach (var unit in _units)
        {
            var ns = UnitNamespace(unit);

            foreach (var ta in unit.TypeAliases)
            {
                if (!_typeAliasesByUniqueName.TryAdd(ta.Name, ta))
                {
                    // 名称冲突：后续发射阶段不做“按名唯一”的 alias 展开
                    _typeAliasesByUniqueName.Remove(ta.Name);
                }
            }

            foreach (var itf in unit.Interfaces)
            {
                var sym = new TypeSymbol(ns, itf.Name, IsInterface: true, itf, Class: null, Struct: null);
                AddType(sym, itf.Span);
            }

            foreach (var cls in unit.Classes)
            {
                var sym = new TypeSymbol(ns, cls.Name, IsInterface: false, Interface: null, cls, Struct: null);
                AddType(sym, cls.Span);
            }

            foreach (var st in unit.Structs)
            {
                var sym = new TypeSymbol(ns, st.Name, IsInterface: false, Interface: null, Class: null, Struct: st);
                AddType(sym, st.Span);
            }

            foreach (var f in unit.Functions)
            {
                var key = (unit.SourcePath, f.Name);
                if (_topLevelFunctions.ContainsKey(key))
                {
                    Error(f.Span, $"重复定义顶层函数: {f.Name}");
                    continue;
                }
                _topLevelFunctions[key] = f;
            }

            foreach (var g in unit.Globals)
            {
                var key = (unit.SourcePath, g.Name);
                if (_topLevelGlobals.ContainsKey(key))
                {
                    Error(g.Span, $"重复定义顶层全局变量: {g.Name}");
                    continue;
                }
                _topLevelGlobals[key] = g;
            }
        }
    }

    private bool TryGetUniqueTypeAlias(string name, out TypeAliasDecl alias)
    {
        return _typeAliasesByUniqueName.TryGetValue(name, out alias!);
    }

    private bool HasAnyZeroTypeParamTypeAlias(string name)
    {
        foreach (var unit in _units)
        {
            foreach (var ta in unit.TypeAliases)
            {
                if (!string.Equals(ta.Name, name, StringComparison.Ordinal)) continue;
                if (ta.TypeParams.Count == 0) return true;
            }
        }
        return false;
    }

    private bool HasAnyTypeAlias(string name)
    {
        foreach (var unit in _units)
        {
            foreach (var ta in unit.TypeAliases)
            {
                if (string.Equals(ta.Name, name, StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    private void AddType(TypeSymbol sym, SourceSpan span)
    {
        var full = sym.FullName;
        if (_typesByFullName.ContainsKey(full))
        {
            Error(span, $"重复定义类型: {full}");
            return;
        }
        _typesByFullName[full] = sym;
        _typesByNamespaceAndName[(sym.Namespace, sym.Name)] = sym;
    }

    private void CollectReferencedInterfaces()
    {
        foreach (var unit in _units)
        {
            foreach (var g in unit.Globals)
            {
                CollectTypeRefs(unit, TypeEnv.Empty, g.Type);
            }

            foreach (var ta in unit.TypeAliases)
            {
                var envTa = TypeEnv.Empty.AddMany(ta.TypeParams.Select(tp => tp.Name));
                CollectTypeRefs(unit, envTa, ta.Target);
            }

            foreach (var f in unit.Functions)
            {
                var env = TypeEnv.Empty.AddMany(f.TypeParams.Select(tp => tp.Name));
                foreach (var p in f.Parameters) CollectTypeRefs(unit, env, p.Type);
                if (f.ReturnType is not null) CollectTypeRefs(unit, env, f.ReturnType);
                CollectTypeRefsFromBlock(unit, env, f.Body);
            }

            foreach (var cls in unit.Classes)
            {
                var env = TypeEnv.Empty.AddMany(cls.TypeParams.Select(tp => tp.Name));
                foreach (var bt in cls.BaseTypes)
                {
                    CollectNamedTypeRef(unit, bt.Name, bt.Span);
                }
                foreach (var m in cls.Members)
                {
                    switch (m)
                    {
                        case FieldDecl fd:
                            CollectTypeRefs(unit, env, fd.Type);
                            break;
                        case MethodDecl md:
                            var env2 = env.AddMany(md.TypeParams.Select(tp => tp.Name));
                            foreach (var p in md.Parameters) CollectTypeRefs(unit, env2, p.Type);
                            if (md.ReturnType is not null) CollectTypeRefs(unit, env2, md.ReturnType);
                            CollectTypeRefsFromBlock(unit, env2, md.Body);
                            break;
                        case CtorDecl cd:
                            foreach (var p in cd.Parameters) CollectTypeRefs(unit, env, p.Type);
                            CollectTypeRefsFromBlock(unit, env, cd.Body);
                            break;
                        case AssociatedTypeBindDecl atb:
                            CollectTypeRefs(unit, env, atb.ValueType);
                            break;
                    }
                }
            }

            foreach (var itf in unit.Interfaces)
            {
                var env = TypeEnv.Empty.AddMany(itf.TypeParams.Select(tp => tp.Name));
                foreach (var bt in itf.BaseTypes)
                {
                    CollectNamedTypeRef(unit, bt.Name, bt.Span);
                }
                foreach (var e in itf.Entries)
                {
                    switch (e)
                    {
                        case InterfaceMethodSig ms:
                            var env2 = env.AddMany(itf.Entries.OfType<AssociatedTypeDecl>().Select(a => a.Name));
                            foreach (var p in ms.Parameters) CollectTypeRefs(unit, env2, p.Type);
                            if (ms.ReturnType is not null) CollectTypeRefs(unit, env2, ms.ReturnType);
                            break;
                        case InterfaceEmbedEntry ie:
                            CollectNamedTypeRef(unit, ie.Embedded.Name, ie.Embedded.Span);
                            break;
                        case AssociatedTypeDecl:
                            break;
                    }
                }
            }
        }
    }

    private void CollectTypeRefsFromBlock(CompilationUnit unit, TypeEnv env, BlockStmt block)
    {
        foreach (var st in block.Statements)
        {
            CollectTypeRefsFromStmt(unit, env, st);
        }
    }

    private void CollectTypeRefsFromStmt(CompilationUnit unit, TypeEnv env, Stmt st)
    {
        switch (st)
        {
            case BlockStmt b:
                CollectTypeRefsFromBlock(unit, env, b);
                break;
            case VarDeclStmt v:
                if (v.ExplicitType is not null) CollectTypeRefs(unit, env, v.ExplicitType);
                CollectTypeRefsFromExpr(unit, env, v.Init);
                break;
            case ExprStmt e:
                CollectTypeRefsFromExpr(unit, env, e.Expression);
                break;
            case IfStmt i:
                CollectTypeRefsFromExpr(unit, env, i.Condition);
                CollectTypeRefsFromBlock(unit, env, i.Then);
                if (i.Else is not null) CollectTypeRefsFromBlock(unit, env, i.Else);
                break;
            case WhileStmt w:
                CollectTypeRefsFromExpr(unit, env, w.Condition);
                CollectTypeRefsFromBlock(unit, env, w.Body);
                break;
            case ReturnStmt r:
                if (r.Expression is not null) CollectTypeRefsFromExpr(unit, env, r.Expression);
                break;
            case TryStmt t:
                CollectTypeRefsFromBlock(unit, env, t.TryBlock);
                if (t.Except is not null)
                {
                    if (t.Except.Type is not null) CollectTypeRefs(unit, env, t.Except.Type);
                    CollectTypeRefsFromBlock(unit, env, t.Except.Block);
                }
                if (t.Finally is not null) CollectTypeRefsFromBlock(unit, env, t.Finally);
                break;
            case RaiseStmt rs:
                if (rs.Expression is not null) CollectTypeRefsFromExpr(unit, env, rs.Expression);
                break;
            case DeferStmt d:
                CollectTypeRefsFromExpr(unit, env, d.Expression);
                break;
        }
    }

    private void CollectTypeRefsFromExpr(CompilationUnit unit, TypeEnv env, Expr e)
    {
        switch (e)
        {
            case IdentifierExpr:
            case LiteralExpr:
            case ThisExpr:
                break;
            case UnaryExpr u:
                CollectTypeRefsFromExpr(unit, env, u.Operand);
                break;
            case BinaryExpr b:
                CollectTypeRefsFromExpr(unit, env, b.Left);
                CollectTypeRefsFromExpr(unit, env, b.Right);
                break;
            case AssignExpr a:
                CollectTypeRefsFromExpr(unit, env, a.Target);
                CollectTypeRefsFromExpr(unit, env, a.Value);
                break;
            case CallExpr c:
                CollectTypeRefsFromExpr(unit, env, c.Callee);
                foreach (var arg in c.Args) CollectTypeRefsFromExpr(unit, env, arg);
                break;
            case GenericCallExpr gc:
                CollectTypeRefsFromExpr(unit, env, gc.Callee);
                foreach (var ta in gc.TypeArgs) CollectTypeRefs(unit, env, ta);
                foreach (var arg in gc.Args) CollectTypeRefsFromExpr(unit, env, arg);
                break;
            case IndexExpr i:
                CollectTypeRefsFromExpr(unit, env, i.Target);
                CollectTypeRefsFromExpr(unit, env, i.Index);
                break;
            case MemberExpr m:
                CollectTypeRefsFromExpr(unit, env, m.Target);
                break;
            case ArrayLiteralExpr arr:
                foreach (var el in arr.Elements) CollectTypeRefsFromExpr(unit, env, el);
                break;
            case ParenWrapExpr p:
                CollectTypeRefsFromExpr(unit, env, p.Inner);
                break;
            case NewExpr ne:
                CollectTypeRefs(unit, env, ne.Type);
                foreach (var a0 in ne.Args) CollectTypeRefsFromExpr(unit, env, a0);
                break;
            case NewArrayExpr nae:
                CollectTypeRefs(unit, env, nae.ElementType);
                CollectTypeRefsFromExpr(unit, env, nae.Size);
                break;
            case LambdaExpr le:
                foreach (var p0 in le.Parameters) CollectTypeRefs(unit, env, p0.Type);
                if (le.ReturnType is not null) CollectTypeRefs(unit, env, le.ReturnType);
                CollectTypeRefsFromBlock(unit, env, le.Body);
                break;
        }
    }

    private void CollectTypeRefs(CompilationUnit unit, TypeEnv env, TypeNode node)
    {
        switch (node)
        {
            case SimpleTypeNode s:
                if (env.IsTypeParam(s.Name)) return;
                CollectNamedTypeRef(unit, s.Name, s.Span);
                break;
            case QualifiedTypeNode q:
                if (TryGetAssocProjectionFromQualified(ns: UnitNamespace(unit), env, q, out var baseType, out _))
                {
                    CollectTypeRefs(unit, env, baseType);
                    break;
                }
                if (q.Segments.Count > 0)
                {
                    CollectNamedTypeRef(unit, q.Segments.Last(), q.Span);
                }
                break;
            case ArrayTypeNode a:
                CollectTypeRefs(unit, env, a.ElementType);
                break;
            case FunctionTypeNode f:
                foreach (var p in f.ParameterTypes) CollectTypeRefs(unit, env, p);
                CollectTypeRefs(unit, env, f.ReturnType);
                break;
            case GenericTypeNode g:
                CollectTypeRefs(unit, env, g.BaseType);
                foreach (var ta in g.TypeArguments) CollectTypeRefs(unit, env, ta);
                break;
            case AssocTypeNode a2:
                CollectTypeRefs(unit, env, a2.BaseType);
                break;
        }
    }

    private void CollectNamedTypeRef(CompilationUnit unit, string name, SourceSpan span)
    {
        if (IsBuiltinType(name)) return;
        if (HasAnyTypeAlias(name)) return;

        var ns = UnitNamespace(unit);
        var sym = ResolveAnyTypeSymbol(ns, name, span, requireKnown: false);
        if (sym is not null)
        {
            if (sym.IsInterface)
            {
                _referencedInterfaces.Add(sym.FullName);
            }
            return;
        }

        var qn = !string.IsNullOrWhiteSpace(ns)
            ? new QualifiedName(ns.Split('.').Concat(new[] { name }).ToArray())
            : new QualifiedName(new[] { name });
        AddMissingModule(qn);
        if (_strictUnknownTypes)
        {
            Error(span, $"未知类型: {name}");
        }
    }

    private void AddMissingModule(QualifiedName qn)
    {
        var key = qn.ToString();
        if (_missingModuleNames.Add(key))
        {
            _missingModules.Add(qn);
        }
    }

    private void BindInterfaces()
    {
        foreach (var unit in _units)
        {
            var ns = UnitNamespace(unit);
            foreach (var itf in unit.Interfaces)
            {
                var key = NodeKey.FromSpan(itf.Span);

                var baseIfaces = new List<string>();
                foreach (var bt in itf.BaseTypes)
                {
                    var sym = ResolveAnyTypeSymbol(ns, bt.Name, bt.Span, requireKnown: true);
                    if (sym is null) continue;
                    if (!sym.IsInterface)
                    {
                        Error(bt.Span, $"interface 只能继承 interface: {bt.Name}");
                        continue;
                    }
                    baseIfaces.Add(sym.FullName);
                }
                foreach (var e in itf.Entries)
                {
                    if (e is InterfaceEmbedEntry ie)
                    {
                        var sym = ResolveAnyTypeSymbol(ns, ie.Embedded.Name, ie.Embedded.Span, requireKnown: true);
                        if (sym is null) continue;
                        if (!sym.IsInterface)
                        {
                            Error(ie.Embedded.Span, $"嵌入条目必须是 interface: {ie.Embedded.Name}");
                            continue;
                        }
                        baseIfaces.Add(sym.FullName);
                    }
                }

                _interfaceBaseLists[key] = new BoundInterfaceBases(baseIfaces.Distinct(StringComparer.Ordinal).ToList());
            }
        }
    }

    private readonly record struct MethodSigKey(string Name, string ParamSig, string ReturnType)
    {
        public override string ToString() => $"{Name}({ParamSig}):{ReturnType}";
    }

    private static string MakeParamSig(IEnumerable<string> parts) => string.Join(",", parts);

    private Dictionary<string, HashSet<MethodSigKey>> BuildInterfaceMethodSets()
    {
        var map = new Dictionary<string, HashSet<MethodSigKey>>(StringComparer.Ordinal);
        foreach (var unit in _units)
        {
            var ns = UnitNamespace(unit);
            foreach (var itf in unit.Interfaces)
            {
                var full = string.IsNullOrWhiteSpace(ns) ? itf.Name : ns + "." + itf.Name;
                map[full] = new HashSet<MethodSigKey>();
            }
        }

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        HashSet<MethodSigKey> Get(string full)
        {
            if (visited.Contains(full)) return map[full];
            if (visiting.Contains(full))
            {
                return map[full];
            }
            visiting.Add(full);
            var sym = _typesByFullName.TryGetValue(full, out var ts) ? ts : null;
            if (sym?.Interface is null)
            {
                visiting.Remove(full);
                visited.Add(full);
                return map[full];
            }

            var unitNs = sym.Namespace;
            var key = NodeKey.FromSpan(sym.Interface.Span);
            var bases = _interfaceBaseLists.TryGetValue(key, out var b) ? b.InterfaceTypeNames : Array.Empty<string>();
            foreach (var bfull in bases)
            {
                if (!map.ContainsKey(bfull))
                {
                    continue;
                }
                foreach (var m in Get(bfull)) map[full].Add(m);
            }

            foreach (var e in sym.Interface.Entries)
            {
                if (e is InterfaceMethodSig ms)
                {
                    var assocNames = _interfaceAssocTypeParams.TryGetValue(key, out var an0) ? an0 : Array.Empty<string>();
                    string KeyOf(TypeNode t)
                    {
                        if (t is SimpleTypeNode s && assocNames.Contains(s.Name))
                        {
                            return "__Assoc_" + s.Name;
                        }
                        return TypeKey(unitNs, t);
                    }

                    var pt = ms.Parameters.Select(p => KeyOf(p.Type)).ToArray();
                    var rt = ms.ReturnType is null ? "void" : KeyOf(ms.ReturnType);
                    map[full].Add(new MethodSigKey(ms.Name, MakeParamSig(pt), rt));
                }
            }

            visiting.Remove(full);
            visited.Add(full);
            return map[full];
        }

        foreach (var full in map.Keys.ToArray())
        {
            Get(full);
        }
        return map;
    }

    private HashSet<string> BuildReferencedInterfaceClosure()
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var i in _referencedInterfaces) result.Add(i);

        var visiting = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string full)
        {
            if (!result.Add(full)) return;
            if (!visiting.Add(full)) return;
            if (_typesByFullName.TryGetValue(full, out var sym) && sym.Interface is not null)
            {
                var key = NodeKey.FromSpan(sym.Interface.Span);
                if (_interfaceBaseLists.TryGetValue(key, out var bases))
                {
                    foreach (var b in bases.InterfaceTypeNames)
                    {
                        Visit(b);
                    }
                }
            }
            visiting.Remove(full);
        }

        foreach (var i in _referencedInterfaces.ToArray())
        {
            Visit(i);
        }

        return result;
    }

    private void BindClasses()
    {
        var interfaceMethods = BuildInterfaceMethodSets();
        var referencedClosure = BuildReferencedInterfaceClosure();

        foreach (var unit in _units)
        {
            var ns = UnitNamespace(unit);
            foreach (var cls in unit.Classes)
            {
                var baseClass = (string?)null;
                var explicitIfaces = new List<string>();
                if (cls.BaseTypes.Count > 0)
                {
                    var first = cls.BaseTypes[0];
                    var firstSym = ResolveAnyTypeSymbol(ns, first.Name, first.Span, requireKnown: true);
                    if (firstSym is not null)
                    {
                        if (!firstSym.IsInterface)
                        {
                            baseClass = firstSym.FullName;
                        }
                        else
                        {
                            explicitIfaces.Add(firstSym.FullName);
                        }
                    }

                    for (var i = 1; i < cls.BaseTypes.Count; i++)
                    {
                        var bt = cls.BaseTypes[i];
                        var sym = ResolveAnyTypeSymbol(ns, bt.Name, bt.Span, requireKnown: true);
                        if (sym is null) continue;
                        if (!sym.IsInterface)
                        {
                            Error(bt.Span, $"类继承列表中仅允许 0..1 个基类且必须在首位: {bt.Name}");
                            continue;
                        }
                        explicitIfaces.Add(sym.FullName);
                    }
                }

                var classMethods = new HashSet<MethodSigKey>();
                foreach (var m in cls.Members)
                {
                    if (m is MethodDecl md)
                    {
                        if (EffectiveAccess(md.Access) == Accessibility.Public && !md.IsStatic)
                        {
                            var assocBinds = _classAssocTypeBindings.TryGetValue(NodeKey.FromSpan(cls.Span), out var ab)
                                ? ab
                                : new Dictionary<string, TypeNode>(StringComparer.Ordinal);

                            string KeyOf(TypeNode t)
                            {
                                if (t is AssocTypeNode a2 && a2.BaseType is SimpleTypeNode s2 && string.Equals(s2.Name, cls.Name, StringComparison.Ordinal))
                                {
                                    if (assocBinds.TryGetValue(a2.Member, out var bound))
                                    {
                                        return TypeKey(ns, bound);
                                    }
                                    return "any";
                                }
                                return TypeKey(ns, t);
                            }

                            var pt = md.Parameters.Select(p => KeyOf(p.Type)).ToArray();
                            var rt = md.ReturnType is null ? "void" : KeyOf(md.ReturnType);
                            classMethods.Add(new MethodSigKey(md.Name, MakeParamSig(pt), rt));
                        }
                    }
                }

                foreach (var iface in explicitIfaces.Distinct(StringComparer.Ordinal))
                {
                    if (!interfaceMethods.TryGetValue(iface, out var req))
                    {
                        continue;
                    }

                    var assocBinds = _classAssocTypeBindings.TryGetValue(NodeKey.FromSpan(cls.Span), out var ab)
                        ? ab
                        : new Dictionary<string, TypeNode>(StringComparer.Ordinal);

                    var resolvedReq = new HashSet<MethodSigKey>();
                    foreach (var r in req)
                    {
                        var ps = string.IsNullOrWhiteSpace(r.ParamSig) ? Array.Empty<string>() : r.ParamSig.Split(',');
                        for (var i = 0; i < ps.Length; i++)
                        {
                            if (ps[i].StartsWith("__Assoc_", StringComparison.Ordinal))
                            {
                                var name = ps[i]["__Assoc_".Length..];
                                if (assocBinds.TryGetValue(name, out var bt))
                                {
                                    ps[i] = TypeKey(ns, bt);
                                }
                            }
                        }

                        var rt = r.ReturnType;
                        if (rt.StartsWith("__Assoc_", StringComparison.Ordinal))
                        {
                            var name = rt["__Assoc_".Length..];
                            if (assocBinds.TryGetValue(name, out var bt))
                            {
                                rt = TypeKey(ns, bt);
                            }
                        }

                        resolvedReq.Add(new MethodSigKey(r.Name, MakeParamSig(ps), rt));
                    }

                    var missing = resolvedReq.Where(r => !classMethods.Contains(r)).ToList();
                    if (missing.Count > 0)
                    {
                        Error(cls.Span, $"类 {cls.Name} 未实现接口 {iface}: 缺少方法 {string.Join(", ", missing.Select(m => m.ToString()))}");
                    }
                }

                var implicitIfaces = new List<string>();
                foreach (var iface in referencedClosure)
                {
                    if (explicitIfaces.Contains(iface)) continue;
                    if (interfaceMethods.TryGetValue(iface, out var req2))
                    {
                        if (req2.All(r => classMethods.Contains(r)))
                        {
                            implicitIfaces.Add(iface);
                        }
                    }
                }

                var allIfaces = explicitIfaces.Distinct(StringComparer.Ordinal).Concat(implicitIfaces.Distinct(StringComparer.Ordinal)).ToList();
                _classBaseLists[NodeKey.FromSpan(cls.Span)] = new BoundClassBaseList(baseClass, allIfaces);
            }
        }
    }

    private void BindBodiesForThisRaiseAndFuncRefs()
    {
        foreach (var unit in _units)
        {
            var ns = UnitNamespace(unit);
            foreach (var f in unit.Functions)
            {
                var env = TypeEnv.Empty.AddMany(f.TypeParams.Select(tp => tp.Name));
                foreach (var p in f.Parameters)
                {
                    ValidateTypeExists(ns, env, p.Type);
                }
                if (f.ReturnType is not null)
                {
                    ValidateTypeExists(ns, env, f.ReturnType);
                }
                BindBlock(unit, ns, env, currentClass: null, isStaticContext: true, inExcept: false, locals: new HashSet<string>(f.Parameters.Select(p => p.Name), StringComparer.Ordinal), f.Body);
            }

            foreach (var cls in unit.Classes)
            {
                var env = TypeEnv.Empty.AddMany(cls.TypeParams.Select(tp => tp.Name));
                foreach (var m in cls.Members)
                {
                    switch (m)
                    {
                        case FieldDecl fd:
                            ValidateTypeExists(ns, env, fd.Type);
                            break;
                        case MethodDecl md:
                            var env2 = env.AddMany(md.TypeParams.Select(tp => tp.Name));
                            foreach (var p in md.Parameters)
                            {
                                ValidateTypeExists(ns, env2, p.Type);
                            }
                            if (md.ReturnType is not null)
                            {
                                ValidateTypeExists(ns, env2, md.ReturnType);
                            }
                            BindBlock(unit, ns, env2, cls, isStaticContext: md.IsStatic, inExcept: false, new HashSet<string>(md.Parameters.Select(p => p.Name), StringComparer.Ordinal), md.Body);
                            break;
                        case CtorDecl cd:
                            foreach (var p in cd.Parameters)
                            {
                                ValidateTypeExists(ns, env, p.Type);
                            }
                            BindBlock(unit, ns, env, cls, isStaticContext: false, inExcept: false, new HashSet<string>(cd.Parameters.Select(p => p.Name), StringComparer.Ordinal), cd.Body);
                            break;
                        case AssociatedTypeBindDecl atb:
                            ValidateTypeExists(ns, env, atb.ValueType);
                            break;
                    }
                }
            }
        }
    }

    private void ValidateAllTypeAliases(string ns)
    {
        foreach (var unit in _units)
        {
            foreach (var ta in unit.TypeAliases)
            {
                var env = TypeEnv.Empty.AddMany(ta.TypeParams.Select(tp => tp.Name));
                ValidateTypeExists(ns, env, ta.Target);
            }
        }
    }

    private void BindBlock(CompilationUnit unit, string ns, TypeEnv env, ClassDecl? currentClass, bool isStaticContext, bool inExcept, HashSet<string> locals, BlockStmt block)
    {
        foreach (var st in block.Statements)
        {
            BindStmt(unit, ns, env, currentClass, isStaticContext, inExcept, locals, st);
        }
    }

    private void BindStmt(CompilationUnit unit, string ns, TypeEnv env, ClassDecl? currentClass, bool isStaticContext, bool inExcept, HashSet<string> locals, Stmt st)
    {
        switch (st)
        {
            case BlockStmt b:
                BindBlock(unit, ns, env, currentClass, isStaticContext, inExcept, new HashSet<string>(locals, StringComparer.Ordinal), b);
                break;
            case VarDeclStmt v:
                if (v.ExplicitType is not null) ValidateTypeExists(ns, env, v.ExplicitType);
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, v.Init);
                if (v.Kind != VarDeclKind.ExplicitType && v.Init is IdentifierExpr id)
                {
                    if (!locals.Contains(id.Name) && TryResolveTopLevelFunction(unit, id.Name, out var fun))
                    {
                        var ft = FunDeclToFunctionType(fun);
                        _inferredVarFuncTypes[NodeKey.FromSpan(v.Span)] = ft;
                    }
                }
                locals.Add(v.Name);
                break;
            case ExprStmt e:
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, e.Expression);
                break;
            case IfStmt i:
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, i.Condition);
                BindBlock(unit, ns, env, currentClass, isStaticContext, inExcept, new HashSet<string>(locals, StringComparer.Ordinal), i.Then);
                if (i.Else is not null)
                {
                    BindBlock(unit, ns, env, currentClass, isStaticContext, inExcept, new HashSet<string>(locals, StringComparer.Ordinal), i.Else);
                }
                break;
            case WhileStmt w:
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, w.Condition);
                BindBlock(unit, ns, env, currentClass, isStaticContext, inExcept, new HashSet<string>(locals, StringComparer.Ordinal), w.Body);
                break;
            case ReturnStmt r:
                if (r.Expression is not null) BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, r.Expression);
                break;
            case TryStmt t:
                BindBlock(unit, ns, env, currentClass, isStaticContext, inExcept: false, new HashSet<string>(locals, StringComparer.Ordinal), t.TryBlock);
                if (t.Except is not null)
                {
                    var locals2 = new HashSet<string>(locals, StringComparer.Ordinal);
                    if (!string.IsNullOrWhiteSpace(t.Except.Name)) locals2.Add(t.Except.Name!);
                    if (t.Except.Type is not null) ValidateTypeExists(ns, env, t.Except.Type);
                    BindBlock(unit, ns, env, currentClass, isStaticContext, inExcept: true, locals2, t.Except.Block);
                }
                if (t.Finally is not null)
                {
                    BindBlock(unit, ns, env, currentClass, isStaticContext, inExcept: false, new HashSet<string>(locals, StringComparer.Ordinal), t.Finally);
                }
                break;
            case RaiseStmt rs:
                if (rs.Expression is null)
                {
                    if (!inExcept)
                    {
                        Error(rs.Span, "raise 不带表达式仅允许在 except 块内（rethrow）");
                    }
                }
                else
                {
                    BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, rs.Expression);
                }
                break;
            case DeferStmt d:
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, d.Expression);
                break;
        }
    }

    private void BindExpr(CompilationUnit unit, string ns, TypeEnv env, ClassDecl? currentClass, bool isStaticContext, bool inExcept, HashSet<string> locals, Expr e)
    {
        switch (e)
        {
            case ThisExpr t:
                if (currentClass is null)
                {
                    Error(t.Span, "this 仅允许在实例方法/构造函数内");
                }
                else if (isStaticContext)
                {
                    Error(t.Span, "this 不允许在 static 方法内");
                }
                break;
            case AssignExpr a:
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, a.Target);
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, a.Value);
                if (a.Target is IdentifierExpr idt)
                {
                    if (!locals.Contains(idt.Name) && _topLevelGlobals.TryGetValue((unit.SourcePath, idt.Name), out var g) && g.IsConst)
                    {
                        Error(idt.Span, $"const 全局变量不允许再次赋值: {idt.Name}");
                    }
                }
                break;
            case UnaryExpr u:
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, u.Operand);
                break;
            case BinaryExpr b:
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, b.Left);
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, b.Right);
                break;
            case CallExpr c:
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, c.Callee);
                foreach (var a0 in c.Args) BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, a0);
                break;
            case GenericCallExpr gc:
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, gc.Callee);
                foreach (var ta in gc.TypeArgs) ValidateTypeExists(ns, env, ta);
                foreach (var a0 in gc.Args) BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, a0);
                break;
            case IndexExpr i:
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, i.Target);
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, i.Index);
                break;
            case MemberExpr m:
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, m.Target);
                break;
            case CastExpr cex:
                ValidateTypeExists(ns, env, cex.Type);
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, cex.Target);
                break;
            case ArrayLiteralExpr arr:
                foreach (var el in arr.Elements) BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, el);
                break;
            case ParenWrapExpr p:
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, p.Inner);
                break;
            case NewExpr ne:
                ValidateTypeExists(ns, env, ne.Type);
                foreach (var a0 in ne.Args) BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, a0);
                break;
            case NewArrayExpr nae:
                ValidateTypeExists(ns, env, nae.ElementType);
                BindExpr(unit, ns, env, currentClass, isStaticContext, inExcept, locals, nae.Size);
                break;
            case LambdaExpr le:
                foreach (var p0 in le.Parameters) ValidateTypeExists(ns, env, p0.Type);
                if (le.ReturnType is not null) ValidateTypeExists(ns, env, le.ReturnType);
                var locals2 = new HashSet<string>(locals, StringComparer.Ordinal);
                foreach (var p0 in le.Parameters) locals2.Add(p0.Name);
                BindBlock(unit, ns, env, currentClass, isStaticContext: false, inExcept, locals2, le.Body);
                break;
            case IdentifierExpr id:
                break;
        }
    }

    private void ValidateTypeExists(string ns, TypeNode node)
    {
        ValidateTypeExists(ns, TypeEnv.Empty, node);
    }

    private void ValidateTypeExists(string ns, TypeEnv env, TypeNode node)
    {
        switch (node)
        {
            case SimpleTypeNode s:
                if (IsBuiltinType(s.Name)) return;
                if (env.IsTypeParam(s.Name)) return;
                if (_resolvedAssocTypes.TryGetValue(NodeKey.FromSpan(s.Span), out _)) return;
                if (TryGetUniqueTypeAlias(s.Name, out var a0) && a0.TypeParams.Count == 0) return;
                if (HasAnyZeroTypeParamTypeAlias(s.Name)) return;
                if (ResolveAnyTypeSymbol(ns, s.Name, s.Span, requireKnown: false) is null)
                {
                    var qn = !string.IsNullOrWhiteSpace(ns)
                        ? new QualifiedName(ns.Split('.').Concat(new[] { s.Name }).ToArray())
                        : new QualifiedName(new[] { s.Name });
                    AddMissingModule(qn);
                    if (_strictUnknownTypes)
                    {
                        Error(s.Span, $"未知类型: {s.Name}");
                    }
                }
                break;
            case QualifiedTypeNode q:
                if (TryGetAssocProjectionFromQualified(ns, env, q, out var baseType, out var member))
                {
                    ValidateTypeExists(ns, env, baseType);
                    var assoc = new AssocTypeNode(baseType, member, q.Span);
                    var resolved = TryResolveAssocType(ns, env, assoc);
                    _resolvedAssocTypes[NodeKey.FromSpan(q.Span)] = resolved ?? new SimpleTypeNode("any", AssocInternalSpan(q.Span));
                    break;
                }
                if (q.Segments.Count == 0) break;
                var last = q.Segments.Last();
                if (IsBuiltinType(last)) return;
                if (ResolveAnyTypeSymbol(ns, last, q.Span, requireKnown: false) is null)
                {
                    var qn2 = !string.IsNullOrWhiteSpace(ns)
                        ? new QualifiedName(ns.Split('.').Concat(new[] { last }).ToArray())
                        : new QualifiedName(new[] { last });
                    AddMissingModule(qn2);
                    if (_strictUnknownTypes)
                    {
                        Error(q.Span, $"未知类型: {string.Join(".", q.Segments)}");
                    }
                }
                break;
            case ArrayTypeNode a:
                ValidateTypeExists(ns, env, a.ElementType);
                break;
            case FunctionTypeNode f:
                foreach (var p in f.ParameterTypes) ValidateTypeExists(ns, env, p);
                ValidateTypeExists(ns, env, f.ReturnType);
                break;
            case GenericTypeNode g:
                if (g.BaseType is SimpleTypeNode gs && TryGetUniqueTypeAlias(gs.Name, out var a1))
                {
                    foreach (var ta in g.TypeArguments) ValidateTypeExists(ns, env, ta);
                    break;
                }
                ValidateTypeExists(ns, env, g.BaseType);
                foreach (var ta in g.TypeArguments) ValidateTypeExists(ns, env, ta);
                break;
            case AssocTypeNode a2:
                ValidateTypeExists(ns, env, a2.BaseType);
                break;
        }
    }

    private bool TryGetAssocProjectionFromQualified(string ns, TypeEnv env, QualifiedTypeNode q, out TypeNode baseType, out string member)
    {
        baseType = default!;
        member = string.Empty;
        if (q.Segments.Count < 2) return false;

        var baseSegs = q.Segments.Take(q.Segments.Count - 1).ToList();
        member = q.Segments.Last();
        baseType = baseSegs.Count == 1
            ? new SimpleTypeNode(baseSegs[0], q.Span)
            : new QualifiedTypeNode(baseSegs, q.Span);

        if (baseType is SimpleTypeNode s && env.IsTypeParam(s.Name))
        {
            return true;
        }

        var baseLast = baseSegs.Last();
        var sym = ResolveAnyTypeSymbol(currentNamespace: ns, name: baseLast, span: q.Span, requireKnown: false);
        return sym is not null;
    }

    private bool TryResolveTopLevelFunction(CompilationUnit unit, string name, out FunDecl fun)
    {
        return _topLevelFunctions.TryGetValue((unit.SourcePath, name), out fun!);
    }

    private static FunctionTypeNode FunDeclToFunctionType(FunDecl fun)
    {
        var ps = fun.Parameters.Select(p => p.Type).ToList();
        var rt = fun.ReturnType ?? new SimpleTypeNode("void", fun.Span);
        return new FunctionTypeNode(ps, rt, fun.Span);
    }

    private TypeSymbol? ResolveTypeSymbol(string ns, string name, SourceSpan span, bool requireKnown)
    {
        if (IsBuiltinType(name))
        {
            if (requireKnown)
            {
                Error(span, $"此处需要 class/interface 类型名，但得到内置类型: {name}");
            }
            return null;
        }

        if (_typesByNamespaceAndName.TryGetValue((ns, name), out var sym))
        {
            return sym;
        }

        if (requireKnown)
        {
            var qn = !string.IsNullOrWhiteSpace(ns)
                ? new QualifiedName(ns.Split('.').Concat(new[] { name }).ToArray())
                : new QualifiedName(new[] { name });
            AddMissingModule(qn);
            if (_strictUnknownTypes)
            {
                Error(span, $"未知类型: {name}");
            }
        }
        return null;
    }

    private static Accessibility EffectiveAccess(Accessibility access)
    {
        return access == Accessibility.Default ? Accessibility.Private : access;
    }

    private string TypeKey(string ns, TypeNode node)
    {
        return node switch
        {
            SimpleTypeNode s => IsBuiltinType(s.Name) ? s.Name : ResolveAnyTypeSymbol(ns, s.Name, s.Span, requireKnown: true)?.FullName ?? s.Name,
            AssocTypeNode a2 => _resolvedAssocTypes.TryGetValue(NodeKey.FromSpan(a2.Span), out var r) ? TypeKey(ns, r) : "any",
            ArrayTypeNode a => TypeKey(ns, a.ElementType) + "[]",
            FunctionTypeNode f => $"({string.Join(",", f.ParameterTypes.Select(p => TypeKey(ns, p))) })->{TypeKey(ns, f.ReturnType)}",
            PtrTypeNode p => "ptr<" + TypeKey(ns, p.ElementType) + ">",
            _ => "any",
        };
    }

    private void Error(SourceSpan span, string message)
    {
        _hasErrors = true;
        _diagnostics.WriteLine(new Diagnostic(DiagnosticSeverity.Error, span, message).ToString());
    }

    private static bool IsBuiltinType(string name)
    {
        return name is "i8" or "i16" or "i32" or "i64" or "u8" or "u16" or "u32" or "u64" or "f32" or "f64" or
            "int" or "uint" or "float" or "double" or "bool" or "char" or "string" or "any" or "void";
    }
}
