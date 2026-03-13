# Alice v0（最小可用版）

这是一个最小可用的爱丽丝语言编译器（Alice v0）实现。它把 `.alice` 源码编译为 C#，再用 Roslyn 在运行时编译并在当前进程中执行。

## 目录结构

- `Alice.sln`
- `src/alicec/`：编译器与 CLI
- `examples/`：示例代码
- `docx/`：使用文档（从快速上手到模块说明）

## 构建

```bash
dotnet --version
dotnet build
```

## 一分钟上手（推荐）

仓库根目录提供了简易启动器脚本（本质是 `dotnet run --project src/alicec -- ...` 的薄封装）：

- Linux/macOS：`./alice`（或旧脚本 `./alicec.sh`）
- Windows：`alice.cmd`（cmd）或 `./alice.ps1`（PowerShell）

示例：

```bash
# 直接运行
./alice run examples/bubble_sort.alice

# 也支持傻瓜式直跑（等价于 run）
./alice examples/bubble_sort.alice

# 运行自测
./alice selftest

# 运行 unsafe 示例（需要显式开关）
./alice run examples/unsafe/PtrU8Write.alice --allow-unsafe
```

更多文档见：[`docx/README.md`](./docx/README.md)

## Alice v0.4/v0.5：标准库、.alicei/extern、泛型、关联类型、type alias、defer、bind

本版本在 v0.3 基础上新增（均已落地并在 `selftest` 覆盖）：

- 标准库运行时程序集：`src/Alice.Std/`（编译器默认引用）
- 标准库声明：`std/*.alicei`（模块加载器默认把 `./std` 当作搜索根之一）
- `.alicei`：允许仅声明签名（无函数体），用于“只入符号表、不生成 C#”的接口/声明文件
- `extern`：仅允许出现在 `.alicei` 文件中（用于声明来自 DLL 的实现）
- 泛型：`<T extends IFoo, IBar = Default>` + C# `where` 发射
- 关联类型：`interface { type Item }` + `class { type Item = int }` + `Sequence<int>` 语法糖（降级为 C# 额外泛型参数）
- type alias：支持泛型 alias 的发射期展开（示例见下）
- `defer`：block 退出时 LIFO 执行（C# `try/finally` 展开）
- `alicec bind`：从 DLL 生成 `.alicei`
- `enum`：枚举与显式/隐式值（示例：`examples/enum/Basic.alice`）
- `struct`：值类型结构体、字段与方法、构造函数（示例：`examples/struct/Point.alice`）
- `sync`：`Chan<T>` channel（示例：`examples/sync/ChanPingPong.alice`）
- `bytes/crypto/tls/mem`：标准库补充模块（示例分别见 `examples/bytes` / `examples/crypto` / `examples/tls` / `examples/unsafe`）

### 新 CLI

```bash
# 运行
dotnet run --project src/alicec -- run <entry.alice> [--emit-cs <path>] [--module-path <dir> ...] [-- <args...>]

# 仅检查（不运行）
dotnet run --project src/alicec -- check <entry.alice> [--emit-cs <path>] [--module-path <dir> ...]

# 构建 dll
dotnet run --project src/alicec -- build <entry.alice> -o <out.dll> [--emit-cs <path>] [--module-path <dir> ...]

# 从 DLL 生成 .alicei
dotnet run --project src/alicec -- bind --ref <path.dll> -o <outDir>

# 自测
dotnet run --project src/alicec -- selftest
```

### 标准库（import）

标准库运行时位于 `Alice.Std`（C# 命名空间前缀 `global::Alice.Std`），对应 Alice 侧的 `.alicei` 声明位于 `std/`。

可用模块（示例均在 `examples/` 并被 `selftest` 覆盖）：

