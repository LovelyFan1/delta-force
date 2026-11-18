# 11.16 启动链协议基线 · 研究线索

> 内部研究记录，按"线索"编号（L1…）。每条只写**实际执行并留下证据**的结论，推测标注「推测」。
> 对象：`DAS.Login-11.16`（启动器）、`DAS.Verify-11.16`（校验器）、rail 启动链。
> 复现环境见 `bench/targets.json` 的哈希与 `docs/USAGE.md` 的参数表。

---

## L1 · 网络帧有两代格式（置信度：高）

旧帧：

```
EMSG0001 | u64 payloadLen | u32 payloadLen | payload
```

新分块帧：

```
EMSG0001 | u32 total | u32 offset | u32 chunkLen | chunk
```

证据：一次 10231 字节的响应被切成两块，`offset=0/len=8192` + `offset=8192/len=2039`。
坑：拿 `u64@8` 去比 `u32@16` 会一直 `bad_header`，新协议必须按 total/offset/chunkLen 解析。

---

## L2 · 长度即指纹（置信度：高）

| 长度 | 含义 |
|---|---|
| 10231 | 逻辑帧 20 + 10211 |
| 10207 | `XRPC2R` 响应体 |
| 5072 | 授权材料体（旧） |
| 1391 / 1392 | LoginByQq 请求 明文 / 密文 |
| 2080 / 2096 | GetGameTicket 请求 明文 / 密文 |
| 2510 | LoginByQq 响应 JSON |
| 739 / 669 | GetGameTicket 成功 / 失败响应 |

抓包时先看长度就能判定是哪一层，比看内容快。

---

## L3 · 请求体是 AES-CBC + PKCS7 满块填充（置信度：高，可复现）

```
AES-CBC(1391, key, IV) == 1392
AES-CBC(2080, key, IV) == 2096
```

两次实测会话的 key/IV（完整向量不入库，仅保留首尾）（**每次会话变化**，仅作向量）：

```
LoginByQq     key=5ef11fba…e36c   iv=34cc8f7c…f91c
GetGameTicket key=974db775…60d4   iv=96e334db…d03f
```

下一步：确认 key/IV 是从哪一层导出的（推测与登录响应里的 `sk` 字段有关），当前未验证。

---

## L4 · 字段映射（置信度：高，同会话实测）

```
st        == qq_info.sig
ct        == LoginByQq.ct
dt        == LoginByQq.dt
user_id   == uin
tgpid     == tgp_id
```

2080 明文里的固定项：`game_id=2001918`、`mibao_cmd=23`、`need_game_ticket_type=1`、`pf=union-10153-1`、`version_id=4246`、`wg_client_ver=6.7.1.10221`。

---

## L5 · 5072 材料体不是协议包，是"信封"（置信度：高）

```
5072 密文 --BCryptDecrypt--> 5072 Base64 文本 --Base64--> 2480 / 2512 JSON
```

JSON 内含 `openid / ct / ctid / ctt / sk / qq_info / biz_ticket / dt`。
线索：把 5072 当"登录包"去解析一定失败，它只是材料，真正字段在 Base64 之后。

---

## L6 · `login_info.sig` 的位置定了，生成方未定（置信度：位置高，算法未知）

- 位置：LoginByQq **1391 明文 offset 533**，长度 **32 个 hex 字符**；
- 样本：`C6E286C5…1FD3`、`7B8441AF…1A6B`、`371D9271…4B9F`（三条不同会话）；
- 已排除：响应字段直出、`lct/luid/openid/appid` 的 MD5/SHA、已知 key 的 HMAC、`MD5String` 硬件指纹、`BCryptFinishHash` 当前输出；
- 动态监控：对 `buffer+533` 下 `MemoryAccessMonitor`，**只见 read 不见 write** → 缓冲区在 `BCryptEncrypt` 之前就构造完了，监控挂晚了。

关键字符串（存在但无直接 xref）：`0xa400c "login_info":{"qq_info_type":4,"appid":`、`0xa4034 "sig":"`、`0xa4134 "sig must be 32 hex"`、`0xa41f0 "sig"`、`0xa4210 "auth_openid"`。
.xtext 全段搜 xref 均为 0 → 推测逻辑在 `.vmps0/.vmps1` 或运行时解密段。

