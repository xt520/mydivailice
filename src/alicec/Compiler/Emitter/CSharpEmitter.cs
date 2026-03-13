using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Alice.Compiler;

internal sealed class CSharpEmitter
{
    private readonly string _sourcePath;
    private BindingResult? _binding;
    private CompilationUnit? _currentUnit;

    private static string UnitNamespace(CompilationUnit? unit)
    {
        if (unit?.Namespace is null) return string.Empty;
        return string.Join(".", unit.Namespace.Name.Segments);
    }

    internal CSharpEmitter(string sourcePath)
    {
        _sourcePath = string.IsNullOrWhiteSpace(sourcePath) ? "<source>" : sourcePath;
        _binding = null;
    }

    internal string EmitProgram(ProgramNode program)
    {
        _binding = null;
        return EmitProgramCore(program);
    }

    internal string EmitProgram(BindingResult binding)
    {
        _binding = binding;
        return EmitProgramCore(binding.Program);
    }

    private string EmitProgramCore(ProgramNode program)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#nullable enable");
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine("using System.Threading.Tasks;");
        sb.AppendLine();

        sb.AppendLine("namespace Alice.Generated");
        sb.AppendLine("{");
        sb.AppendLine("#line hidden");
        EmitStd(sb);
        sb.AppendLine("#line default");
        sb.AppendLine("}");
        sb.AppendLine();

        foreach (var unit in program.Units)
        {
            if (unit.IsInterfaceFile) continue;
            if (unit.IsStdLib) continue;
            EmitUnit(sb, unit);
            sb.AppendLine();
        }

        var entryUnit = program.Units.First(u => !u.IsInterfaceFile);
        var entryNs = entryUnit.Namespace?.Name.Segments ?? Array.Empty<string>();
        var entryBase = entryUnit.FileBaseName;
        var entryClass = "__AliceModule_" + entryBase;
        var entryFull = string.Join(".", new[] { "global::Alice.Generated" }.Concat(entryNs).Concat(new[] { entryClass }));
        var mainFun = entryUnit.Functions.FirstOrDefault(f => f.Name == "main");

        sb.AppendLine("public static class __AliceEntry");
        sb.AppendLine("{");
        sb.AppendLine("    public static int Main(string[] args)");
        sb.AppendLine("    {");
        if (mainFun is not null && mainFun.IsAsync)
        {
            if (mainFun.Parameters.Count == 1)
            {
                sb.AppendLine($"        {entryFull}.main(args).GetAwaiter().GetResult();");
            }
            else
            {
                sb.AppendLine($"        {entryFull}.main().GetAwaiter().GetResult();");
            }
        }
        else
        {
            if (mainFun is not null && mainFun.Parameters.Count == 1)
            {
                sb.AppendLine($"        {entryFull}.main(args);");
            }
            else
            {
                sb.AppendLine($"        {entryFull}.main();");
            }
        }
        sb.AppendLine("        return 0;");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private void EmitUnit(StringBuilder sb, CompilationUnit unit)
    {
        _currentUnit = unit;
        var nsSegs = unit.Namespace?.Name.Segments ?? Array.Empty<string>();
        sb.Append("namespace Alice.Generated");
        if (nsSegs.Count > 0)
        {
            sb.Append('.');
            sb.Append(string.Join(".", nsSegs));
        }
        sb.AppendLine();
        sb.AppendLine("{");

        foreach (var imp in unit.Imports)
        {
            var moduleName = imp.ModuleName.ToString();
            var resolved = _binding?.Program.Units.FirstOrDefault(u => u.ModuleName == moduleName);
            var resolvedNs = resolved?.Namespace?.Name.Segments ?? imp.ModuleName.Segments;
            var fileBase = resolved?.FileBaseName ?? imp.ModuleName.Segments.Last();
            var targetRoot = resolved is not null && resolved.IsStdLib ? "global::Alice.Std" : "global::Alice.Generated";
            var target = string.Join(".", new[] { targetRoot }.Concat(resolvedNs).Concat(new[] { "__AliceModule_" + fileBase }));
            sb.AppendLine($"    using {imp.Alias} = {target};");
        }
        if (unit.Imports.Count > 0) sb.AppendLine();

        EmitModuleClass(sb, unit);
        sb.AppendLine();

        foreach (var itf in unit.Interfaces)
        {
            EmitInterfaceType(sb, unit, itf);
            sb.AppendLine();
        }

        foreach (var en in unit.Enums)
        {
            EmitEnumType(sb, en);
            sb.AppendLine();
        }

        foreach (var cls in unit.Classes)
        {
            EmitClassType(sb, unit, cls);
            sb.AppendLine();
        }

        foreach (var st in unit.Structs)
        {
            EmitStructType(sb, unit, st);
            sb.AppendLine();
        }

        sb.AppendLine("}");

        _currentUnit = null;
    }

    private void EmitEnumType(StringBuilder sb, EnumDecl en)
    {
        sb.Append("    internal enum ");
        sb.Append(en.Name);
        sb.AppendLine();
        sb.AppendLine("    {");
        foreach (var m in en.Members)
        {
            sb.Append("        ");
            sb.Append(m.Name);
            if (m.Value is not null)
            {
                sb.Append(" = ");
                sb.Append(m.Value.Value);
            }
            sb.AppendLine(",");
        }
        sb.AppendLine("    }");
    }

    private void EmitStructType(StringBuilder sb, CompilationUnit unit, StructDecl st)
    {
        sb.Append("    internal struct ");
        sb.Append(st.Name);
        if (st.TypeParams.Count > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(", ", st.TypeParams.Select(tp => tp.Name)));
            sb.Append('>');
        }
        sb.AppendLine();
        EmitWhereClauses(sb, st.TypeParams, extraWhere: null);
        sb.AppendLine("    {");