- `import io as io`：文件/编码/流接口
- `import time as time`：`nowUnixMs/sleepMs`
- `import thread as thread`：`spawn/JoinHandle`
- `import sync as sync`：`Chan<T>`、`makeChan/send/recv/close`（`cap=0` 为无缓冲）
- `import async as a`：`DelayMs/Then/Catch/Finally/withTimeout` + `CancelSource/CancelToken`
- `import net as net`：TCP/UDP
- `import net.http as http`：同步 HTTP client
- `import net.http.server as srv`：`serveOnce` 本地 HTTP server
- `import net.tls as tls`：TLS smoke test（最小连通性验证）
- `import collections as c`：`List<T>` / `Map<K,V>`
- `import bytes as bytes`：字节序读写与拼接
- `import crypto as crypto`：`sha256/hmacSha256/randomBytes`
- `import mem as mem`：`NativeBuffer`、`alloc/free`

### 切片（slice）

Alice 提供与 Go 类似的切片类型 `slice.Slice<T>`（运行时在 `Alice.Std.slice`），以及表达式级的切片语法。

#### 导入

```alice
import slice as slice
```

#### 语法

- 数组切片：`a[lo:hi]`（返回 `slice.Slice<T>`）
- 切片再切片：`s[lo:hi]`（返回 `slice.Slice<T>`）
- `hi` 可省略：`a[lo:]` / `s[lo:]`

索引访问支持数组与切片：

- `a[i]` / `a[i] = v`
- `s[i]` / `s[i] = v`（会回写到底层数组）

`length` 也支持数组与切片：

- `a.length`
- `s.length`

#### 标准库 API（slice 模块）

见 `std/slice/index.alicei`：

- `FromArray<T>(a:T[]): Slice<T>`
- `SliceArray<T>(a:T[], lo:int, hi:int): Slice<T>`
- `SliceSlice<T>(s:Slice<T>, lo:int, hi:int): Slice<T>`
- `Append<T>(s:Slice<T>, v:T): Slice<T>`

#### 示例

见 `examples/slice/Basic.alice`（被 `selftest` 覆盖），包含：数组切片、slice 切片、slice 索引读写。

### enum/struct（语法示例）

#### enum

见 `examples/enum/Basic.alice`：

```alice
enum Color {
  Red = 1,
  Green,
  Blue = 10,
}

// 枚举成员值支持负数整数常量：
// enum E { A = -1 }

fun main() {
  print(Color.Red)
  print(Color.Green)
  print(Color.Blue)
}
```

#### struct

见 `examples/struct/Point.alice`：

```alice
struct Point {
  x:int
  y:int

  fun Point(x:int, y:int) {
    this.x = x
    this.y = y
  }

  fun Sum(): int { return this.x + this.y }
}
```

### 并发与异步（thread/sync/async）

- 线程：`thread.spawn` + `JoinHandle.Join()`（示例：`examples/sync/ChanPingPong.alice`）
- channel：`sync.Chan<T>` + `makeChan/send/recv/close`（示例：`examples/sync/ChanPingPong.alice`）
- 延迟：`async.DelayMs(ms:int)`（示例：`examples/async/DelaySmoke.alice`）

最小 channel 示例（见 `examples/sync/ChanPingPong.alice`）：

```alice
import sync as sync
import thread as th

fun main() {
  ch := sync.makeChan<string>(0)
  h := th.spawn(fun () { sync.send(ch, "ping") })
  msg := sync.recv(ch)
  print(msg)
  sync.close(ch)
  h.Join()
}
```

#### async/await/go（最小闭环）

- 顶层支持：`async fun`、`await expr`、`go callExpr`
- `async fun` 会发射为 C# `async Task/Task<T>`；若 `main` 是 `async`，入口会 `GetAwaiter().GetResult()` 等待完成
- 示例：
  - `examples/async/AsyncFun.alice`：`async fun main()` + `await`
  - `examples/async/GoThen.alice`：`go` + `await`
  - `examples/async/Timeout.alice`：`withTimeout` + `try/except`

### bytes/crypto/tls/mem（标准库补充）

- `bytes`：`readU16BE/readU32BE/writeU32BE/concat`（示例：`examples/bytes/Basic.alice`）
- `crypto`：`sha256/hmacSha256/randomBytes`（示例：`examples/crypto/Sha256Len.alice`）
- `net.tls`：`wrap(conn, serverName)`（最小封装）与 `Smoke()`（示例：`examples/tls/Smoke.alice`）
- `mem`：`NativeBuffer`、`alloc/free/memcpy/memcpyToArray`（示例：`examples/unsafe/MemSmoke.alice`、`examples/unsafe/NativeBufferCopy.alice`）

