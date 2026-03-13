# 起始状态

- 时间：2026-03-11（Asia/Shanghai）
- 运行环境：.NET 8（Alice -> 生成 C# -> Roslyn 编译 -> collectible AssemblyLoadContext 执行）

## 当前已支持（仓库现状）

- 语言：namespace/import、顶层 fun、全局变量、数组字面量、while/if、try/except/finally、raise、lambda、new 对象、new 数组（`new T[n]`）、defer（基于 try/finally 展开）、class/interface（v0.3）
- 模块系统：支持 `.alice` 多模块加载；支持 `.alicei` 作为接口文件加载但不发射 C#；支持目录 `index` 模块；默认把仓库根目录 `std/` 加入 module 搜索路径
- 标准库运行时（C#）：新增 `src/Alice.Std` 项目（net8.0），已实现 io/time/thread/net/http/http.server 的最小可用实现
- CLI：已有 run/build/check/selftest（v0.3）

补充（已在仓库落地并由 `selftest` 覆盖）：

- `enum` / `struct`
- `slice`（`slice.Slice<T>` + `[lo:hi]` 语法）
- 标准库模块：`bytes` / `crypto` / `mem` / `async`（仅 DelayMs）/ `sync`（Chan）/ `net.tls`（仅 Smoke 占位）

新增（本轮落地）：

- 语言前端：`async fun`、`await`、`go`（最小闭环）
- 标准库 async：`Then/Catch/Finally`（基于 Task/ContinueWith）
- 标准库 async：`withTimeout<T>` + `CancelSource/CancelToken`（最小取消/超时能力）
- 标准库 sync：`cap=0` 改为无缓冲 rendezvous 语义
- 标准库 mem：`memcpy/memcpyToArray` + 示例 `examples/unsafe/NativeBufferCopy.alice`
- 标准库 tls：新增 `wrap(conn, serverName)`（SslStream 包装；示例仍以 Smoke 为主）
- CLI：新增 `--allow-unsafe`（允许生成/编译 unsafe C#，当前为骨架）
- 语法属性：`@unsafe`（默认拒绝；需 `--allow-unsafe` 才允许编译）
- `ptr<T>`：类型与 `&`/`*`（最小集）+ 示例 `examples/unsafe/PtrU8Write.alice`
- 指针扩展：`mem.ptr<T>` 与 `mem.memcpyPtr` + 示例 `examples/unsafe/PtrGenericSmoke.alice`、`examples/unsafe/MemcpyPtrSmoke.alice`

## 基线回归（必须通过）

### 命令

```bash
dotnet build Alice.sln -c Release
dotnet run --project src/alicec -- selftest
```

### 结果

- `dotnet build`：通过（存在 NU1603 警告，不影响构建）
- `selftest`：34/34 通过（包含 enum/struct/slice/bytes/crypto/tls/async/sync/unsafe/mem 等示例）
- `selftest`：38/38 通过（新增 Timeout 用例；unsafe 用例需 allow-unsafe）

# 进度记录

## 阶段 A（准备）

- 已创建本报告文件并记录基线。
