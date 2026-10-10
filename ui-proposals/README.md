# 源匣 SrcBox · 三套界面外观方案

同一个原型工程的三种外观方案，每套都按「不同界面 × 深浅两种颜色」截图。
所有截图都是 **1440×1000**、窗口居中，三套用的是同一份演示数据（同样 18 个频道、同样的演示节目单），可以直接并排比较。
截图里的数值都是演示用的默认值，不代表你的真实配置。

> 三套方案**只改外观**：功能集合、界面文案、快捷键完全一致，不新增功能，也不删功能。
> 设计依据、色值令牌与尺寸清单见 [`design-spec.md`](./design-spec.md)。

## 可交互原型（下载后用浏览器打开）

| 方案 | 原型文件 |
|---|---|
| **A** · 影音工作台（三栏常驻） | [`v1-workbench.html`](./v1-workbench.html) |
| **B** · Screenbox 方向（NavigationView） | [`v2-screenbox.html`](./v2-screenbox.html) |
| **C** · Cinema First（无框架） | [`v3-cinema.html`](./v3-cinema.html) |

三个原型都是自包含的单文件 HTML（无外部依赖、无网络请求）。

## A · v1-workbench — 影音工作台（三栏常驻）

左侧节目指南 / 中间视频 / 右侧频道列表 + 底部传输条，常驻不收起。

| 文件 | 界面 | 颜色 |
|---|---|---|
| `v1-workbench/01-main-dark.png` | 主界面（未选频道） | 深色 |
| `v1-workbench/02-main-light.png` | 主界面 | 浅色 |
| `v1-workbench/03-playing-dark.png` | 播放中（选中广东新闻） | 深色 |
| `v1-workbench/04-playing-light.png` | 播放中 | 浅色 |
| `v1-workbench/05-settings-dark.png` | 设置对话框（8 个分类） | 深色 |
| `v1-workbench/06-settings-light.png` | 设置对话框 | 浅色 |

## B · v2-screenbox — Screenbox 方向（NavigationView）

48px 顶栏 + 左侧导航栏 + 库视图，播放页是覆盖式浮层。

| 文件 | 界面 | 颜色 |
|---|---|---|
| `v2-screenbox/01-home-dark.png` | 主页（含播放横幅 + 频道卡片墙） | 深色 |
| `v2-screenbox/02-home-light.png` | 主页 | 浅色 |
| `v2-screenbox/03-player-dark.png` | 播放页（浮层控制条常显） | 深色 |
| `v2-screenbox/04-player-light.png` | 播放页 | 浅色 |
| `v2-screenbox/05-channels-dark.png` | 频道列表页（分组筛选 + 搜索） | 深色 |
| `v2-screenbox/06-guide-light.png` | 节目指南页 | 浅色 |
| `v2-screenbox/07-settings-dark.png` | 设置页（8 个分类） | 深色 |
| `v2-screenbox/08-settings-light.png` | 设置页 | 浅色 |

## C · v3-cinema — Cinema First（无框架）

没有常驻框架：悬浮缎带 + 悬浮控制坞，其余都是覆盖式面板。

| 文件 | 界面 | 颜色 |
|---|---|---|
| `v3-cinema/01-main-dark.png` | 主视图（播放中） | 深色 |
| `v3-cinema/02-main-light.png` | 主视图 | 浅色 |
| `v3-cinema/03-wall-dark.png` | 浏览面板（聚光卡 + 卡片墙） | 深色 |
| `v3-cinema/04-wall-light.png` | 浏览面板 | 浅色 |
| `v3-cinema/05-epg-dark.png` | 节目指南（18 频道横向时间轴） | 深色 |
| `v3-cinema/06-epg-light.png` | 节目指南 | 浅色 |
| `v3-cinema/07-settings-dark.png` | 设置面板（7 个分段 / 103 项） | 深色 |
| `v3-cinema/08-settings-light.png` | 设置面板 | 浅色 |
| `v3-cinema/09-recording-dark.png` | 录制面板（录播列表 + 上传队列） | 深色 |
| `v3-cinema/10-minimized-dark.png` | 最小化状态（收成标题条） | 深色 |

## 三套的结构差异（一眼看懂）

| | A · 影音工作台 | B · Screenbox 方向 | C · Cinema First |
|---|---|---|---|
| 常驻框架 | 三栏固定分栏 | 48px 导航栏 | 无（全部悬浮） |
| 频道浏览 | 右侧抽屉列表 | 导航页网格 | 覆盖式「浏览」面板（聚光卡 + 卡片墙） |
| 节目指南 | 左侧竖向列表 | 独立页竖向列表 | 多频道横向时间轴（18 条频道道 + 时间标尺 + 现在线） |
| 播放控制 | 底部固定传输条 | 浮层底部控制条 | 悬浮控制坞（播放中收成一条进度轨，鼠标移动展开） |
| 设置 | 模态对话框 | 导航页分栏 | 覆盖面板（顶部分段 + 瀑布流卡片） |
| 视觉材料 | 极弱描边 + 色调差 | Mica 分层 + Acrylic | 毛玻璃浮层 + 发丝描边 + 单一强调色 |

## 复现方式

截图由原型工程里的 `.shoot.ps1` 批量生成：它把每个状态注入到原型 HTML 的临时副本里，
再逐个导出 PNG。想换界面或加一张，改脚本里的 `$shots` 数组即可。

原型本体即本目录下的 `v1-workbench.html`（A）、`v2-screenbox.html`（B）、`v3-cinema.html`（C）。
