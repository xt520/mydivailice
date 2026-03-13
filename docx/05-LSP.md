# 05-LSP（编辑器智能提示）

Alice 编译器内置一个最小 LSP server，可用于编辑器的诊断、补全与跳转。

当前实现基于 `OmniSharp.Extensions.LanguageServer`（与 LSP 规范对齐、易于扩展）。

## 启动

在仓库根目录：

```bash
./alice lsp
```

等价于：

```bash
dotnet run --project src/alicec -- lsp
```

## 当前支持

- Diagnostics：lexer/parser 诊断（语法错误）
- Completion：关键字 + 当前文件顶层符号
- Definition：当前文件内顶层符号（fun/class/struct/enum/interface/type alias）跳转

## 不支持/限制（当前阶段）

- 仅做“单文件级”分析：补全与跳转不会跨文件索引
- Diagnostics 目前只包含 lexer/parser；不包含 binder/typecheck 级别的诊断
- `textDocument/didChange` 使用 Full 同步（每次传整文件文本）

## 后续计划（按优先级）

- 引入 binder 诊断并转成 LSP diagnostics（类型错误、未定义符号等）
- 跨文件索引：workspace symbols / references / rename
- 更准确的 definition（定位到标识符 token 的精确范围）

## VS Code（最小配置示意）

你可以用任意 LSP 客户端/插件启动该命令并通过 stdio 通信。
如果使用自定义客户端，需要把 server 命令配置为：

- Windows：`alice.cmd lsp`
- Linux/macOS：`./alice lsp`

说明：LSP 是 stdio 协议，server 的 stdout 必须保持纯协议输出。
