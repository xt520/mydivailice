using System;
using System.Collections.Generic;

namespace Alice.Compiler;

internal sealed record ProgramNode(IReadOnlyList<CompilationUnit> Units);

internal sealed record CompilationUnit(
    string ModuleName,
    string FileBaseName,
    string SourcePath,
    bool IsInterfaceFile,
    bool IsStdLib,
    NamespaceDecl? Namespace,
    IReadOnlyList<ImportDecl> Imports,
    IReadOnlyList<TypeAliasDecl> TypeAliases,
    IReadOnlyList<GlobalVarDecl> Globals,
    IReadOnlyList<FunDecl> Functions,
    IReadOnlyList<ClassDecl> Classes,
    IReadOnlyList<StructDecl> Structs,
    IReadOnlyList<EnumDecl> Enums,
    IReadOnlyList<InterfaceDecl> Interfaces);

internal sealed record NamespaceDecl(QualifiedName Name, SourceSpan Span);
internal sealed record ImportDecl(QualifiedName ModuleName, string Alias, SourceSpan Span);

internal sealed record GlobalVarDecl(bool IsConst, string Name, TypeNode Type, Expr Init, SourceSpan Span);

internal sealed record TypeAliasDecl(string Name, IReadOnlyList<TypeParamDecl> TypeParams, TypeNode Target, SourceSpan Span);

internal sealed record QualifiedName(IReadOnlyList<string> Segments)
{
    public override string ToString() => string.Join(".", Segments);
}

internal sealed record FunDecl(
    bool IsAsync,
    IReadOnlyList<string> Attributes,
    string Name,
    IReadOnlyList<TypeParamDecl> TypeParams,
    IReadOnlyList<ParamDecl> Parameters,
    TypeNode? ReturnType,
    BlockStmt Body,
    SourceSpan Span);

internal sealed record TypeParamDecl(
    string Name,
    IReadOnlyList<TypeNode> Constraints,
    TypeNode? DefaultType,
    SourceSpan Span);
internal sealed record ParamDecl(string Name, TypeNode Type, SourceSpan Span);

internal enum Accessibility
{
    Default,
    Public,
    Protected,
    Private,
}

internal sealed record NamedTypeRef(string Name, IReadOnlyList<TypeNode> TypeArguments, SourceSpan Span)
{
    public NamedTypeRef(string Name, SourceSpan Span) : this(Name, Array.Empty<TypeNode>(), Span)
    {
    }
}

internal abstract record TypeDecl(SourceSpan Span);

internal sealed record StructDecl(
    bool IsPublic,
    bool IsExtern,
    string Name,
    IReadOnlyList<TypeParamDecl> TypeParams,
    IReadOnlyList<NamedTypeRef> BaseTypes,
    IReadOnlyList<ClassMember> Members,
    SourceSpan Span) : TypeDecl(Span);

internal sealed record EnumDecl(
    bool IsPublic,
    string Name,
    IReadOnlyList<EnumMemberDecl> Members,
    SourceSpan Span) : TypeDecl(Span);

internal sealed record EnumMemberDecl(string Name, long? Value, SourceSpan Span);

internal sealed record ClassDecl(
    bool IsPublic,
    bool IsExtern,
    string Name,
    IReadOnlyList<TypeParamDecl> TypeParams,
    IReadOnlyList<NamedTypeRef> BaseTypes,
    IReadOnlyList<ClassMember> Members,
    SourceSpan Span) : TypeDecl(Span);

internal sealed record InterfaceDecl(
    bool IsPublic,
    bool IsExtern,
    string Name,
    IReadOnlyList<TypeParamDecl> TypeParams,
    IReadOnlyList<NamedTypeRef> BaseTypes,
    IReadOnlyList<InterfaceEntry> Entries,
    SourceSpan Span) : TypeDecl(Span);

internal abstract record ClassMember(SourceSpan Span);

internal sealed record FieldDecl(
    Accessibility Access,
    bool IsStatic,
    bool IsConst,
    string Name,
    TypeNode Type,
    Expr? Init,
    SourceSpan Span) : ClassMember(Span);

internal sealed record MethodDecl(
    Accessibility Access,
    bool IsStatic,
    bool IsExtern,
    IReadOnlyList<string> Attributes,
    string Name,
    IReadOnlyList<TypeParamDecl> TypeParams,
    IReadOnlyList<ParamDecl> Parameters,
    TypeNode? ReturnType,
    BlockStmt Body,
    SourceSpan Span) : ClassMember(Span);

internal sealed record CtorDecl(
    Accessibility Access,
    bool IsExtern,
    IReadOnlyList<string> Attributes,
    string Name,
    IReadOnlyList<ParamDecl> Parameters,
    BlockStmt Body,
    SourceSpan Span) : ClassMember(Span);

internal sealed record AssociatedTypeBindDecl(string Name, TypeNode ValueType, SourceSpan Span) : ClassMember(Span);

internal abstract record InterfaceEntry(SourceSpan Span);

internal sealed record InterfaceMethodSig(
    string Name,
    IReadOnlyList<ParamDecl> Parameters,
    TypeNode? ReturnType,
    SourceSpan Span) : InterfaceEntry(Span);

internal sealed record InterfaceEmbedEntry(NamedTypeRef Embedded, SourceSpan Span) : InterfaceEntry(Span);

internal sealed record AssociatedTypeDecl(string Name, SourceSpan Span) : InterfaceEntry(Span);

