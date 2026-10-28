# 架构

## 分层

```
DAS.App       ── CLI 参数、进度渲染、退出码
DAS.Core      ── Queue / Scheduler / AccountStateMachine / StateStore / JsonlLogger
DAS.Platform  ── WindowDriver / ProcessGuard / RegistryProbe / DirResolver
```

依赖方向单向向下，`DAS.Platform` 不引用 `DAS.Core`，避免平台细节污染调度逻辑。

## 队列

`QueueEngine` 是流式读取器：一次取一行，解析为 `Account`，交给调度器；不预读全表，百万行队列常驻内存保持在几十 MB 以内。

支持三种格式，按扩展名分派：

- `.txt`：`user----pass`，`#` 开头为注释；
- `.csv`：表头 `user,pass,tag`；
- `.json`：对象数组，字段同上。

## 调度

- 并发由 `--workers` 限定，内部是 `SemaphoreSlim`；
- 全局速率由 `--rate`（QPS）令牌桶限流，默认不限；
- 单账号超时 `--timeout`，超时走 `Unknown` 并记录耗时；
- 失败重试指数退避：`1s → 2s → 4s`，最多 3 次，仅对 `Transient` 类错误生效。

## 状态机

```
Pending ──► Running ──┬──► Ok
                      ├──► BadCred
                      ├──► NeedsHuman
                      ├──► Blocked
                      └──► Unknown
```

`NeedsHuman` 会把当前窗口截图、句柄、时间戳写进 `snapshot/`，并在 `state.json` 标记，方便人工处理完 `--resume`。

## 持久化

- `state.json`：每账号一条 `{user_hash, status, attempts, last_error, ts}`，只存账号哈希不存明文；
- `logs/run-<run_id>.jsonl`：逐条事件，字段固定，便于用 `jq` 做统计；
- `dirs.json`：`game → install path` 映射缓存。

## 平台层

- `WindowDriver`：句柄绑定 + 消息投递，所有操作前校验句柄归属，修复 11.16 的串号问题；
- `ProcessGuard`：进程树快照与清理，保证一个账号失败不留孤儿进程；
- `RegistryProbe` / `DirResolver`：见 README「安装目录是怎么定位的」。
