# 路线图 (Roadmap)

我们致力于持续提升 IPTV 观看体验。

## <span style="font-size:1.2em">✨</span> 已完成

### 核心播放

<span style="color:#22c55e">✔</span> **FCC 极速切台** - 毫秒级频道切换，针对 IPTV 深度优化<br>
<span style="color:#22c55e">✔</span> **M3U 播放列表** - 本地/远程支持，UTF-8/GB18030 编码，`#EXTINF` 扩展属性<br>
<span style="color:#22c55e">✔</span> **M3U 二进制缓存** - ETag/Last-Modified 验证，毫秒级加载<br>
<span style="color:#22c55e">✔</span> **EPG 电子节目单** - XMLTV (gz) 解析，央视/教育台类型后缀支持<br>
<span style="color:#22c55e">✔</span> **频道回看 (Catchup)** - 模板自动生成回放 URL，支持时移回看<br>
<span style="color:#22c55e">✔</span> **时移 (Time-Shift)** - 实时拖动回看，节目边界内快进快退<br>
<span style="color:#22c55e">✔</span> **频道管理** - 分组、搜索、收藏、历史，支持分组排序<br>
<span style="color:#22c55e">✔</span> **RTP 直连地址** - UDP 优化，减少网络延迟

### 播放优化

<span style="color:#22c55e">✔</span> **硬件解码** - D3D11VA/DXVA2/NVDEC/软件自动切换<br>
<span style="color:#22c55e">✔</span> **去交错处理** - 1080i/720i 优化，yadif/bwdif 算法<br>
<span style="color:#22c55e">✔</span> **音频设置** - 音量增益、最大音量、音频延迟<br>
<span style="color:#22c55e">✔</span> **倍速播放** - 时移/回放模式 0.5×~5.0×，音高校正<br>
<span style="color:#22c55e">✔</span> **自动换源** - 源失效时自动切换下一源<br>
<span style="color:#22c55e">✔</span> **连接预热** - 预先建立连接，加速换台速度

### 多屏与录制

<span style="color:#22c55e">✔</span> **多屏播放** - 4/6/9 屏幕同时观看，数字键快速选择<br>
<span style="color:#22c55e">✔</span> **本地录播** - 直接录制到本地磁盘<br>
<span style="color:#22c55e">✔</span> **WebDAV 上传** - 录播后自动上传到云端<br>
<span style="color:#22c55e">✔</span> **预约录制** - 前台/后台双模式，定时自动停止

### 界面与交互

<span style="color:#22c55e">✔</span> **深色/浅色主题** - 完美适配 Windows 10/11<br>
<span style="color:#22c55e">✔</span> **全屏悬浮控制** - 鼠标触底显示播放控制条<br>
<span style="color:#22c55e">✔</span> **侧边抽屉** - 频道列表(右)和 EPG(左)<br>
<span style="color:#22c55e">✔</span> **精简模式** - 紧凑窗口形态<br>
<span style="color:#22c55e">✔</span> **系统托盘** - 常驻图标，快速操作菜单<br>
<span style="color:#22c55e">✔</span> **快捷键支持** - 完整的键盘快捷键，快捷键帮助窗口<br>
<span style="color:#22c55e">✔</span> **关闭模式记忆** - 记住退出/最小化到托盘的选择<br>
<span style="color:#22c55e">✔</span> **频道预览** - 悬停显示频道缩略图，支持自定义大小<br>
<span style="color:#22c55e">✔</span> **调试窗口** - 实时日志查看，调试模式开关

### 远程与同步

<span style="color:#22c55e">✔</span> **Web 远程控制** - 浏览器远程操控，支持完整播放控制、回看、预约、录制<br>
<span style="color:#22c55e">✔</span> **预约提醒** - 节目到点通知，支持自动播放策略<br>
<span style="color:#22c55e">✔</span> **多语言** - 简体中文、繁体中文、English、Русский

### 源健康与稳定性

<span style="color:#22c55e">✔</span> **源健康检测** - 后台 HTTP HEAD 探测，实时显示源状态<br>
<span style="color:#22c55e">✔</span> **源状态指示器** - 频道列表显示源健康状态（绿色/红色椭圆）<br>
<span style="color:#22c55e">✔</span> **右键源菜单** - 查看所有源健康状态、延迟、切换源<br>
<span style="color:#22c55e">✔</span> **自动源降级** - 主源失败时自动切换到健康备用源

## <span style="font-size:1.2em">🚧</span> 进行中

<span style="color:#f59e0b">⚙</span> **EPG 状态芯片可点击** - 点击直接回到直播，交互评估中<br>
<span style="color:#f59e0b">⚙</span> **预约通知动画** - 淡入/滑入动效方案已设计

## <span style="font-size:1.2em">📌</span> 未来计划

<span style="color:#6b7280">○</span> **云端录制 (PVR)** - 连接远程存储进行节目录制<br>
<span style="color:#6b7280">○</span> **播放链路优化** - 继续降低切台延迟，优化弱网场景稳定性<br>
<span style="color:#6b7280">○</span> **测试体系扩展** - 补齐播放状态机、录播索引、EPG 同步相关单元测试<br>
<span style="color:#6b7280">○</span> **录播体验增强** - 录制中信息同步、远端元数据一致性
