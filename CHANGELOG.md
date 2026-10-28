# Changelog

## 11.16

### 修复
- 并发 > 4 时窗口句柄串号，导致 A 账号登录动作打进 B 账号的会话（核心修复，`WindowDriver` 改为句柄绑定令牌）。
- 中文/空格路径下安装目录被截断（`DirResolver` 改走 `\\?\` 长路径前缀）。
- 队列最后一行无换行符时被丢弃。

### 变更
- `DAS.Login` 与 `DAS.Verify` 合并为同一宿主，用 `--mode` 固化入口，产物仍是两个独立 exe。
- 日志改为 JSONL，字段对齐 `bench/targets.json` 的 `run_id`。
- 单文件发布开启压缩，体积从 78 MB 降到 50 MB 量级。

## 10.28
- 新增 `--only-ok` 与 `--resume`。
- 状态判定补充 `NeedsHuman`（扫码 / 短信），现场落 `snapshot/`。

## 10.12
- 首个对外版本：队列读取、并发调度、目录三路探测。