下一步：把断点前移到 1391 构造入口（`0x43e18c` 之前），或者在 `0x462290` 的调用者上设硬件断点读栈参。

---

## L7 · 调用栈与 `0x43ddc0` 的真实结构（置信度：高）

```
1391: 0x4639e7 → 0x4637ce → 0x462df3 → 0x43e191 → 0x41bf37 → 0x41eafe → 0x40ab28
2080: 0x4639e7 → 0x4637ce → 0x462df3 → 0x4295c2 → 0x429118
```

- `0x4639e7` 是 BCrypt/Base64 包装层（运行时 dump 证明是正常 x86，不是 VM 乱码），内部调 `0x46e47e`；
- `0x43ddc0` 才是请求构造函数的真 prologue：

```
43ddc0 push ebp
43ddc1 mov  ebp, esp
43ddd1 sub  esp, 0x554
```

- `0x43e18c` 调 `0x462290`（结构/字符串构造器），压栈顺序 `[ebp-0x460], ecx, edx, eax`。

线索：`0x43ddc0` 的 `0x554` 栈帧里应该就有 sig/ct/dt 的落点——只要能在进函数前读到栈参。

---

## L8 · 反调试：卡在 VMProtect 完整性检查（置信度：高，本节点未过）

现象：x32dbg F9 后弹

```
File corrupted! This program has been manipulated and maybe
it's infected by a Virus or cracked. This file won't work anymore.
```

弹窗控件：`class #32770` + Static 文本 + 「确定」按钮；`0x43ddc0` 未执行到。

已试无效（逐条实测）：

| 手段 | 结果 |
|---|---|
| x32dbg + ScyllaHide（VMProtect profile） | 仍弹 File corrupted |
| ScyllaHide 调 `NtGetContextThread/NtSetContextThread/NtSetInformationThread/Peb` | 仍弹 |
| 独立 `InjectorCLI` 注入 `HookLibraryx86.dll` | 仍弹 |
| `MessageBoxW / MessageBoxTimeoutW` 断点 | 能断下 |
| `asm MessageBoxW "mov eax,1; ret 0x10"` | 弹窗消失，随后 `ACCESS_VIOLATION / DEP Violation` |
| headless 后 attach | 曾进入登录，但 TLS/访问违例 |
| 先注入再 attach | headless 卡住 |
| cdb attach | `Cannot debug pid` / `No runnable debuggees` |

结论：**用户态 ScyllaHide + MessageBox 补丁不足**。
下一步（按代价排序）：内核态隐藏（TitanHide/HyperHide）→ patch 完整性检查分支 → dump 后静态。

---

## L9 · rail 走命名管道，不是 TCP（置信度：中高）

```
\\.\pipe\rail_base_pipe<GUID>          由 df_helper_main.exe 创建
pipe_tgp_host_tcls_core / pipe_tgp_guest_tcls_core   存在，但 CreateFile → 错误 5（拒绝访问）
```

- 直接往 `rail_base_pipe` 发裸 `RailGameMessage`：`ReadFile 233`（管道另一端无进程）→ 说明它不吃裸消息；
- `df_helper_main.exe` 里的日志串：`OnPipeData, recv data too small, header_size:{}, recv_size:{}` / `OnPipeData, data truncation - max body size:{}, size in header:{}` / `OnPipeData, recv data - cmd:{}, data:{}`；
- 静态推出的帧布局：**header 0x6C = 108**、`body_size @ data+0xC`、`cmd @ data+0x2C`、`body @ data+0x6C`；
- 按此构造仍被关：还有 magic / checksum / session 字段没找到；
- 命令名：`sys_start_game`、`sys_switch_zone`、`sys_zone_change`、`sys_heartbeat_cmd`。

坑：`rail_id` 不是管道名（`rail_pipe_172938…` 连不上），真实名是 `rail_base_pipe<GUID>`。

---

## L10 · RailGameMessage 可解析、可回写（置信度：高）

```
u64 length = 9351
protobuf:
  field 1  cmd = 100 (RAIL_TG_GAME_INITIALIZE_INFO)
  field 2  RailGameInitializeInfo (9346)
```

