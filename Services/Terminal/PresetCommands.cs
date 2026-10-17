using System;
using System.Collections.Generic;
using JxTerminal.Models;

namespace JxTerminal.Services.Terminal;

/// <summary>
/// Default pre-seeded Linux/DevOps commands loaded into TinyDb snippets collection on first run.
/// Users can freely view, edit, add, or delete any of these in the Snippets management page.
/// </summary>
public static class PresetCommands
{
    public static List<SnippetItem> GetDefaultSnippets()
    {
        return new List<SnippetItem>
        {
            // --- 目录与文件浏览 ---
            new() { Name = "列出当前目录文件与文件夹", Command = "ls" },
            new() { Name = "以长格式列出所有文件（含隐藏文件）", Command = "ls -la" },
            new() { Name = "易读单位（KB/MB/GB）查看文件详情", Command = "ls -lh" },
            new() { Name = "以长格式列出文件详细信息（权限/大小）", Command = "ls -l" },
            new() { Name = "列出所有文件（含 . 开头的隐藏文件）", Command = "ls -a" },
            new() { Name = "按修改时间倒序排列文件（最新在前）", Command = "ls -lt" },
            new() { Name = "快捷别名：以详细长格式列出文件", Command = "ll" },

            new() { Name = "切换工作目录（默认切换回用户主目录）", Command = "cd" },
            new() { Name = "切换到当前登录用户家目录", Command = "cd ~" },
            new() { Name = "返回上一级父目录", Command = "cd .." },
            new() { Name = "切换回上一次所在的工作目录", Command = "cd -" },
            new() { Name = "切换到系统根目录", Command = "cd /" },

            new() { Name = "显示当前工作目录的完整绝对路径", Command = "pwd" },

            // --- 文件与目录管理 ---
            new() { Name = "创建新目录", Command = "mkdir" },
            new() { Name = "递归创建多级嵌套父子目录", Command = "mkdir -p" },
            new() { Name = "创建空文件或更新已有文件时间戳", Command = "touch" },

            new() { Name = "删除指定文件", Command = "rm" },
            new() { Name = "递归强制删除指定文件或文件夹（谨慎操作）", Command = "rm -rf" },
            new() { Name = "强制删除文件且不弹出任何确认提示", Command = "rm -f" },
            new() { Name = "递归删除文件夹及其所有子内容", Command = "rm -r" },

            new() { Name = "复制指定文件或目录", Command = "cp" },
            new() { Name = "递归复制文件夹及其所有子内容", Command = "cp -r" },
            new() { Name = "复制文件并保留原始时间戳和权限", Command = "cp -p" },
            new() { Name = "全属性归档复制（保留权限/所有者/软链接）", Command = "cp -a" },

            new() { Name = "移动或重命名文件/目录", Command = "mv" },
            new() { Name = "强制移动或覆盖目标同名文件", Command = "mv -f" },

            new() { Name = "修改文件或目录的访问权限", Command = "chmod" },
            new() { Name = "为文件添加可执行权限", Command = "chmod +x" },
            new() { Name = "递归将目录权限设为 755 (rwxr-xr-x)", Command = "chmod -R 755" },
            new() { Name = "递归将文件权限设为 644 (rw-r--r--)", Command = "chmod -R 644" },
            new() { Name = "赋予所有用户完全读写执行权限（慎用）", Command = "chmod 777" },

            new() { Name = "修改文件或目录的所有者和所属用户组", Command = "chown" },
            new() { Name = "递归更改文件夹及其下所有文件的所有者与用户组", Command = "chown -R" },

            // --- 文本查看与处理 ---
            new() { Name = "查看或串联合并输出文件内容", Command = "cat" },
            new() { Name = "查看文件内容并在每行前面显示行号", Command = "cat -n" },

            new() { Name = "查看文件末尾若干行内容", Command = "tail" },
            new() { Name = "实时追踪并持续输出文件最新追加内容", Command = "tail -f" },
            new() { Name = "输出文件末尾最近 100 行记录", Command = "tail -n 100" },
            new() { Name = "实时追踪并先输出末尾 50 行内容", Command = "tail -n 50 -f" },

            new() { Name = "查看文件开头若干行内容", Command = "head" },
            new() { Name = "输出文件开头的 20 行内容", Command = "head -n 20" },

            new() { Name = "分屏翻页查看长文件（支持前后翻动与搜索）", Command = "less" },
            new() { Name = "逐页向下滚动查看长文本文件内容", Command = "more" },

            new() { Name = "在文本文件或管道流中按正则模式搜索文本", Command = "grep" },
            new() { Name = "当前目录递归搜索关键字及行号", Command = "grep -rn \"keyword\" ." },
            new() { Name = "不区分大小写进行文本模式搜索", Command = "grep -i" },
            new() { Name = "反向过滤：排除匹配指定文本模式的行", Command = "grep -v" },
            new() { Name = "使用扩展正则表达式进行模式匹配", Command = "grep -E" },

            new() { Name = "在目录层次结构中搜索文件或文件夹", Command = "find" },
            new() { Name = "按文件名通配符模式搜索文件", Command = "find . -name \"*.log\"" },
            new() { Name = "在当前目录下仅搜索普通文件（排除目录）", Command = "find . -type f" },
            new() { Name = "搜索当前目录下体积大于 100MB 的大文件", Command = "find . -size +100M" },
            new() { Name = "查找最近 7 天内被修改过的文件", Command = "find . -mtime -7" },

            // --- Systemd 与服务管理 ---
            new() { Name = "控制 systemd 系统与服务管理器的核心命令", Command = "systemctl" },
            new() { Name = "查看指定服务运行状态与日志", Command = "systemctl status" },
            new() { Name = "重新启动指定的系统服务", Command = "systemctl restart" },
            new() { Name = "启动指定的系统服务", Command = "systemctl start" },
            new() { Name = "停止指定的系统服务", Command = "systemctl stop" },
            new() { Name = "平滑重载服务配置（不中断服务）", Command = "systemctl reload" },
            new() { Name = "设置指定服务开机自启", Command = "systemctl enable" },
            new() { Name = "关闭指定服务的开机自启", Command = "systemctl disable" },
            new() { Name = "重新加载 systemd 单元配置文件与改动", Command = "systemctl daemon-reload" },
            new() { Name = "检查服务当前是否处于激活运行状态", Command = "systemctl is-active" },
            new() { Name = "列出当前正在运行的所有服务", Command = "systemctl list-units --type=service" },

            new() { Name = "查询 systemd 日志服务日志的核心工具", Command = "journalctl" },
            new() { Name = "查看指定服务单元的专属运行日志", Command = "journalctl -u" },
            new() { Name = "实时跟踪系统最新输出日志", Command = "journalctl -f" },
            new() { Name = "查看系统故障排查详细报错日志", Command = "journalctl -xe" },
            new() { Name = "查看最近 100 行系统日志记录", Command = "journalctl -n 100" },
            new() { Name = "查看今日生成的所有系统日志", Command = "journalctl --since today" },

            // --- Docker 容器与编排 ---
            new() { Name = "Docker 容器引擎核心管理命令", Command = "docker" },
            new() { Name = "列出正在运行的容器", Command = "docker ps" },
            new() { Name = "列出所有容器（含已停止的容器）", Command = "docker ps -a" },
            new() { Name = "列出本地存储的所有镜像", Command = "docker images" },
            new() { Name = "实时跟踪指定容器的标准输出日志", Command = "docker logs -f" },
            new() { Name = "进入正在运行的容器交互式终端", Command = "docker exec -it" },
            new() { Name = "重启指定的容器", Command = "docker restart" },
            new() { Name = "停止运行中的容器", Command = "docker stop" },
            new() { Name = "启动已停止的容器", Command = "docker start" },
            new() { Name = "强制删除指定容器", Command = "docker rm -f" },
            new() { Name = "删除本地指定的镜像", Command = "docker rmi" },
            new() { Name = "后台守护进程模式运行新容器", Command = "docker run -d" },
            new() { Name = "交互式终端模式启动运行新容器", Command = "docker run -it" },
            new() { Name = "基于 Dockerfile 构建镜像并指定标签", Command = "docker build -t" },
            new() { Name = "后台构建并启动 Compose 编排服务", Command = "docker compose up -d" },
            new() { Name = "停止并彻底清理 Compose 容器与网络", Command = "docker compose down" },
            new() { Name = "查看当前 Compose 服务的运行状态", Command = "docker compose ps" },
            new() { Name = "实时查看 Compose 集群聚合日志", Command = "docker compose logs -f" },
            new() { Name = "重启编排集群中的所有服务", Command = "docker compose restart" },
            new() { Name = "列出 Docker 创建的所有虚拟网络", Command = "docker network ls" },
            new() { Name = "列出 Docker 管理的所有数据卷", Command = "docker volume ls" },
            new() { Name = "清理所有未使用的镜像、容器与网络", Command = "docker system prune -a" },

            // --- Git 版本控制 ---
            new() { Name = "分布式版本控制系统核心命令", Command = "git" },
            new() { Name = "查看当前工作区与暂存区修改状态", Command = "git status" },
            new() { Name = "拉取远程分支最新更新并自动合并", Command = "git pull" },
            new() { Name = "推送本地分支最新提交到远程仓库", Command = "git push" },
            new() { Name = "推送到指定的远程仓库分支", Command = "git push origin" },
            new() { Name = "提交暂存区代码并附带简要说明", Command = "git commit -m \"\"" },
            new() { Name = "暂存当前目录下所有修改和新增文件", Command = "git add ." },
            new() { Name = "暂存所有已追踪和未追踪文件的修改", Command = "git add -A" },
            new() { Name = "查看本地所有分支以及远程跟踪分支", Command = "git branch -a" },
            new() { Name = "创建并立即切换到新的工作分支", Command = "git checkout -b" },
            new() { Name = "切换到指定的本地已有分支", Command = "git switch" },
            new() { Name = "查看工作区尚未暂存的代码改动差异", Command = "git diff" },
            new() { Name = "查看已暂存但尚未提交的代码改动差异", Command = "git diff --staged" },
            new() { Name = "单行精简格式查看最近 10 次提交记录", Command = "git log --oneline -n 10" },
            new() { Name = "将未提交的工作区更改临时暂存到堆栈", Command = "git stash" },
            new() { Name = "恢复最近暂存的工作区修改并将其从堆栈移除", Command = "git stash pop" },
            new() { Name = "强制重置工作区到指定提交（丢弃所有修改）", Command = "git reset --hard" },
            new() { Name = "查看当前本地仓库关联的所有远程仓库源及地址", Command = "git remote -v" },
            new() { Name = "从远程 Git 仓库克隆完整代码库到本地", Command = "git clone" },

            // --- Nginx 与 Web 服务器 ---
            new() { Name = "高性能 HTTP 和反向代理 Web 服务器", Command = "nginx" },
            new() { Name = "测试 Nginx 配置文件语法格式与合法性", Command = "nginx -t" },
            new() { Name = "平滑重新加载 Nginx 配置（热重载）", Command = "nginx -s reload" },
            new() { Name = "快速停止 Nginx 主进程", Command = "nginx -s stop" },
            new() { Name = "优雅等待连接处理完毕后再退出 Nginx", Command = "nginx -s quit" },
            new() { Name = "显示 Nginx 版本号以及编译配置与加载模块", Command = "nginx -V" },

            // --- 网络与端口排查 ---
            new() { Name = "查看网络连接、路由表与网络接口统计信息", Command = "netstat" },
            new() { Name = "查看所有正在监听的 TCP/UDP 端口及对应 PID", Command = "netstat -tulnp" },
            new() { Name = "查看所有活动网络连接状态与端口", Command = "netstat -anp" },

            new() { Name = "现代实用网络套接字统计工具（高效替代 netstat）", Command = "ss" },
            new() { Name = "快速查看所有监听中的 TCP/UDP 端口与进程", Command = "ss -tulnp" },
            new() { Name = "查看总体网络连接套接字统计概况", Command = "ss -s" },

            new() { Name = "列出当前系统中被各进程打开的文件与网络端口", Command = "lsof" },
            new() { Name = "查看占用 80 端口的进程与 PID", Command = "lsof -i :80" },
            new() { Name = "查看占用 443 HTTPS 端口的进程与 PID", Command = "lsof -i :443" },
            new() { Name = "查看占用 MySQL 3306 端口的进程与 PID", Command = "lsof -i :3306" },
            new() { Name = "查看占用 SSH 22 端口的进程与 PID", Command = "lsof -i :22" },
            new() { Name = "查看指定 PID 进程打开的所有文件与句柄", Command = "lsof -p" },

            new() { Name = "通过 URL 传输数据的命令行网络请求工具", Command = "curl" },
            new() { Name = "仅获取目标 URL 的 HTTP 响应头信息", Command = "curl -I" },
            new() { Name = "下载远程文件并保存为远端默认文件名", Command = "curl -O" },
            new() { Name = "静默模式运行 curl 不输出进度信息", Command = "curl -s" },
            new() { Name = "发送带有 JSON 头的 POST 请求", Command = "curl -X POST -H \"Content-Type: application/json\" -d '{}'" },
            new() { Name = "显示详细的 HTTP 请求与响应通信调试过程", Command = "curl -v" },

            new() { Name = "非交互式网络文件下载工具", Command = "wget" },
            new() { Name = "断点续传下载大文件", Command = "wget -c" },
            new() { Name = "下载远程文件并保存为指定本地文件名", Command = "wget -O" },

            new() { Name = "向网络主机发送 ICMP ECHO 探测包测试连通性", Command = "ping" },
            new() { Name = "发送 4 次 ICMP 探测包测试网络连通性与延迟", Command = "ping -c 4" },
            new() { Name = "探测数据包到达目标主机经过的路由跳数", Command = "traceroute" },
            new() { Name = "通过 DNS 服务器查询域名解析记录", Command = "dig" },
            new() { Name = "查询域名对应 IP 解析地址", Command = "nslookup" },

            new() { Name = "查看与管理网络设备、路由和隧道的综合工具", Command = "ip" },
            new() { Name = "查看本机所有网络接口与 IP 地址配置", Command = "ip a" },
            new() { Name = "查看当前内核 IP 路由转发表", Command = "ip route" },
            new() { Name = "配置与显示网络接口参数（经典工具）", Command = "ifconfig" },

            // --- 压缩与归档 ---
            new() { Name = "磁带归档与解归档压缩工具", Command = "tar" },
            new() { Name = "打包并压缩为 .tar.gz 格式文件", Command = "tar -czvf" },
            new() { Name = "解压 .tar.gz 压缩包到当前目录", Command = "tar -xzvf" },
            new() { Name = "打包并压缩为 .tar.bz2 格式文件", Command = "tar -cjvf" },
            new() { Name = "解压 .tar.bz2 格式压缩包", Command = "tar -xjvf" },
            new() { Name = "查看压缩包中的文件清单而不解压", Command = "tar -tf" },

            new() { Name = "创建 ZIP 格式压缩文件", Command = "zip" },
            new() { Name = "递归打包压缩目录为 .zip 文件", Command = "zip -r" },
            new() { Name = "解压 .zip 压缩包到当前目录", Command = "unzip" },

            // --- 系统资源与性能排查 ---
            new() { Name = "报告当前运行中的进程状态快照", Command = "ps" },
            new() { Name = "报告系统当前所有运行中进程（BSD 格式）", Command = "ps aux" },
            new() { Name = "报告系统所有运行中进程（标准格式）", Command = "ps -ef" },
            new() { Name = "按关键词过滤查找指定运行进程", Command = "ps aux | grep" },

            new() { Name = "实时全屏动态监控系统进程与资源占用", Command = "top" },
            new() { Name = "增强交互式全屏系统资源与进程监视器", Command = "htop" },

            new() { Name = "报告文件系统磁盘空间使用情况", Command = "df" },
            new() { Name = "以易读的 GB/MB 格式查看磁盘分区容量", Command = "df -h" },
            new() { Name = "查看各挂载磁盘的文件系统类型", Command = "df -T" },

            new() { Name = "查看系统物理内存与交换区使用情况", Command = "free" },
            new() { Name = "以人类易读单位查看内存与交换区使用", Command = "free -h" },
            new() { Name = "以 MB 为单位查看物理内存与 Swap", Command = "free -m" },

            new() { Name = "统计文件或目录占用的磁盘空间大小", Command = "du" },
            new() { Name = "统计当前目录下所有文件/文件夹各自总大小", Command = "du -sh *" },
            new() { Name = "查看当前目录下一级子目录各自体积", Command = "du -h --max-depth=1" },

            new() { Name = "向目标进程发送终止信号", Command = "kill" },
            new() { Name = "发送 SIGKILL 信号强制终止目标进程", Command = "kill -9" },
            new() { Name = "发送 SIGTERM 信号优雅请求退出进程", Command = "kill -15" },
            new() { Name = "按完整命令行匹配强制杀掉关联进程", Command = "pkill -9 -f" },
            new() { Name = "按进程名强制杀掉所有同名进程", Command = "killall" },

            new() { Name = "查看系统已运行时间及 1/5/15 分钟负载", Command = "uptime" },
            new() { Name = "打印当前有效登录用户名", Command = "whoami" },
            new() { Name = "打印当前用户的 UID、GID 与附属组信息", Command = "id" },
            new() { Name = "输出当前 Linux 系统内核与架构详情", Command = "uname -a" },
            new() { Name = "查询或修改主机名与系统环境信息", Command = "hostnamectl" },
            new() { Name = "显示或设置系统当前日期与时间", Command = "date" },

            // --- 防火墙与安全 ---
            new() { Name = "以超级用户 (root) 或其他用户身份执行命令", Command = "sudo" },
            new() { Name = "切换到超级管理员 root 的交互式终端", Command = "sudo su" },
            new() { Name = "完全切换到 root 环境（包含全部环境变量）", Command = "su -" },

            new() { Name = "Ubuntu 简单防火墙管理工具", Command = "ufw" },
            new() { Name = "查看 Ubuntu 简易防火墙规则状态", Command = "ufw status" },
            new() { Name = "放行指定端口（例如 ufw allow 80/tcp）", Command = "ufw allow" },
            new() { Name = "重新加载防火墙规则", Command = "ufw reload" },

            new() { Name = "Firewalld 动态防火墙命令行管理工具", Command = "firewall-cmd" },
            new() { Name = "查看 RHEL/CentOS 防火墙已开放端口与规则", Command = "firewall-cmd --list-all" },
            new() { Name = "重新加载防火墙配置", Command = "firewall-cmd --reload" },

            new() { Name = "Linux 内核包过滤与防火墙规则管理工具", Command = "iptables" },
            new() { Name = "列出当前所有 iptables 过滤规则详情", Command = "iptables -L -n -v" },

            // --- 软件包管理与远程运维 ---
            new() { Name = "Debian/Ubuntu 软件包管理工具", Command = "apt" },
            new() { Name = "更新本地 APT 软件源索引列表", Command = "apt update" },
            new() { Name = "升级系统中所有已有软件包到最新版", Command = "apt upgrade -y" },
            new() { Name = "安装指定软件包并自动确认", Command = "apt install -y" },

            new() { Name = "RHEL/CentOS 软件包管理器", Command = "yum" },
            new() { Name = "升级 RHEL/CentOS 系统所有软件包", Command = "yum update -y" },
            new() { Name = "安装指定 RPM 软件包", Command = "yum install -y" },

            new() { Name = "Fedora/CentOS 8+ 新一代软件包管理工具", Command = "dnf" },
            new() { Name = "通过 DNF 包管理器安装软件包", Command = "dnf install -y" },

            new() { Name = "提交、编辑或列出用户计划定时任务", Command = "crontab" },
            new() { Name = "查看当前用户的计划定时任务列表", Command = "crontab -l" },
            new() { Name = "编辑当前用户的计划定时任务", Command = "crontab -e" },

            new() { Name = "安全远程 Shell 客户端", Command = "ssh" },
            new() { Name = "通过 SSH 连接指定端口的远端主机", Command = "ssh -p 22" },
            new() { Name = "指定公钥私钥文件认证连接远端", Command = "ssh -i" },

            new() { Name = "基于 SSH 的安全文件拷贝工具", Command = "scp" },
            new() { Name = "通过 SSH 递归拷贝本地文件夹到远程", Command = "scp -r" },

            new() { Name = "快速增量远程文件与目录同步工具", Command = "rsync" },
            new() { Name = "归档压缩并显示实时进度增量同步数据", Command = "rsync -avz --progress" },

            new() { Name = "功能强大的全屏终端文本编辑器", Command = "vim" },
            new() { Name = "经典全屏终端文本编辑器", Command = "vi" },
            new() { Name = "简易直观的终端文本编辑器", Command = "nano" },

            new() { Name = "清空当前终端屏幕显示", Command = "clear" },
            new() { Name = "打印用户在 Shell 执行过的历史命令", Command = "history" },
            new() { Name = "输出指定的文本字符串或环境变量值", Command = "echo" },
            new() { Name = "显示可执行程序的完整绝对路径", Command = "which" },
            new() { Name = "定位可执行文件、源代码与帮助手册路径", Command = "whereis" },
            new() { Name = "查看指定命令的联机帮助参考手册", Command = "man" },
            new() { Name = "在当前 Shell 环境中执行脚本文件", Command = "source" },
            new() { Name = "设置或导出环境变量供子进程使用", Command = "export" },
            new() { Name = "定义或查看 Shell 快捷别名", Command = "alias" },
            new() { Name = "重新启动操作系统", Command = "reboot" },
            new() { Name = "立即关闭计算机系统", Command = "shutdown -h now" }
        };
    }
}