bytes 示例（见 `examples/bytes/Basic.alice`）：

```alice
import bytes as bytes

fun main() {
  b := new u8[4]
  bytes.writeU32BE(b, 0, 16909060)
  print(bytes.readU32BE(b, 0))
}
```

crypto 示例（见 `examples/crypto/Sha256Len.alice`）：

```alice
import crypto as crypto
import io as io

fun main() {
  h := crypto.sha256(io.utf8Encode("abc"))
  print(h.length)
}
```

HTTP server/client 示例（见 `examples/http/HttpServerClient.alice`）：

```bash
dotnet run --project src/alicec -- run examples/http/HttpServerClient.alice
```

注释：目前支持两种注释：

- 单行注释：`// ...`
- 块注释：`/* ... */`（可跨行；未闭合会报错）

### allow-unsafe（骨架）

编译器支持 `--allow-unsafe` 开关：用于允许使用 `@unsafe` 标注的函数（默认拒绝）。

当前已支持的 unsafe 能力（最小集）：

- 类型：`ptr<T>`（发射到 C# 的 `T*`）
- 表达式：取地址 `&x`、解引用 `*p`，且 `*p = v` 可作为赋值左值
- 标准库：`mem.ptrU8(NativeBuffer, off): ptr<u8>`
- 标准库：`mem.ptr<T>(NativeBuffer, off): ptr<T>`（`T` 需为 unmanaged）
- 标准库：`mem.memcpyPtr(dst:ptr<u8>, src:ptr<u8>, n:int)`

限制：

- 仅当函数标注 `@unsafe` 且编译开启 `--allow-unsafe` 时才允许编译通过

注意：

- `mem.alloc(n)` 返回的是 `mem.NativeBuffer`，不要把它直接存到 `ptr<T>` 字段里；推荐把 `NativeBuffer` 作为字段保存，需要指针时通过 `mem.ptr<T>(buf, off)` / `mem.ptrU8(buf, off)` 获取“指针视图”。

#### 用法示例（指针写入 NativeBuffer）

对应示例文件：`examples/unsafe/PtrU8Write.alice`、`examples/unsafe/PtrGenericSmoke.alice`、`examples/unsafe/MemcpyPtrSmoke.alice`

```alice
import mem as mem
import io as io

@unsafe
fun main() {
  nb := mem.alloc(3)

  // 获取指针（两种 API 都可）
  p := mem.ptrU8(nb, 0)
  // p := mem.ptr<u8>(nb, 0)

  // 原地写入 byte
  *p = 97
  *(p + 1) = 98
  *(p + 2) = 99

  out0 := new u8[3]
  mem.memcpyToArray(nb, 0, out0, 0, 3)
  mem.free(nb)
  print(io.utf8Decode(out0))
}
```

运行：

```bash
dotnet run --project src/alicec -- run examples/unsafe/PtrU8Write.alice --allow-unsafe
```

### 泛型（extends/where）

```alice
interface Show {
  fun Show(): string
}

fun showAll<T extends Show>(xs:T[]): string {
  return xs[0].Show() + xs[1].Show()
}
```

### 关联类型（associated types）

```alice
interface Sequence {
  type Item
  fun Len(): int
  fun Get(i:int): int
}

class IntSeq : Sequence {
  type Item = int
}

fun sum(s: Sequence<int>): int { return 0 }
```

注意：`type` 是一个“上下文关键字”，含义取决于位置：

- 在 `interface` 内：声明关联类型（仅声明名字，不是别名）
- 在 `class/struct` 内：绑定关联类型到具体类型
- 在文件顶层：声明类型别名（type alias）

实现说明（面向生成的 C#）：关联类型会降级为接口额外泛型参数（例如 `Sequence<__Assoc_Item>`）。

注意事项（避免误解）：