        foreach (var mem in st.Members)
        {
            if (mem is FieldDecl fd)
            {
                EmitLineDirective(sb, fd.Span);
                sb.Append("        ");
                sb.Append(EmitType(fd.Type));
                sb.Append(' ');
                sb.Append(fd.Name);
                sb.AppendLine(";");
                sb.AppendLine("#line default");
                continue;
            }

            if (mem is CtorDecl cd)
            {
                EmitLineDirective(sb, cd.Span);
                sb.Append("        ");
                sb.Append("public ");
                sb.Append(st.Name);
                sb.Append('(');
                for (var i = 0; i < cd.Parameters.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var p = cd.Parameters[i];
                    sb.Append(EmitType(p.Type));
                    sb.Append(' ');
                    sb.Append(p.Name);
                }
                sb.AppendLine(")");
                EmitBlock(sb, cd.Body, indent: 2);
                sb.AppendLine("#line default");
                sb.AppendLine();
                continue;
            }

            if (mem is MethodDecl md)
            {
                EmitLineDirective(sb, md.Span);
                sb.Append("        ");
                sb.Append("public ");
                if (md.IsStatic) sb.Append("static ");
                sb.Append(EmitType(md.ReturnType ?? new SimpleTypeNode("void", md.Span)));
                sb.Append(' ');
                sb.Append(md.Name);
                if (md.TypeParams.Count > 0)
                {
                    sb.Append('<');
                    sb.Append(string.Join(", ", md.TypeParams.Select(tp => tp.Name)));
                    sb.Append('>');
                }
                sb.Append('(');
                for (var i = 0; i < md.Parameters.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var p = md.Parameters[i];
                    sb.Append(EmitType(p.Type));
                    sb.Append(' ');
                    sb.Append(p.Name);
                }
                sb.AppendLine(")");
                EmitWhereClauses(sb, md.TypeParams, extraWhere: null);
                EmitBlock(sb, md.Body, indent: 2);
                sb.AppendLine("#line default");
                sb.AppendLine();
                continue;
            }
        }
        sb.AppendLine("    }");
    }

    private bool TryResolveTypeAlias(string name, out TypeAliasDecl alias)
    {
        if (_currentUnit is not null)
        {
            var local = _currentUnit.TypeAliases.Where(ta => string.Equals(ta.Name, name, StringComparison.Ordinal)).ToList();
            if (local.Count == 1)
            {
                alias = local[0];
                return true;
            }
            if (local.Count > 1)
            {
                alias = local[0];
                return false;
            }
        }

        var program = _binding?.Program;
        if (program is null)
        {
            alias = default!;
            return false;
        }

        var matches = program.Units.SelectMany(u => u.TypeAliases).Where(ta => string.Equals(ta.Name, name, StringComparison.Ordinal)).ToList();
        if (matches.Count == 1)
        {
            alias = matches[0];
            return true;
        }

        alias = matches.FirstOrDefault()!;
        return false;
    }

    private string EmitClassBaseInterfaceType(ClassDecl cls, string ifaceFullName)
    {
        if (_binding is null)
        {
            return "global::Alice.Generated." + ifaceFullName;
        }

        var itf = FindInterfaceByFullName(ifaceFullName);
        if (itf is null)
        {
            return "global::Alice.Generated." + ifaceFullName;
        }

        var ik = NodeKey.FromSpan(itf.Span);
        if (!_binding.InterfaceAssocTypeParams.TryGetValue(ik, out var assocNames) || assocNames.Count == 0)
        {
            return "global::Alice.Generated." + ifaceFullName;
        }

        if (!_binding.ClassAssocTypeBindings.TryGetValue(NodeKey.FromSpan(cls.Span), out var binds))
        {
            return "global::Alice.Generated." + ifaceFullName;
        }

        var args = new List<string>();
        foreach (var an in assocNames)
        {
            if (binds.TryGetValue(an, out var t))
            {
                args.Add(EmitType(t));
            }
            else
            {
                args.Add("object");
            }
        }

        return $"global::Alice.Generated.{ifaceFullName}<{string.Join(", ", args)}>";
    }

    private InterfaceDecl? FindInterfaceByFullName(string ifaceFullName)
    {
        var program = _binding?.Program;
        if (program is null) return null;

        foreach (var u in program.Units)
        {
            var ns = u.Namespace?.Name.Segments ?? Array.Empty<string>();
            var unitNs = string.Join(".", ns);
            foreach (var itf in u.Interfaces)
            {
                var full = string.IsNullOrWhiteSpace(unitNs) ? itf.Name : unitNs + "." + itf.Name;
                if (string.Equals(full, ifaceFullName, StringComparison.Ordinal))
                {
                    return itf;
                }
            }
        }

        return null;
    }

    private void EmitModuleClass(StringBuilder sb, CompilationUnit unit)
    {
        var moduleClassName = "__AliceModule_" + unit.FileBaseName;
        sb.AppendLine($"    internal static class {moduleClassName}");
        sb.AppendLine("    {");

        foreach (var g in unit.Globals)
        {
            EmitLineDirective(sb, g.Span);
            sb.Append("        internal static ");
            sb.Append(EmitType(g.Type));
            sb.Append(' ');
            sb.Append(g.Name);
            sb.Append(" = ");
            sb.Append(EmitExpr(g.Init));
            sb.AppendLine(";");
            sb.AppendLine("#line default");
        }
        if (unit.Globals.Count > 0) sb.AppendLine();

        foreach (var f in unit.Functions)
        {
            EmitFun(sb, f);
            sb.AppendLine();
        }

        sb.AppendLine("    }");
    }

    private void EmitInterfaceType(StringBuilder sb, CompilationUnit unit, InterfaceDecl itf)
    {
        EmitLineDirective(sb, itf.Span);
        sb.Append("    ");
        sb.Append(itf.IsPublic ? "public" : "internal");
        sb.Append(" interface ");
        sb.Append(itf.Name);
        var itfExtraTps = _binding is not null && _binding.ScopeExtraTypeParams.TryGetValue(NodeKey.FromSpan(itf.Span), out var itfEx) ? itfEx : Array.Empty<string>();
        if (itf.TypeParams.Count > 0 || itfExtraTps.Count > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(", ", itf.TypeParams.Select(tp => tp.Name).Concat(itfExtraTps)));
            sb.Append('>');
        }

        if (_binding is not null && _binding.InterfaceBaseLists.TryGetValue(NodeKey.FromSpan(itf.Span), out var bases) && bases.InterfaceTypeNames.Count > 0)
        {
            sb.Append(" : ");
            sb.Append(string.Join(", ", bases.InterfaceTypeNames.Select(i => "global::Alice.Generated." + i)));
        }
        else if (itf.BaseTypes.Count > 0)
        {
            sb.Append(" : ");
            sb.Append(string.Join(", ", itf.BaseTypes.Select(b => b.Name)));
        }

        sb.AppendLine();
        EmitWhereClauses(sb, itf.TypeParams, extraWhere: null);
        sb.AppendLine("    {");
        foreach (var e in itf.Entries)
        {
            if (e is InterfaceMethodSig ms)
            {
                EmitLineDirective(sb, ms.Span);
                sb.Append("        ");
                sb.Append(EmitType(ms.ReturnType ?? new SimpleTypeNode("void", ms.Span)));
                sb.Append(' ');
                sb.Append(ms.Name);
                sb.Append('(');
                for (var i = 0; i < ms.Parameters.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    var p = ms.Parameters[i];
                    sb.Append(EmitType(p.Type));
                    sb.Append(' ');
                    sb.Append(p.Name);
                }
                sb.AppendLine(");");
                sb.AppendLine("#line default");
            }
        }
        sb.AppendLine("    }");
        sb.AppendLine("#line default");
    }

    private void EmitWhereClauses(StringBuilder sb, IReadOnlyList<TypeParamDecl> typeParams, IReadOnlyDictionary<string, IReadOnlyList<TypeNode>>? extraWhere)
    {
        foreach (var tp in typeParams)
        {
            if (tp.Constraints.Count == 0) continue;
            sb.AppendLine();
            sb.Append("        where ");
            sb.Append(tp.Name);
            sb.Append(" : ");
            sb.Append(string.Join(", ", tp.Constraints.Select(EmitType)));
        }

        if (extraWhere is not null)
        {
            foreach (var kv in extraWhere)
            {
                if (kv.Value.Count == 0) continue;
                if (kv.Value.Any(t => t is SimpleTypeNode s && s.Name == "any")) continue;
                sb.AppendLine();
                sb.Append("        where ");
                sb.Append(kv.Key);
                sb.Append(" : ");
                sb.Append(string.Join(", ", kv.Value.Select(EmitType)));
            }
        }
    }

    private void EmitClassType(StringBuilder sb, CompilationUnit unit, ClassDecl cls)
    {
        EmitLineDirective(sb, cls.Span);
        sb.Append("    ");
        sb.Append(cls.IsPublic ? "public" : "internal");
        sb.Append(" class ");
        sb.Append(cls.Name);
        var clsExtraTps = _binding is not null && _binding.ScopeExtraTypeParams.TryGetValue(NodeKey.FromSpan(cls.Span), out var clsEx) ? clsEx : Array.Empty<string>();
        if (cls.TypeParams.Count > 0 || clsExtraTps.Count > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(", ", cls.TypeParams.Select(tp => tp.Name).Concat(clsExtraTps)));
            sb.Append('>');
        }

        if (_binding is not null && _binding.ClassBaseLists.TryGetValue(NodeKey.FromSpan(cls.Span), out var bases))
        {
            var baseParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(bases.BaseClassTypeName))
            {
                baseParts.Add("global::Alice.Generated." + bases.BaseClassTypeName);
            }
            foreach (var i0 in bases.InterfaceTypeNames)
            {
                baseParts.Add(EmitClassBaseInterfaceType(cls, i0));
            }
            if (baseParts.Count > 0)
            {
                sb.Append(" : ");
                sb.Append(string.Join(", ", baseParts));
            }
        }
        else if (cls.BaseTypes.Count > 0)
        {
            sb.Append(" : ");
            sb.Append(string.Join(", ", cls.BaseTypes.Select(b => b.Name)));
        }

        sb.AppendLine();
        EmitWhereClauses(sb, cls.TypeParams, extraWhere: null);
        sb.AppendLine("    {");
        foreach (var mem in cls.Members)
        {
            switch (mem)
            {
                case FieldDecl fd:
                    EmitLineDirective(sb, fd.Span);
                    sb.Append("        ");
                    EmitAccess(sb, fd.Access);
                    if (fd.IsStatic) sb.Append("static ");
                    if (fd.IsConst) sb.Append("static readonly ");
                    sb.Append(EmitType(fd.Type));
                    sb.Append(' ');
                    sb.Append(fd.Name);
                    if (fd.Init is not null)
                    {
                        sb.Append(" = ");
                        sb.Append(EmitExpr(fd.Init));
                    }
                    sb.AppendLine(";");
                    sb.AppendLine("#line default");
                    break;
                case CtorDecl cd:
                    EmitLineDirective(sb, cd.Span);
                    sb.Append("        ");
                    EmitAccess(sb, cd.Access);
                    if (cd.Attributes.Contains("unsafe", StringComparer.Ordinal)) sb.Append("unsafe ");
                    sb.Append(cls.Name);
                    sb.Append('(');
                    for (var i = 0; i < cd.Parameters.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        var p = cd.Parameters[i];
                        sb.Append(EmitType(p.Type));
                        sb.Append(' ');
                        sb.Append(p.Name);
                    }
                    sb.AppendLine(")");
                    EmitBlock(sb, cd.Body, indent: 2);
                    sb.AppendLine("#line default");
                    sb.AppendLine();
                    break;
                case MethodDecl md:
                    EmitLineDirective(sb, md.Span);
                    sb.Append("        ");
                    EmitAccess(sb, md.Access);
                    if (md.Attributes.Contains("unsafe", StringComparer.Ordinal)) sb.Append("unsafe ");
                    if (md.IsStatic) sb.Append("static ");
                    sb.Append(EmitType(md.ReturnType ?? new SimpleTypeNode("void", md.Span)));
                    sb.Append(' ');
                    sb.Append(md.Name);
                    var extraTps = _binding is not null && _binding.ScopeExtraTypeParams.TryGetValue(NodeKey.FromSpan(md.Span), out var ex) ? ex : Array.Empty<string>();
                    if (md.TypeParams.Count > 0 || extraTps.Count > 0)
                    {
                        sb.Append('<');
                        sb.Append(string.Join(", ", md.TypeParams.Select(tp => tp.Name).Concat(extraTps)));
                        sb.Append('>');
                    }
                    sb.Append('(');
                    for (var i = 0; i < md.Parameters.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        var p = md.Parameters[i];
                        sb.Append(EmitType(p.Type));
                        sb.Append(' ');
                        sb.Append(p.Name);
                    }
                    sb.AppendLine(")");
                    EmitWhereClauses(sb, md.TypeParams, extraWhere: _binding is not null && _binding.ScopeTypeParamWhereConstraints.TryGetValue(NodeKey.FromSpan(md.Span), out var mw) ? mw : null);
                    EmitBlock(sb, md.Body, indent: 2);
                    sb.AppendLine("#line default");
                    sb.AppendLine();
                    break;
            }
        }
        sb.AppendLine("    }");
        sb.AppendLine("#line default");
    }

    private static void EmitAccess(StringBuilder sb, Accessibility access)
    {
        var eff = access == Accessibility.Default ? Accessibility.Private : access;
        sb.Append(eff switch
        {
            Accessibility.Public => "public ",
            Accessibility.Protected => "protected ",
            Accessibility.Private => "private ",
            _ => "private ",
        });
    }

    private void EmitFun(StringBuilder sb, FunDecl f)
    {
        var baseReturnType = f.ReturnType is null ? "void" : EmitType(f.ReturnType);
        var returnType = baseReturnType;
        if (f.IsAsync)
        {
            returnType = baseReturnType == "void"
                ? "global::System.Threading.Tasks.Task"
                : $"global::System.Threading.Tasks.Task<{baseReturnType}>";
        }
        sb.Append("        internal static ");
        if (f.Attributes.Contains("unsafe", StringComparer.Ordinal))
        {
            sb.Append("unsafe ");
        }
        if (f.IsAsync)
        {
            sb.Append("async ");
        }
        sb.Append(returnType);
        sb.Append(' ');
        sb.Append(f.Name);
        var extraTps = _binding is not null && _binding.ScopeExtraTypeParams.TryGetValue(NodeKey.FromSpan(f.Span), out var ex) ? ex : Array.Empty<string>();
        if (f.TypeParams.Count > 0 || extraTps.Count > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(", ", f.TypeParams.Select(tp => tp.Name).Concat(extraTps)));
            sb.Append('>');
        }
        sb.Append('(');
        for (var i = 0; i < f.Parameters.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            var p = f.Parameters[i];
            sb.Append(EmitType(p.Type));
            sb.Append(' ');
            sb.Append(p.Name);
        }
        sb.AppendLine(")");
        EmitWhereClauses(sb, f.TypeParams, extraWhere: _binding is not null && _binding.ScopeTypeParamWhereConstraints.TryGetValue(NodeKey.FromSpan(f.Span), out var fw) ? fw : null);
        EmitBlock(sb, f.Body, indent: 2);
    }

    private void EmitBlock(StringBuilder sb, BlockStmt block, int indent)
    {
        Indent(sb, indent);

        if (block.Statements.OfType<DeferStmt>().Any())
        {
            sb.AppendLine("{");
            Indent(sb, indent + 1);
            sb.AppendLine("var __defer = new global::System.Collections.Generic.List<global::System.Action>();");
            Indent(sb, indent + 1);
            sb.AppendLine("try");
            Indent(sb, indent + 1);
            sb.AppendLine("{");
            foreach (var st in block.Statements.Where(s => s is not DeferStmt))
            {
                EmitStmt(sb, st, indent + 2);
            }
            Indent(sb, indent + 1);
            sb.AppendLine("}");
            Indent(sb, indent + 1);
            sb.AppendLine("finally");
            Indent(sb, indent + 1);
            sb.AppendLine("{");
            Indent(sb, indent + 2);
            sb.AppendLine("for (var __i = __defer.Count - 1; __i >= 0; __i--) __defer[__i]();");
            Indent(sb, indent + 1);
            sb.AppendLine("}");
            Indent(sb, indent);
            sb.AppendLine("}");
            return;
        }

        sb.AppendLine("{");
        foreach (var st in block.Statements.Where(s => s is not DeferStmt))
        {
            EmitStmt(sb, st, indent + 1);
        }
        Indent(sb, indent);
        sb.AppendLine("}");
    }

    private void EmitStmt(StringBuilder sb, Stmt st, int indent)
    {
        switch (st)
        {
            case BlockStmt b:
                EmitBlock(sb, b, indent);
                return;
            case VarDeclStmt v:
                EmitLineDirective(sb, v.Span);
                Indent(sb, indent);
                if (_binding is not null && _binding.InferredVarFunctionTypes.TryGetValue(NodeKey.FromSpan(v.Span), out var ft))
                {
                    sb.Append(EmitType(ft));
                    sb.Append(' ');
                }
                else if (v.ExplicitType is not null)
                {
                    sb.Append(EmitType(v.ExplicitType));
                    sb.Append(' ');
                }
                else
                {
                    sb.Append("var ");
                }
                sb.Append(v.Name);
                sb.Append(" = ");
                sb.Append(EmitExpr(v.Init));
                sb.AppendLine(";");
                sb.AppendLine("#line default");
                return;
            case ExprStmt e:
                EmitLineDirective(sb, e.Span);
                Indent(sb, indent);
                sb.Append(EmitExpr(e.Expression));
                sb.AppendLine(";");
                sb.AppendLine("#line default");
                return;
            case IfStmt i:
                EmitLineDirective(sb, i.Span);
                Indent(sb, indent);
                sb.Append("if (");
                sb.Append(EmitExpr(i.Condition));
                sb.AppendLine(")");
                EmitBlock(sb, i.Then, indent);
                if (i.Else is not null)
                {
                    Indent(sb, indent);
                    sb.AppendLine("else");
                    EmitBlock(sb, i.Else, indent);
                }
                sb.AppendLine("#line default");
                return;
            case WhileStmt w:
                EmitLineDirective(sb, w.Span);
                Indent(sb, indent);
                sb.Append("while (");
                sb.Append(EmitExpr(w.Condition));
                sb.AppendLine(")");
                EmitBlock(sb, w.Body, indent);
                sb.AppendLine("#line default");
                return;
            case ReturnStmt r:
                EmitLineDirective(sb, r.Span);
                Indent(sb, indent);
                sb.Append("return");
                if (r.Expression is not null)
                {
                    sb.Append(' ');
                    sb.Append(EmitExpr(r.Expression));
                }
                sb.AppendLine(";");
                sb.AppendLine("#line default");
                return;
            case BreakStmt:
                Indent(sb, indent);
                sb.AppendLine("break;");
                return;
            case ContinueStmt:
                Indent(sb, indent);
                sb.AppendLine("continue;");
                return;
            case TryStmt t:
                Indent(sb, indent);
                sb.AppendLine("try");
                EmitBlock(sb, t.TryBlock, indent);
                if (t.Except is not null)
                {
                    Indent(sb, indent);
                    if (t.Except.Name is not null)
                    {
                        sb.Append("catch (global::System.Exception ");
                        sb.Append(t.Except.Name);
                        sb.AppendLine(")");
                    }
                    else
                    {
                        sb.AppendLine("catch (global::System.Exception)");
                    }
                    EmitBlock(sb, t.Except.Block, indent);
                }
                if (t.Finally is not null)
                {
                    Indent(sb, indent);
                    sb.AppendLine("finally");
                    EmitBlock(sb, t.Finally, indent);
                }
                return;
            case RaiseStmt r2:
                Indent(sb, indent);
                if (r2.Expression is null)
                {
                    sb.AppendLine("throw;");
                }
                else
                {
                    var exExpr = EmitExpr(r2.Expression);
                    if (r2.Expression is LiteralExpr lit && lit.Value is StringLiteralValue)
                    {
                        sb.AppendLine($"throw new global::System.Exception({exExpr});");
                    }
                    else
                    {
                        sb.AppendLine($"throw new global::System.Exception(({exExpr})?.ToString() ?? \"null\");");
                    }
                }
                return;
            case DeferStmt d:
                Indent(sb, indent);
                sb.Append("global::Alice.Generated.__AliceStd.__Defer(__defer, ");
                sb.Append("() => ");
                sb.Append(EmitExpr(d.Expression));
                sb.AppendLine(");");
                return;
        }
    }

    private string EmitExpr(Expr e)
    {
        return e switch
        {
            IdentifierExpr id => id.Name,
            ThisExpr => "this",
            LiteralExpr lit => EmitLiteral(lit.Value),
            ParenWrapExpr p => "(" + EmitExpr(p.Inner) + ")",
            UnaryExpr u => u.Op + EmitExpr(u.Operand),
            BinaryExpr b => EmitBinary(b),
            AssignExpr a => EmitAssign(a),
            CastExpr c => $"({EmitType(c.Type)})({EmitExpr(c.Target)})",
            MemberExpr m => EmitMember(m),
            IndexExpr i => $"global::Alice.Generated.__AliceStd.__Index(({EmitExpr(i.Target)}), ({EmitExpr(i.Index)}))",
            SliceExpr s => EmitSliceExpr(s),
            AwaitExpr a => $"await ({EmitExpr(a.Target)})",
            GoExpr g => $"global::System.Threading.Tasks.Task.Run(() => {EmitExpr(g.Call)})",
            AddrOfExpr ao => $"&({EmitExpr(ao.Target)})",
            DerefExpr d => $"*({EmitExpr(d.Target)})",
            CallExpr c => EmitCall(c),
            GenericCallExpr gc => EmitGenericCall(gc),
            ArrayLiteralExpr a => $"new[] {{ {string.Join(", ", a.Elements.Select(EmitExpr))} }}",
            NewExpr ne => EmitNewExpr(ne),
            NewArrayExpr nae => $"new {EmitType(nae.ElementType)}[{EmitExpr(nae.Size)}]",
            LambdaExpr le => EmitLambda(le),
            _ => "null",
        };
    }

    private string EmitAssign(AssignExpr a)
    {
        if (a.Target is IndexExpr ix)
        {
            return $"global::Alice.Generated.__AliceStd.__SetIndex(({EmitExpr(ix.Target)}), ({EmitExpr(ix.Index)}), ({EmitExpr(a.Value)}))";
        }
        if (a.Target is DerefExpr d)
        {
            return $"*({EmitExpr(d.Target)}) = ({EmitExpr(a.Value)})";
        }
        return $"{EmitExpr(a.Target)} = {EmitExpr(a.Value)}";
    }

    private string EmitNewExpr(NewExpr ne)
    {
        var t = ne.Type;
        if (t is not GenericTypeNode && TryGetDefaultTypeArgsForClass(t, out var defaults))
        {
            t = new GenericTypeNode(t, defaults, t.Span);
        }
        return $"new {EmitType(t)}({string.Join(", ", ne.Args.Select(EmitExpr))})";
    }

    private string EmitCall(CallExpr c)
    {
        if (c.Callee is IdentifierExpr id && id.Name == "print")
        {
            return $"global::Alice.Generated.__AliceStd.Print({string.Join(", ", c.Args.Select(EmitExpr))})";
        }

        if (c.Callee is MemberExpr m)
        {
            var calleeText = EmitExpr(m.Target);
            if (m.Member == "then") return $"{calleeText}.ContinueWith(t => ({EmitExpr(c.Args[0])})(t.GetAwaiter().GetResult()))";
            if (m.Member == "catch") return $"global::Alice.Std.async.__AliceModule_index.Catch({calleeText}, (global::System.Exception e) => {{ {EmitExpr(c.Args[0])}(e); return global::System.Threading.Tasks.Task.CompletedTask; }})";
            if (m.Member == "finally") return $"global::Alice.Std.async.__AliceModule_index.Finally({calleeText}, () => {EmitExpr(c.Args[0])}())";
        }

        return $"{EmitExpr(c.Callee)}({string.Join(", ", c.Args.Select(EmitExpr))})";
    }

    private string EmitGenericCall(GenericCallExpr c)
    {
        var callee = EmitExpr(c.Callee);
        return $"{callee}<{string.Join(", ", c.TypeArgs.Select(EmitType))}>({string.Join(", ", c.Args.Select(EmitExpr))})";
    }

    private string EmitSliceExpr(SliceExpr s)
    {
        var lo = s.Lo is null ? "0" : EmitExpr(s.Lo);
        var hi = s.Hi is null ? "null" : $"(int?)({EmitExpr(s.Hi)})";
        return $"global::Alice.Generated.__AliceStd.__SliceAuto(({EmitExpr(s.Target)}), ({lo}), {hi})";
    }

    private string EmitMember(MemberExpr m)
    {
        if (m.Member == "length")
        {
            return $"global::Alice.Generated.__AliceStd.__Len(({EmitExpr(m.Target)}))";
        }
        return $"{EmitExpr(m.Target)}.{m.Member}";
    }

    private string EmitBinary(BinaryExpr b)
    {
        var op = b.Op;
        if (op == "and") op = "&&";
        if (op == "or") op = "||";
        if (op == "+")
        {
            var l = b.Left;
            var r = b.Right;
            if (l is LiteralExpr ll && ll.Value is IntLiteralValue && r is MemberExpr rm)
            {
                return $"{EmitExpr(l)} + (int)({EmitExpr(rm)})";
            }
            if (r is LiteralExpr rr && rr.Value is IntLiteralValue && l is MemberExpr lm)
            {
                return $"(int)({EmitExpr(lm)}) + {EmitExpr(r)}";
            }
        }
        return $"{EmitExpr(b.Left)} {op} {EmitExpr(b.Right)}";
    }

    private string EmitLambda(LambdaExpr le)
    {
        var ps = string.Join(", ", le.Parameters.Select(p => $"{EmitType(p.Type)} {p.Name}"));
        var head = $"({ps})";
        var block = EmitLambdaBlock(le.Body);
        return $"{head} => {block}";
    }

    private string EmitLambdaBlock(BlockStmt block)
    {
        var sb = new StringBuilder();
        sb.AppendLine("{");
        foreach (var st in block.Statements)
        {
            EmitStmt(sb, st, indent: 1);
        }
        sb.Append("}");
        return sb.ToString().TrimEnd();
    }

    private string EmitLiteral(LiteralValue v)
    {
        return v switch
        {
            IntLiteralValue i => EmitIntLiteral(i),
            FloatLiteralValue f => EnsureHasDecimalOrExponent(f.Text),
            StringLiteralValue s => s.Text,
            CharLiteralValue c => "'" + c.Text.Replace("\\", "\\\\").Replace("'", "\\'") + "'",
            BoolLiteralValue b => b.Value ? "true" : "false",
            NullLiteralValue => "null",
            _ => "null",
        };
    }

    private static string EmitIntLiteral(IntLiteralValue i)
    {
        var suffix = i.Suffix;
        var text = i.Text;
        if (suffix is null) return text;
        return suffix switch
        {
            "i8" => "(sbyte)" + text,
            "i16" => "(short)" + text,
            "i32" => "(int)" + text,
            "i64" => text + "L",
            "u8" => "(byte)" + text,
            "u16" => "(ushort)" + text,
            "u32" => text + "U",
            "u64" => text + "UL",
            _ => text,
        };
    }

    private static string EnsureHasDecimalOrExponent(string text)
    {
        if (text.Contains('.') || text.Contains('e') || text.Contains('E'))
        {
            return text;
        }
        return text + ".0";
    }

    private string EmitType(TypeNode t)
    {
        return t switch
        {
            SimpleTypeNode s => EmitResolvedSimpleType(s),
            QualifiedTypeNode q => EmitQualifiedType(q),
            ArrayTypeNode a => EmitType(a.ElementType) + "[]",
            FunctionTypeNode f => EmitFunctionType(f),
            GenericTypeNode g => EmitGenericType(g),
            AssocTypeNode a2 => EmitAssocType(a2),
            PtrTypeNode p => EmitPtrType(p),
            _ => "object",
        };
    }

    private string EmitPtrType(PtrTypeNode p)
    {
        var et = EmitType(p.ElementType);
        return $"{et}*";
    }

    private string EmitResolvedSimpleType(SimpleTypeNode s)
    {
        if (string.Equals(s.Name, "Promise", StringComparison.Ordinal))
        {
            return "global::System.Threading.Tasks.Task";
        }
        return EmitResolvedSimpleTypeInner(s);
    }

    private string EmitResolvedSimpleTypeInner(SimpleTypeNode s)
    {
        if (_binding is not null && _binding.ResolvedAssocTypes.TryGetValue(NodeKey.FromSpan(s.Span), out var resolved))
        {
            return EmitType(resolved);
        }
        return EmitSimpleType(s);
    }

    private string EmitGenericType(GenericTypeNode g)
    {
        if (g.BaseType is SimpleTypeNode ps && string.Equals(ps.Name, "Promise", StringComparison.Ordinal) && g.TypeArguments.Count == 1)
        {
            return $"global::System.Threading.Tasks.Task<{EmitType(g.TypeArguments[0])}>";
        }

        if (g.BaseType is SimpleTypeNode s && TryResolveTypeAlias(s.Name, out var a0) && a0.TypeParams.Count == g.TypeArguments.Count)
        {
            var map = new Dictionary<string, TypeNode>(StringComparer.Ordinal);
            for (var i = 0; i < a0.TypeParams.Count; i++)
            {
                map[a0.TypeParams[i].Name] = g.TypeArguments[i];
            }
            var expanded = SubstituteAliasTypeParams(a0.Target, map);
            return EmitType(expanded);
        }

        return $"{EmitType(g.BaseType)}<{string.Join(", ", g.TypeArguments.Select(EmitType))}>";
    }

    private static TypeNode SubstituteAliasTypeParams(TypeNode node, IReadOnlyDictionary<string, TypeNode> map)
    {
        return node switch
        {
            SimpleTypeNode s => map.TryGetValue(s.Name, out var r) ? r : node,
            QualifiedTypeNode => node,
            ArrayTypeNode a => new ArrayTypeNode(SubstituteAliasTypeParams(a.ElementType, map), a.Span),
            FunctionTypeNode f => new FunctionTypeNode(f.ParameterTypes.Select(p => SubstituteAliasTypeParams(p, map)).ToList(), SubstituteAliasTypeParams(f.ReturnType, map), f.Span),
            GenericTypeNode g => new GenericTypeNode(SubstituteAliasTypeParams(g.BaseType, map), g.TypeArguments.Select(ta => SubstituteAliasTypeParams(ta, map)).ToList(), g.Span),
            AssocTypeNode a2 => new AssocTypeNode(SubstituteAliasTypeParams(a2.BaseType, map), a2.Member, a2.Span),
            _ => node,
        };
    }

    private string EmitQualifiedType(QualifiedTypeNode q)
    {
        if (_binding is not null && _binding.ResolvedAssocTypes.TryGetValue(NodeKey.FromSpan(q.Span), out var resolved))
        {
            return EmitType(resolved);
        }
        return string.Join(".", q.Segments.Select(MapType));
    }

    private string EmitAssocType(AssocTypeNode a2)
    {
        if (_binding is not null && _binding.ResolvedAssocTypes.TryGetValue(NodeKey.FromSpan(a2.Span), out var resolved))
        {
            return EmitType(resolved);
        }
        return "object";
    }

    private string EmitSimpleType(SimpleTypeNode s)
    {
        var mapped = MapType(s.Name);
        if (mapped != s.Name) return mapped;

        if (TryResolveTypeAlias(s.Name, out var a0) && a0.TypeParams.Count == 0)
        {
            return EmitType(a0.Target);
        }

        return s.Name;
    }

    private bool TryGetDefaultTypeArgsForClass(TypeNode t, out IReadOnlyList<TypeNode> defaults)
    {
        defaults = Array.Empty<TypeNode>();
        var name = t switch
        {
            SimpleTypeNode s => s.Name,
            QualifiedTypeNode q => q.Segments.Count > 0 ? q.Segments.Last() : string.Empty,
            _ => string.Empty,
        };
        if (string.IsNullOrWhiteSpace(name)) return false;

        ClassDecl? found = null;

        if (_currentUnit is not null)
        {
            var local = _currentUnit.Classes.Where(c => string.Equals(c.Name, name, StringComparison.Ordinal)).ToList();
            if (local.Count == 1) found = local[0];
            if (local.Count > 1) return false;
        }

        if (found is null)
        {
            var program = _binding?.Program;
            if (program is null) return false;
            var matches = program.Units.SelectMany(u => u.Classes)
                .Where(c => string.Equals(c.Name, name, StringComparison.Ordinal))
                .ToList();
            if (matches.Count != 1) return false;
            found = matches[0];
        }

        if (found.TypeParams.Count == 0) return false;
        if (found.TypeParams.Any(tp => tp.DefaultType is null)) return false;
        defaults = found.TypeParams.Select(tp => tp.DefaultType!).ToList();
        return true;
    }

    private string EmitFunctionType(FunctionTypeNode f)
    {
        var paramTypes = f.ParameterTypes.Select(EmitType).ToList();
        var ret = EmitType(f.ReturnType);
        if (ret == "void")
        {
            if (paramTypes.Count == 0) return "global::System.Action";
            return $"global::System.Action<{string.Join(", ", paramTypes)}>";
        }

        if (paramTypes.Count == 0) return $"global::System.Func<{ret}>";
        var all = string.Join(", ", paramTypes.Concat(new[] { ret }));
        return $"global::System.Func<{all}>";
    }

    private static string MapType(string name)
    {
        return name switch
        {
            "i8" => "sbyte",
            "i16" => "short",
            "i32" => "int",
            "i64" => "long",
            "u8" => "byte",
            "u16" => "ushort",
            "u32" => "uint",
            "u64" => "ulong",
            "f32" => "float",
            "f64" => "double",
            "int" => "int",
            "uint" => "uint",
            "float" => "float",
            "double" => "double",
            "bool" => "bool",
            "char" => "char",
            "string" => "string",
            "any" => "object",
            "void" => "void",
            _ => name,
        };
    }

    private static void Indent(StringBuilder sb, int indent)
    {
        sb.Append(new string(' ', indent * 4));
    }

    private static void EmitLineDirective(StringBuilder sb, SourceSpan span)
    {
        var file = span.SourcePath.Replace("\\", "\\\\");
        sb.AppendLine($"#line {span.Line} \"{file}\"");
    }

    private static void EmitStd(StringBuilder sb)
    {
        sb.AppendLine("    internal static class __AliceStd");
        sb.AppendLine("    {");
        sb.AppendLine("        public static void __Defer(global::System.Collections.Generic.List<global::System.Action> stack, global::System.Action f)");
        sb.AppendLine("        {");
        sb.AppendLine("            stack.Add(f);");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        public static int __Len<T>(T[] a) => a.Length;");
        sb.AppendLine("        public static int __Len<T>(global::Alice.Std.slice.__AliceModule_index.Slice<T> s) => s.len;");
        sb.AppendLine("        public static int __Len(string s) => s.Length;");
        sb.AppendLine();

        sb.AppendLine("        public static T __Index<T>(T[] a, int i) => a[i];");
        sb.AppendLine("        public static T __Index<T>(global::Alice.Std.slice.__AliceModule_index.Slice<T> s, int i)");
        sb.AppendLine("        {");
        sb.AppendLine("            if ((uint)i >= (uint)s.len) throw new global::System.ArgumentOutOfRangeException(nameof(i));");
        sb.AppendLine("            return s.Get(i);");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        public static T __SetIndex<T>(T[] a, int i, T v)");
        sb.AppendLine("        {");
        sb.AppendLine("            a[i] = v;");
        sb.AppendLine("            return v;");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        public static T __SetIndex<T>(global::Alice.Std.slice.__AliceModule_index.Slice<T> s, int i, T v)");
        sb.AppendLine("        {");
        sb.AppendLine("            if ((uint)i >= (uint)s.len) throw new global::System.ArgumentOutOfRangeException(nameof(i));");
        sb.AppendLine("            s.Set(i, v);");
        sb.AppendLine("            return v;");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        public static global::Alice.Std.slice.__AliceModule_index.Slice<T> __SliceAuto<T>(T[] a, int lo, int? hi)");
        sb.AppendLine("        {");
        sb.AppendLine("            var h = hi ?? a.Length;");
        sb.AppendLine("            if ((uint)lo > (uint)a.Length) throw new global::System.ArgumentOutOfRangeException(nameof(lo));");
        sb.AppendLine("            if (h < lo || h > a.Length) throw new global::System.ArgumentOutOfRangeException(nameof(hi));");
        sb.AppendLine("            return global::Alice.Std.slice.__AliceModule_index.SliceArray(a, lo, h);");
        sb.AppendLine("        }");
        sb.AppendLine();

        sb.AppendLine("        public static global::Alice.Std.slice.__AliceModule_index.Slice<T> __SliceAuto<T>(global::Alice.Std.slice.__AliceModule_index.Slice<T> s, int lo, int? hi)");
        sb.AppendLine("        {");
        sb.AppendLine("            var h = hi ?? s.len;");
        sb.AppendLine("            if ((uint)lo > (uint)s.cap) throw new global::System.ArgumentOutOfRangeException(nameof(lo));");
        sb.AppendLine("            if (h < lo || h > s.cap) throw new global::System.ArgumentOutOfRangeException(nameof(hi));");
        sb.AppendLine("            return global::Alice.Std.slice.__AliceModule_index.SliceSlice(s, lo, h);");
        sb.AppendLine("        }");
        sb.AppendLine();
        sb.AppendLine("        public static void Print(object? x)");
        sb.AppendLine("        {");
            sb.AppendLine("            if (x is null)");
            sb.AppendLine("            {");
                sb.AppendLine("                Console.WriteLine(\"null\");");
                sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
            sb.AppendLine("            if (x is Array arr)");
            sb.AppendLine("            {");
                sb.AppendLine("                var parts = new string[arr.Length];");
                sb.AppendLine("                for (var i = 0; i < arr.Length; i++)");
                sb.AppendLine("                {");
                    sb.AppendLine("                    var v = arr.GetValue(i);");
                    sb.AppendLine("                    parts[i] = v is null ? \"null\" : v.ToString()!;");
                sb.AppendLine("                }");
                sb.AppendLine("                Console.WriteLine(\"[\" + string.Join(\", \", parts) + \"]\");");
                sb.AppendLine("                return;");
            sb.AppendLine("            }");
            sb.AppendLine();
        sb.AppendLine("            x = TryNormalizeToPrintable(x);");
        sb.AppendLine("            if (x is Array arr2)");
        sb.AppendLine("            {");
        sb.AppendLine("                var parts = new string[arr2.Length];");
        sb.AppendLine("                for (var i = 0; i < arr2.Length; i++)");
        sb.AppendLine("                {");
        sb.AppendLine("                    var v = arr2.GetValue(i);");
        sb.AppendLine("                    parts[i] = v is null ? \"null\" : v.ToString()!;");
        sb.AppendLine("                }");
        sb.AppendLine("                Console.WriteLine(\"[\" + string.Join(\", \", parts) + \"]\");");
        sb.AppendLine("                return;");
        sb.AppendLine("            }");
        sb.AppendLine();
        sb.AppendLine("            Console.WriteLine(x.ToString());");
        sb.AppendLine("        }");

        sb.AppendLine("        private static object TryNormalizeToPrintable(object x)");
        sb.AppendLine("        {");
        sb.AppendLine("            var xt = x.GetType();");
        sb.AppendLine("            if (xt.IsGenericType && xt.GetGenericTypeDefinition() == typeof(global::Alice.Std.slice.__AliceModule_index.Slice<>))");
        sb.AppendLine("            {");
        sb.AppendLine("                var m = xt.GetMethod(\"ToArray\", global::System.Reflection.BindingFlags.Public | global::System.Reflection.BindingFlags.Instance);");
        sb.AppendLine("                if (m is null) return x;");
        sb.AppendLine("                return m.Invoke(x, global::System.Array.Empty<object?>()) ?? x;");
        sb.AppendLine("            }");
        sb.AppendLine("            return x;");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
    }
}
