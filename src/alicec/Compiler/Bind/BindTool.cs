using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;

namespace Alice.Compiler;

internal static class BindTool
{
    internal sealed record BindOptions(string RefPath, string OutputDir);

    internal static int Run(BindOptions opts, TextWriter diagnostics)
    {
        if (string.IsNullOrWhiteSpace(opts.RefPath))
        {
            diagnostics.WriteLine("错误：--ref 不能为空");
            return 1;
        }

        var refPath = Path.GetFullPath(opts.RefPath);
        if (!File.Exists(refPath))
        {
            diagnostics.WriteLine($"找不到 DLL: {refPath}");
            return 1;
        }

        var outDir = Path.GetFullPath(opts.OutputDir);
        Directory.CreateDirectory(outDir);

        var asm = Assembly.LoadFrom(refPath);

        var exported = asm.GetTypes()
            .Where(t => !t.IsNested)
            .Where(t => !t.IsGenericTypeDefinition || t.GetGenericArguments().All(a => a.IsGenericParameter))
            .ToList();


        var candidates = exported
            .Where(t => (t.IsClass || t.IsInterface) && !t.IsNested)
            .Where(t => t.IsPublic)
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith("Alice.Std", StringComparison.Ordinal))
            .ToList();

        foreach (var parent in exported.Where(t => t.IsClass && t.IsPublic))
        {
            foreach (var nt in parent.GetNestedTypes(BindingFlags.Public))
            {
                if (nt.Namespace is null || !nt.Namespace.StartsWith("Alice.Std", StringComparison.Ordinal)) continue;
                if (nt.IsEnum) continue;
                candidates.Add(nt);
            }
        }

        diagnostics.WriteLine($"bind: loaded={refPath}");

        foreach (var group in candidates.GroupBy(t => t.Namespace ?? string.Empty, StringComparer.Ordinal))
        {
            var ns = group.Key;
            var nsSegs = string.IsNullOrWhiteSpace(ns) ? Array.Empty<string>() : ns.Split('.');
            var dir = nsSegs.Aggregate(outDir, Path.Combine);
            Directory.CreateDirectory(dir);
            var outPath = Path.Combine(dir, "index.alicei");

            var lines = new List<string>();
            if (!string.IsNullOrWhiteSpace(ns))
            {
                lines.Add($"namespace {ns}");
                lines.Add(string.Empty);
            }

            foreach (var t in group.OrderBy(t => t.Name, StringComparer.Ordinal))
            {
                EmitTypeDecl(lines, t);
                lines.Add(string.Empty);
            }

            File.WriteAllText(outPath, string.Join("\n", lines).TrimEnd() + "\n");
        }

        return 0;
    }

    private static void EmitTypeDecl(List<string> lines, Type t)
    {
        if (t.IsEnum)
        {
            return;
        }

        var isNestedPublic = t.IsNested ? t.IsNestedPublic : t.IsPublic;
        if (!isNestedPublic)
        {
            return;
        }

        if (t.IsInterface)
        {
            lines.Add($"interface {AliceTypeName(t)}{EmitTypeParamList(t)} {{");
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (m.IsSpecialName) continue;
                lines.Add("  " + EmitFunSig(m));
            }
            lines.Add("}");
            return;
        }

        if (t.IsClass)
        {
            lines.Add($"class {AliceTypeName(t)}{EmitTypeParamList(t)} {{");

            foreach (var ctor in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance).OrderBy(c => c.GetParameters().Length))
            {
                lines.Add("  " + EmitCtorSig(t, ctor));
            }

            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (m.IsSpecialName) continue;
                lines.Add("  " + EmitFunSig(m));
            }

            lines.Add("}");
        }
    }

    private static string EmitTypeParamList(Type t)
    {
        if (!t.IsGenericTypeDefinition) return string.Empty;
        var tps = t.GetGenericArguments().Select(a => a.Name).ToList();
        return tps.Count == 0 ? string.Empty : $"<{string.Join(",", tps)}>";
    }

    private static string EmitCtorSig(Type declaringType, ConstructorInfo ctor)
    {
        var ps = ctor.GetParameters();
        var parts = new List<string>();
        for (var i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            var pn = string.IsNullOrWhiteSpace(p.Name) ? "p" + i : p.Name!;
            parts.Add($"{pn}:{MapDotNetType(p.ParameterType)}");
        }
        return $"fun {AliceTypeName(declaringType)}({string.Join(",", parts)})";
    }

    private static string EmitFunSig(MethodInfo m)
    {
        var ps = m.GetParameters();
        var parts = new List<string>();
        for (var i = 0; i < ps.Length; i++)
        {
            var p = ps[i];
            var pn = string.IsNullOrWhiteSpace(p.Name) ? "p" + i : p.Name!;
            parts.Add($"{pn}:{MapDotNetType(p.ParameterType)}");
        }

        var staticPrefix = m.IsStatic ? "static " : string.Empty;
        var tps = m.IsGenericMethodDefinition ? $"<{string.Join(",", m.GetGenericArguments().Select(a => a.Name))}>" : string.Empty;
        var ret = MapDotNetType(m.ReturnType);
        var retPart = ret == "void" ? string.Empty : $": {ret}";
        return $"{staticPrefix}fun {m.Name}{tps}({string.Join(",", parts)}){retPart}";
    }

    private static string AliceTypeName(Type t)
    {
        var name = t.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0) name = name[..tick];
        return name;
    }

    private static string MapDotNetType(Type t)
    {
        if (t.IsByRef) t = t.GetElementType()!;

        if (t == typeof(void)) return "void";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(string)) return "string";
        if (t == typeof(object)) return "any";
        if (t == typeof(byte)) return "u8";
        if (t == typeof(sbyte)) return "i8";
        if (t == typeof(short)) return "i16";
        if (t == typeof(ushort)) return "u16";
        if (t == typeof(int)) return "int";
        if (t == typeof(uint)) return "u32";
        if (t == typeof(long)) return "i64";
        if (t == typeof(ulong)) return "u64";
        if (t == typeof(float)) return "f32";
        if (t == typeof(double)) return "f64";
        if (t == typeof(char)) return "char";

        if (t.IsArray)
        {
            return MapDotNetType(t.GetElementType()!) + "[]";
        }

        if (t.IsGenericParameter)
        {
            return t.Name;
        }

        if (t.IsGenericType)
        {
            var def = t.GetGenericTypeDefinition();
            var name = AliceTypeName(def);
            var args = t.GetGenericArguments().Select(MapDotNetType).ToList();
            return $"{name}<{string.Join(",", args)}>";
        }

        if (t.Namespace is "System" && t.Name == "Nullable`1")
        {
            return "any";
        }

        return "any";
    }
}