- 因为会降级为“额外泛型参数”，所以在使用关联类型的接口做泛型约束时，通常需要把接口写成带显式类型参数的形式（例如 `IProcessor<int>`），否则 C# 层可能出现“需要 1 个类型参数 / 不能推断类型参数 / T 没有某方法”等错误。

### type alias（含泛型）

```alice
// 顶层 type = 类型别名（type alias）
type Box<T> = T[]

fun main() {
  xs: Box<int> = new int[3]
  print(xs)
}
```

关联类型 vs 类型别名的对比：

```alice
// 关联类型：声明/绑定发生在 interface/class 内
interface Serializable {
  type RawType
  fun ToRaw(): RawType
}

class Buf : Serializable {
  type RawType = ptr<u8>
  fun ToRaw(): ptr<u8> { return mem.ptr<u8>(this.handle, 0) }
}

// 类型别名：只能在文件顶层声明
type NativePtr = ptr<u8>
```

常见坑：

- 本项目当前不支持 `typealias` 关键字；类型别名请使用顶层 `type Name = ...`。

### struct/class 可见性（常见坑）

当前实现中：

- `struct` 字段若不加 `public { ... }` 可见性块，在生成的 C# 中默认为 `private`，因此 struct 外部访问 `payload.id` 会触发“不具备可访问性”的编译错误。
- `class` 的构造函数/方法同理：如果要在类外 `new Foo()` 或调用方法，记得写 `public fun Foo(...)` / `public fun Bar(...)`。

示例：

```alice
struct TaskPayload {
  public { id:int; value:int }
  public fun TaskPayload(id:int, value:int) { this.id = id; this.value = value }
}

class FastProcessor {
  public fun FastProcessor() { }
}
```

### @unsafe 与 async/await（常见坑）

- `@unsafe` 会让对应函数在生成的 C# 中处于 `unsafe` 上下文。
- C# 不允许在 `unsafe` 上下文里使用 `await`（会报 `CS4004: 无法在不安全的上下文中等待`）。

推荐写法：把指针操作放进单独的 `@unsafe fun`，而把 `async fun main()` 保持为非 `@unsafe`。

### defer

```alice
import io as io

fun main() {
  f := io.create("tmp.txt")
  defer f.Close()
  f.Write(io.utf8Encode("hello"))
  f.Flush()
  f.Close()
  io.remove("tmp.txt")
}
```

## 运行（run）

```bash
dotnet run --project src/alicec -- run examples/bubble_sort.alice
```

期望输出（严格）：

```
[1, 2, 4, 5, 8]
```

可选：输出生成的 C# 便于调试

```bash
dotnet run --project src/alicec -- run examples/bubble_sort.alice --emit-cs artifacts/bubble_sort.g.cs
```

传递参数给 Alice 程序（如果 `main(args: string[])`）：

```bash
dotnet run --project src/alicec -- run examples/bubble_sort.alice -- --foo bar
```

## 构建 DLL（build）

```bash
dotnet run --project src/alicec -- build examples/bubble_sort.alice
```

默认输出位置：在源文件同目录生成 `artifacts/` 文件夹，输出 `<basename>.dll`。

也可以用 `-o` 指定输出路径：

```bash
dotnet run --project src/alicec -- build examples/bubble_sort.alice -o artifacts/bubble_sort.dll
```

### （可选）构建 EXE

只要把 `-o` 的扩展名写成 `.exe`，就会编译成控制台 EXE，并自动生成同名的 `.runtimeconfig.json`（框架依赖运行）。
注意：这是 Roslyn 直接生成的 IL 可执行文件，不是 apphost，所以在 Windows 上建议用 `dotnet` 或生成的 `.cmd` 启动。

```bash
dotnet run --project src/alicec -- build examples/bubble_sort.alice -o artifacts/bubble_sort.exe
dotnet artifacts/bubble_sort.exe
# 或
artifacts/bubble_sort.cmd
```

可选：同时输出生成的 C#：

```bash
dotnet run --project src/alicec -- build examples/bubble_sort.alice -o artifacts/bubble_sort.dll --emit-cs artifacts/bubble_sort.build.g.cs
```

## Smoke test

