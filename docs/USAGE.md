# 使用说明

## 验号

```powershell
DAS.Verify-11.16-x64.exe --queue accounts.txt --workers 8 --timeout 45 --report out\report.csv
```

报告列：`user,status,attempts,elapsed_ms,tag,note`。

状态含义：

| status | 含义 | 建议 |
|---|---|---|
| `ok` | 可正常登录 | 交给上号器 |
| `badcred` | 账号或密码错误 | 剔除 |
| `needs_human` | 需要扫码/短信 | 人工处理后 `--resume` |
| `blocked` | 被风控或限制 | 换环境/换时间重试 |
| `unknown` | 超时或未预期 | 单独跑一次看日志 |

## 上号

```powershell
DAS.Login-11.16-x64.exe --queue out\report.csv --only-ok --workers 2 --game "三角洲行动" --emit-dir out\dirs.json
```

- `--workers` 建议 ≤ 4，窗口操作对并发敏感；
- `--emit-dir` 输出的 `dirs.json` 可直接给下游启动器消费；
- 想只定位目录不登录：`--resolve-only`。

## 断点续跑

```powershell
DAS.Verify-11.16-x64.exe --queue accounts.txt --resume
```

`state.json` 里已完成（`ok`/`badcred`）的账号会跳过，`needs_human` 会重试。

## 日志

```powershell
Get-Content logs\run-*.jsonl | ConvertFrom-Json | Group-Object status | Select-Object Name,Count
```

## 退出码

| 码 | 含义 |
|---|---|
| 0 | 全部处理完成 |
| 2 | 参数错误 |
| 3 | 队列为空或不可读 |
| 4 | 有账号处于 `needs_human` |