internal abstract record Stmt(SourceSpan Span);
internal sealed record BlockStmt(IReadOnlyList<Stmt> Statements, SourceSpan Span) : Stmt(Span);
internal sealed record VarDeclStmt(string Name, TypeNode? ExplicitType, Expr Init, VarDeclKind Kind, SourceSpan Span) : Stmt(Span);
internal enum VarDeclKind { VarKeyword, ColonEquals, ExplicitType }
internal sealed record ExprStmt(Expr Expression, SourceSpan Span) : Stmt(Span);
internal sealed record IfStmt(Expr Condition, BlockStmt Then, BlockStmt? Else, SourceSpan Span) : Stmt(Span);
internal sealed record WhileStmt(Expr Condition, BlockStmt Body, SourceSpan Span) : Stmt(Span);
internal sealed record ReturnStmt(Expr? Expression, SourceSpan Span) : Stmt(Span);
internal sealed record BreakStmt(SourceSpan Span) : Stmt(Span);
internal sealed record ContinueStmt(SourceSpan Span) : Stmt(Span);
internal sealed record TryStmt(BlockStmt TryBlock, ExceptClause? Except, BlockStmt? Finally, SourceSpan Span) : Stmt(Span);
internal sealed record ExceptClause(string? Name, TypeNode? Type, BlockStmt Block, SourceSpan Span);
internal sealed record RaiseStmt(Expr? Expression, SourceSpan Span) : Stmt(Span);
internal sealed record DeferStmt(Expr Expression, SourceSpan Span) : Stmt(Span);

internal abstract record Expr(SourceSpan Span);
internal sealed record IdentifierExpr(string Name, SourceSpan Span) : Expr(Span);
internal sealed record LiteralExpr(LiteralValue Value, SourceSpan Span) : Expr(Span);
internal sealed record UnaryExpr(string Op, Expr Operand, SourceSpan Span) : Expr(Span);
internal sealed record BinaryExpr(string Op, Expr Left, Expr Right, SourceSpan Span) : Expr(Span);
internal sealed record AssignExpr(Expr Target, Expr Value, SourceSpan Span) : Expr(Span);
internal sealed record CastExpr(Expr Target, TypeNode Type, SourceSpan Span) : Expr(Span);
internal sealed record CallExpr(Expr Callee, IReadOnlyList<Expr> Args, SourceSpan Span) : Expr(Span);
internal sealed record GenericCallExpr(Expr Callee, IReadOnlyList<TypeNode> TypeArgs, IReadOnlyList<Expr> Args, SourceSpan Span) : Expr(Span);
internal sealed record IndexExpr(Expr Target, Expr Index, SourceSpan Span) : Expr(Span);
internal sealed record MemberExpr(Expr Target, string Member, SourceSpan Span) : Expr(Span);
internal sealed record ArrayLiteralExpr(IReadOnlyList<Expr> Elements, SourceSpan Span) : Expr(Span);
internal sealed record ParenWrapExpr(Expr Inner, SourceSpan Span) : Expr(Span);
internal sealed record NewExpr(TypeNode Type, IReadOnlyList<Expr> Args, SourceSpan Span) : Expr(Span);
internal sealed record ThisExpr(SourceSpan Span) : Expr(Span);
internal sealed record LambdaExpr(IReadOnlyList<ParamDecl> Parameters, TypeNode? ReturnType, BlockStmt Body, SourceSpan Span) : Expr(Span);
internal sealed record NewArrayExpr(TypeNode ElementType, Expr Size, SourceSpan Span) : Expr(Span);
internal sealed record SliceExpr(Expr Target, Expr? Lo, Expr? Hi, SourceSpan Span) : Expr(Span);

internal sealed record AwaitExpr(Expr Target, SourceSpan Span) : Expr(Span);

internal sealed record GoExpr(CallExpr Call, SourceSpan Span) : Expr(Span);

internal sealed record AddrOfExpr(Expr Target, SourceSpan Span) : Expr(Span);
internal sealed record DerefExpr(Expr Target, SourceSpan Span) : Expr(Span);

internal abstract record TypeNode(SourceSpan Span);
internal sealed record SimpleTypeNode(string Name, SourceSpan Span) : TypeNode(Span);
internal sealed record QualifiedTypeNode(IReadOnlyList<string> Segments, SourceSpan Span) : TypeNode(Span);
internal sealed record ArrayTypeNode(TypeNode ElementType, SourceSpan Span) : TypeNode(Span);
internal sealed record FunctionTypeNode(IReadOnlyList<TypeNode> ParameterTypes, TypeNode ReturnType, SourceSpan Span) : TypeNode(Span);
internal sealed record GenericTypeNode(TypeNode BaseType, IReadOnlyList<TypeNode> TypeArguments, SourceSpan Span) : TypeNode(Span);
internal sealed record AssocTypeNode(TypeNode BaseType, string Member, SourceSpan Span) : TypeNode(Span);

internal sealed record PtrTypeNode(TypeNode ElementType, SourceSpan Span) : TypeNode(Span);

internal abstract record LiteralValue;
internal sealed record IntLiteralValue(string Text, string? Suffix) : LiteralValue;
internal sealed record FloatLiteralValue(string Text, string? Suffix) : LiteralValue;
internal sealed record StringLiteralValue(string Text) : LiteralValue;
internal sealed record CharLiteralValue(string Text) : LiteralValue;
internal sealed record BoolLiteralValue(bool Value) : LiteralValue;
internal sealed record NullLiteralValue() : LiteralValue;
