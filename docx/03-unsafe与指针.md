# 03-unsafe 与指针

## 1. 开关与标注

- 代码中使用 `@unsafe` 的函数必须配合编译器开关 `--allow-unsafe`。

```bash
./alice run examples/unsafe/PtrU8Write.alice --allow-unsafe
```

## 2. ptr<T> 与表达式

- 类型：`ptr<T>`（发射到 C# 的 `T*`）
- 取地址：`&x`
- 解引用：`*p`
- 左值赋值：`*p = v`

## 3. mem 模块常用 API

- `mem.alloc(n): mem.NativeBuffer`
- `mem.free(buf)`
- `mem.ptrU8(buf, off): ptr<u8>`
- `mem.ptr<T>(buf, off): ptr<T>`（`T` 需为 unmanaged）
- `mem.memcpyToArray(...)` / `mem.memcpy(...)`
- `mem.memcpyPtr(dst:ptr<u8>, src:ptr<u8>, n:int)`

## 4. 最小示例

见：

- `examples/unsafe/PtrU8Write.alice`
- `examples/unsafe/PtrGenericSmoke.alice`
- `examples/unsafe/MemcpyPtrSmoke.alice`

## 5. 常见坑

- `mem.alloc(n)` 返回 `mem.NativeBuffer`，不要把它直接存到 `ptr<T>` 字段里；应保存 `NativeBuffer`，需要指针时用 `mem.ptr<T>(buf, off)` 获取“视图”。
- 不要在 `@unsafe async fun` 中 `await`（C# 限制，见 README）。
