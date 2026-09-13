# 华为 MatePad 11 综合工具箱（MatePadToolbox）

面向华为 MatePad 11（**DBY-W09**）的一站式刷机工具：安装驱动、9008 全分区备份、解锁 BL、Root、刷入第三方/官方系统、降级救砖，全部集成在一个原生 WinUI 3 界面中。

本项目由酷安 **@某贼** 的高通工具箱 bat 脚本框架原生重写为 C# / .NET 8，去掉了记事本确认弹窗与命令行黑框，流程逻辑与原版脚本保持一致。

## 功能一览

| 功能 | 说明 |
| --- | --- |
| 安装驱动程序 | 一键安装 ADB 驱动与高通 9008（EDL）驱动 |
| 9008备份 | 9008 模式回读设备全部分区镜像，生成 `images/`、`gpt_and_xml/` 与 `flash_all.bat` 一键回刷脚本；默认跳过 userdata、last_parti、mindows* 等超大/特殊分区，与原版一致 |
| 解锁BL | 鸿蒙 2-3 自动经 ADB 进入 9008 刷入解锁 abl；鸿蒙 4 需工程线/短接进入 9008，或先降级 |
| Root | Magisk 方式（刷 ramdisk）与 APatch 方式（自动经 9008 提取 boot → kptools 修补 → 刷回） |
| 下载第三方系统/底包 | 从 Gitee 获取鸿蒙 2.0 / 3.0 / 4.0 / 4.2 / 4.3 第三方系统列表与底包下载链接 |
| 刷入第三方系统 | Fastboot / 9008 双模式：刷底包、去 AVB 校验、刷 super.img、一键执行 |
| 刷官方系统(救砖) | 解压官方升级包 ZIP 并递归刷入全部分区镜像 |
| 降级系统/回锁BL | 旧版华为手机助手 + HiSuite Proxy + 修改系统时间，实现版本回退或重新上锁 |
| ADB命令行 | 内置命令行终端，工作目录为 `tools/` |
| 设备管理器 | 快捷打开 Windows 设备管理器 |

每个功能页面右上角带有故障排查按钮，遇到问题可一键采集诊断信息。

## 支持设备

- 仅支持 **DBY-W09**（华为 MatePad 11）
- 系统：HarmonyOS 2 / 3 / 4（鸿蒙 4 解锁与 Root 可能需要先降级或使用工程线）

## 运行环境

- Windows 10 1809（内部版本 17763）及以上 / Windows 11，x64
- 自包含单文件发布，**无需安装 .NET 运行时**
- 一条可靠的数据线；进入 9008 模式的功能在鸿蒙 4 下可能需要工程线（普通线剪断 D+/D- 或测试点短接）

## 外部工具（不随源码分发）

因版权与分发限制，以下第三方工具**不包含在本仓库中**，需按原 bat 版目录结构自行放入程序根目录。主界面的「检查核心工具」按钮可校验必备文件是否齐全。

```
tools/
├── adb.exe                        # Android platform-tools
├── fastboot.exe                   # Android platform-tools
├── 7z.exe                         # 7-Zip 命令行版
├── QSaharaServer.exe              # Qualcomm Sahara 协议（9008 上传 firehose）
├── fh_loader.exe                  # Qualcomm Firehose 加载器（9008 读/写）
├── ptool.exe                      # 可选：9008 备份后生成 gpt_and_xml/create
├── driver/
│   ├── adbdriver.exe              # ADB 驱动安装器
│   └── qcdriver.exe               # 高通 9008 驱动安装器
├── apatch_tools/
│   ├── kptools.exe                # APatch 修补工具
│   └── kpimg-android              # APatch 内核镜像
├── HiSuiteProxy/
│   ├── HiSuite1.exe               # 旧版华为手机助手
│   └── HiSuite Proxy V3.exe       # 助手代理（用于降级）
├── apks/
│   ├── Magisk.apk
│   └── Apatch.apk
└── rawprogram/                    # 9008 刷机描述文件
    ├── rawprogram0-5.xml
    ├── rawprogram_vbmeta.xml
    ├── rawprogram_super.xml
    └── misc.img
unlock/
├── Huawei865870_devprg.elf        # 9008 firehose（高通 865870 平台）
└── Huawei865870_abl_unlock.img    # 解锁 abl 镜像
vbmeta/
└── vbmeta.zip                     # 空 vbmeta（去 AVB 校验用）
```

其余 `backup/ cache/ tmp/ out/ base/ ramdisk/ log/ screenshots/ system/` 等目录为运行时自动创建。

## 从源码构建

前置条件：[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（命令行构建无需 Visual Studio；使用 VS 2022 需安装「.NET 桌面开发」工作负载，Windows App SDK 依赖会自动还原）。

```bash
git clone <本仓库地址>
cd 华为MatePad11工具箱-WinUI3源码/华为MatePad11工具箱-WinUI3

# 构建（WinUI 3 不支持 AnyCPU，需指定平台）
dotnet build MatePadToolbox/MatePadToolbox.csproj -c Release -p:Platform=x64

# 自包含单文件发布
dotnet publish MatePadToolbox/MatePadToolbox.csproj -c Release -p:Platform=x64 -r win-x64 -o publish
```

支持 `x86` / `x64` / `ARM64` 三个平台，日常使用推荐 `x64`。

## 9008 备份输出说明

备份完成后在 `backup/QCTool_ParReadback_<时间戳>/` 下生成：

| 路径 | 内容 |
| --- | --- |
| `images/` | 全部分区镜像（默认跳过分区除外） |
| `gpt_and_xml/orig/` | 设备 GPT 原始备份（`gpt_main*.bin`、`gpt_backup*.bin`）、`rawprogram*.xml`、`partition.xml` |
| `gpt_and_xml/create/` | `ptool.exe` 生成的可刷回 GPT 与 rawprogram/patch XML（需 `tools/ptool.exe`） |
| `flash_all.bat` | fastboot 一键回刷脚本 |

默认不回读的分区：`userdata`、`last_parti`、`mindowsesp`、`mindowswin`、`mindowsdat`。

## 风险提示

- 解锁 BL、刷机、9008 深度读写**都会清空数据**，且存在变砖风险，操作前请务必备份并确保电量充足
- 本工具仅面向 DBY-W09，请勿在其他设备上使用
- 刷机过程中请勿拔线、请勿让电脑休眠
- 仅供个人学习与研究使用，因使用本工具造成的任何后果由使用者自行承担

## 致谢

- 酷安 **@某贼** — 原高通工具箱 bat 脚本框架
- Qualcomm — QSaharaServer / fh_loader / ptool
- [APatch](https://github.com/bmax121/APatch)（kptools）与 [Magisk](https://github.com/topjohnwu/Magisk)
- [7-Zip](https://www.7-zip.org/)、Android platform-tools、HiSuite Proxy

## 开源协议

本项目源代码以 [GPL-3.0-or-later](./LICENSE) 协议开源；随程序分发使用的第三方工具（adb、fastboot、7z、QSaharaServer、fh_loader、ptool、kptools 等）版权归各自所有者所有。
