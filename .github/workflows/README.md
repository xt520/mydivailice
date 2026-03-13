# GitHub Actions

## package-compiler

- 手动触发：Actions → package-compiler → Run workflow
- 标签触发：推送 tag `v*`（例如 `v0.1.0`）后会自动打包并发布 Release

产物：

- `artifacts/alicec-dist-<rid>-framework.zip`
- `artifacts/alicec-dist-<rid>-selfcontained.zip`