```bash
dotnet build
dotnet run --project src/alicec -- run examples/bubble_sort.alice
```

## Alice v0.2：多文件模块（namespace/import/const）

### 语法

- 文件级命名空间：
  - `namespace A.B`
- 导入模块：
  - `import A.B.C as X`（`as` 可省略；省略时别名为最后一段）
- 顶层全局声明：
  - `const name: Type = expr`（生成 C# `public static readonly`）
  - `name: Type = expr`
  - 顶层禁止 `:=` 与 `var` 推导声明

### 模块根目录推断与 --module-path

默认不传 `--module-path` 时：
1) 根据入口文件的 `namespace` 推断模块根目录（把入口目录向上回退命名空间段数，并校验路径一致性）
2) 同时把入口文件所在目录作为兜底根目录

也可以显式指定模块搜索目录（可重复）：

```bash
dotnet run --project src/alicec -- run examples/mod/Main.alice --module-path examples
```

### 运行多模块示例

```bash
dotnet run --project src/alicec -- run examples/mod/Main.alice
```

期望输出（严格）：

```
[1, 2, 4, 5, 8]
```

## Alice v0.3：面向对象 / 接口 / 异常 / 函数类型与闭包

本版本在 v0.2 基础上新增：

- 顶层 `class` / `interface`
- `new` / `this`
- `try` / `except` / `finally` / `raise`
- 函数类型：`(T1,T2)->R` 与匿名函数表达式：`fun (x:T): R { ... }`

### 类（class）

```alice
class Counter {
  value:int = 0

  public fun Inc() {
    this.value = this.value + 1
  }
}
```

继承与接口列表：

```alice
class Derived : Base, IFoo, IBar {
  public fun M(): int { return 1 }
}
```

成员可见性：默认 private，支持 `public/protected/private`，并支持批量修饰：

```alice
class Point {
  public { x:int; y:int }
}
```

### 接口（interface）与嵌入

```alice
interface IA {
  fun A(): int
}

interface IB {
  IA
  fun B(): int
}
```

编译器会对“被引用到的接口”做隐式实现补全：如果 class 的 public 实例方法集合满足接口方法集合，则自动把该接口补到生成的 C# `: IFoo` 列表中。

### 异常

```alice
try {
  raise "oops"
} except (e:any) {
  print("caught")
} finally {
  print("finally")
}
```

`raise` 不带表达式表示 rethrow，仅允许在 `except` 块内。

### 函数类型 / 匿名函数 / 闭包

函数类型写法：

```alice
fun apply2(a:int, b:int, f:(int,int)->int): int { return f(a, b) }
```

匿名函数表达式：

```alice
f := fun (x:int): int { return x + 1 }
```

闭包捕获由生成的 C# lambda 自然实现。

### 新 CLI

为了减少输入，推荐两条“傻瓜式命令”：

1) 直接运行（自动推断模块根目录，无需写 `run` / `--module-path`）：

```bash
dotnet run --project src/alicec -- examples/oop/Counter.alice
```

2) 仅编译不运行（别名 `compile`）：

```bash
dotnet run --project src/alicec -- compile examples/oop/Counter.alice
```

你也可以在仓库根目录使用脚本进一步缩短：

- Windows：`alicec.cmd`
- Linux/macOS：`alicec.sh`

示例：

```bash
./alicec.sh run examples/oop/Counter.alice
./alicec.sh build examples/oop/Counter.alice
./alicec.sh compile examples/oop/Counter.alice
./alicec.sh selftest

# 额外快捷：省略 run（等价于 run）
./alicec.sh examples/oop/Counter.alice
```

- 仅编译不执行：

```bash
dotnet run --project src/alicec -- check <file.alice> [--module-path <dir> ...] [--emit-cs <path>]
```

- 运行自测：

```bash
dotnet run --project src/alicec -- selftest
```

### v0.3 示例

```bash
dotnet run --project src/alicec -- run examples/oop/Counter.alice --module-path examples
dotnet run --project src/alicec -- run examples/fn/FuncRef.alice --module-path examples
dotnet run --project src/alicec -- run examples/ex/TryExcept.alice --module-path examples
```