关键字段：`rail_ip=www.wegame.com.cn`、`rail_port=80`、`game_id=2001918`、`rail_id=<u64>`、`uin=<uin>`、`tgp_id=<tgp_id>`、`field 61 game_original_account_info{1 open_id, 2 access_token}`、`field 69 ctt`、`field 74 client_token`、`field 75 str_ctid`。

`parse → serialize` 逐字节回环通过，可作为后续对比基线。

---

## L11 · 同进程复用成立，但不稳定（置信度：中）

成功路径：授权通过 → 关游戏、不关启动器 → 再点开始 → **不重新校验** → 游戏再次启动。

状态机时序（一次成功）：`T18 成功` → `C9 就绪` → `R1 获取票据` → `R4 启动游戏` → `R18 成功`。

边界：冷启动无授权 = 0；重开偶发失败，重试可恢复。**不能当作稳定免授权路径**。

---

## L12 · 已证伪：这些路线不要再走（置信度：高）

```
验号器 JSON 直接替换 st/ct/dt  → 上号失败（0%）
有效授权 + 旧会话 raw          → 失败
无效授权 + 有效 raw            → 失败
10231 响应整体替换             → 失败
外部 POST LoginByQq            → 8000119
外部 POST GetGameTicket        → 短 _ep
rail_id 当管道名               → 连不上
MessageBoxW 补丁绕反调试       → 访问违例 / DEP
writeUtf8String 写 2080        → 写 NUL 破坏缓冲区
```

同会话对照才有效：`control`（原样精确重写 2080）成功、`candidate`（只换字段）失败，才能归因于会话绑定。

---

## L13 · 操作层经验（都是踩出来的）

1. **等待时间**：5–7 秒就关掉会误判授权失效；至少等 25–30 秒到终态。成功时间线 `t=2.5 获取票据 → t=5.5 启动游戏 → t=22.0 成功`。
2. **样本被杀软删**：解压后 `Test-Path` 一度为 True，随后文件消失，`Popen` 报 `WinError 2`。对策：解到纯 ASCII 路径、解压后立刻拉起；静态分析用 7-Zip 流式提取成 `.bin`（但 `.bin` 不能直接执行）。
3. **Frida 别 hook 本体代码**：`0x43ddc0 / 0x462290 / 0x43e18c` 全部 `TransportError: connection closed`；只 hook `bcrypt!BCryptEncrypt/Decrypt/ExportKey`。
4. **写缓冲区用 `writeByteArray`**：`writeUtf8String` 会补 NUL，破坏定长缓冲区。
5. **内存 scan 会骗人**：`Memory.scanSync` 在 1391 buffer 内也返回 0，不能据此断言"没有副本"。
6. **UI 自动化**：Qt 窗口的命令栏不是普通 Win32 控件，全局 F9/Ctrl+G 不可靠；输入用真实点击 + 剪贴板粘贴。

---

## L14 · 待解问题与判定标准

| 问题 | 判定标准 |
|---|---|
| 能否执行到 `0x43ddc0` | 断点命中，且能读到 `[ebp-0x460]` 与 `ecx/edx/eax` |
| `sig` 谁生成 | 命中构造点；若客户端生成，给出派生式并用 1391 样本验证 |
| `Tgp_mediator` 完整帧 | `sys_start_game` 能被 `df_helper_main` 接受并回执 |
| 免授权冷启动 | 只有全链路成功才算；否则输出完整否定报告 |

---

## 附：产物索引

```
re/launcher_vmp.bin                  静态样本（7z 流式提取）
re/auto_runs/launcher_mem_4639e7.bin 0x4639e7 附近运行时 dump
re/auto_runs/text_runtime.bin         .text 段 640,058 字节
re/auto_runs/rail_game_message.bin    RailGameMessage 原始帧
re/auto_runs/rail_game_message_rebuilt.bin  回写结果
re/hook_*.js                         Frida 脚本（bcrypt 层）
re/rail_proto_full.py / rail_build.py 管道帧构造与解析
re/card_tests/*_capture/             1391/2080 明文、key/IV、control/candidate 对照
```
